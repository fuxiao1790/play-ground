using System.Collections.Generic;
using PlayGround.Attack;
using PlayGround.Common;
using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayGround.System.Aoe
{
    public sealed class AoeRoot : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private const float AoeRenderZ = -0.2f;
        private const int AoeRenderQueue = (int)RenderQueue.Transparent + 45;
        private static readonly ProfilerMarker SyncTargetsProfilerMarker = new("AoeRoot.SyncTargets");
        private static readonly ProfilerMarker SubmitAoesProfilerMarker = new("AoeRoot.SubmitAoes");
        private static readonly ProfilerMarker DrainEventsProfilerMarker = new("AoeRoot.DrainEvents");
        private static readonly ProfilerMarker StepSimulationProfilerMarker = new("AoeRoot.StepSimulation");

        [SerializeField] private int targetMask = 1;
        [SerializeField, Min(0)] private int maximumAoeCount = 10000;
        [SerializeField, Min(0)] private int maximumTargetCount = 100;
        [SerializeField] private bool spawnVisuals = true;
        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly AoeTargetRegistry targetRegistry = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesByType = new();
        private readonly Dictionary<AoeConfig, int> configTypeIds = new();
        private readonly Dictionary<AoeTypeDefinition, int> definitionTypeIds = new();

        private AoeTypeRegistry typeRegistry;
        private AoeTargetSync targetSync;
        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allAoeQuery;
        private EntityQuery submitQuery;
        private NativeArray<CombatRenderElement> submitBuffer;
        private int spawnedAoes;
        private int despawnedAoes;
        private int hitEvents;
        private int activeVisuals;
        private int renderBatches;
        private int nextAoeId;
        private int nextTypeId = 1;
        private bool runtimeReady;

        public event global::System.Action<AoeHitContext> AoeHit;

        public AoeTargetRegistry TargetRegistry => targetRegistry;
        public int TargetMask => targetMask;
        public AoeRuntimeCounters Counters => new(
            ActiveAoeCount(),
            spawnedAoes,
            despawnedAoes,
            hitEvents,
            activeVisuals,
            renderBatches);

        private void Awake()
        {
            runtimeReady = false;
            typeRegistry = new AoeTypeRegistry();
            targetSync = new AoeTargetSync(targetRegistry);
            BindWorld();
            runtimeReady = true;
        }

        private void Update()
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            using (SyncTargetsProfilerMarker.Auto())
            {
                SyncTargetsToEcs();
            }
        }

        private void LateUpdate()
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            using (SubmitAoesProfilerMarker.Auto())
            {
                SubmitAoes();
            }

            using (DrainEventsProfilerMarker.Auto())
            {
                DrainEvents();
            }
        }

        private void OnDestroy()
        {
            if (IsRuntimeReady())
            {
                if (scopeEntity != Entity.Null && entityManager.Exists(scopeEntity))
                {
                    entityManager.DestroyEntity(scopeEntity);
                }

                using NativeArray<Entity> aoeEntities = allAoeQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < aoeEntities.Length; i++)
                {
                    Entity entity = aoeEntities[i];
                    AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entity);
                    if (identity.Scope == scopeEntity)
                    {
                        entityManager.DestroyEntity(entity);
                    }
                }
            }

            runtimeReady = false;

            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            DestroyRenderResources();
        }

        public void Configure(int mask)
        {
            targetMask = mask;
            if (!runtimeReady)
            {
                return;
            }

            typeRegistry = new AoeTypeRegistry();
            configTypeIds.Clear();
            definitionTypeIds.Clear();
            nextTypeId = 1;
            DestroyRenderResources();
        }

        public int RegisterConfig(AoeConfig config)
        {
            if (config == null)
            {
                throw new global::System.ArgumentNullException(nameof(config));
            }

            if (configTypeIds.TryGetValue(config, out int existing))
            {
                return existing;
            }

            int typeId = nextTypeId++;
            configTypeIds[config] = typeId;
            AoeTypeDefinition definition = config.CreateTypeDefinition();
            typeRegistry.Register(typeId, definition);
            TryBuildRenderResource(typeId);
            return typeId;
        }

        public int RegisterType(AoeTypeDefinition definition)
        {
            if (definition == null)
            {
                throw new global::System.ArgumentNullException(nameof(definition));
            }

            if (definitionTypeIds.TryGetValue(definition, out int existing))
            {
                return existing;
            }

            int typeId = nextTypeId++;
            definitionTypeIds[definition] = typeId;
            typeRegistry.Register(typeId, definition);
            TryBuildRenderResource(typeId);
            return typeId;
        }

        public int Spawn(ProjectileAoeSpawnRequest request)
        {
            return Spawn(new AoeSpawnCommand(
                request.EffectTypeId,
                request.Position,
                targetMask,
                request.Damage,
                request.LifetimeSeconds,
                request.TickIntervalSeconds));
        }

        public int Spawn(AoeSpawnCommand command)
        {
            EnsureRuntimeReady();
            if (!typeRegistry.TryGetShape(command.TypeId, out AoeShape shape))
            {
                throw new global::System.InvalidOperationException($"Missing AOE collision definition for type id {command.TypeId}.");
            }

            DynamicBuffer<AoeSpawnRequestElement> spawnRequests =
                entityManager.GetBuffer<AoeSpawnRequestElement>(scopeEntity);
            if (maximumAoeCount <= 0 || spawnRequests.Length >= maximumAoeCount)
            {
                return 0;
            }

            int aoeId = ++nextAoeId;
            spawnRequests.Add(SpawnRequestFor(command, shape, aoeId));
            spawnedAoes++;
            return aoeId;
        }

        public void Step(float deltaTime)
        {
            EnsureRuntimeReady();
            SyncTargetsToEcs();
            using (StepSimulationProfilerMarker.Auto())
            {
                World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimulationSystemGroup>().Update();
            }
            DrainEvents();
        }

        private AoeSpawnRequestElement SpawnRequestFor(AoeSpawnCommand command, AoeShape shape, int aoeId)
        {
            float2 position = new(command.Position.x, command.Position.y);
            float2 halfExtents = new(shape.HalfExtents.x, shape.HalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position,
                shape.Radius,
                halfExtents,
                shape.RotationRadians,
                shape.ShapeType,
                out float2 boundsMin,
                out float2 boundsMax);

            return new AoeSpawnRequestElement
            {
                AoeId = aoeId,
                TypeId = command.TypeId,
                TargetMask = command.TargetMask,
                Lifetime = command.LifetimeSeconds,
                RepeatHitCooldownSeconds = command.TickIntervalSeconds,
                DamageAmount = command.Damage.Amount,
                Radius = shape.Radius,
                RotationRadians = shape.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = shape.ShapeType,
                Render = RenderComponentFor(command.TypeId),
                ProjectileBurst = command.ProjectileBurst
            };
        }

        private void DrainEvents()
        {
            DynamicBuffer<AoeHitElement> hitBuffer = entityManager.GetBuffer<AoeHitElement>(scopeEntity);
            DynamicBuffer<AoeRecycleElement> recycleBuffer = entityManager.GetBuffer<AoeRecycleElement>(scopeEntity);
            hitEvents += hitBuffer.Length;
            despawnedAoes += recycleBuffer.Length;

            for (int i = 0; i < hitBuffer.Length; i++)
            {
                AoeHitElement hit = hitBuffer[i];
                targetSync.TargetsById.TryGetValue(hit.TargetId, out IAoeTarget target);
                var damage = new DamageSnapshot(Mathf.Max(0f, hit.DamageAmount));
                var context = new AoeHitContext(
                    hit.AoeId,
                    hit.TypeId,
                    hit.TargetId,
                    new Vector2(hit.Position.x, hit.Position.y),
                    damage,
                    hit.ProjectileBurst,
                    target);
                AoeHit?.Invoke(context);
                target?.ReceiveAoeHit(damage);
            }

            hitBuffer.Clear();
        }

        private CombatRenderComponent RenderComponentFor(int typeId)
        {
            if (!spawnVisuals || !renderResourcesByType.TryGetValue(typeId, out CombatSpriteRenderResources resources))
            {
                return default;
            }

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new Unity.Mathematics.float2(resources.VisualScale.x, resources.VisualScale.y),
                VisualRotationSin = resources.VisualRotationSin,
                VisualRotationCos = resources.VisualRotationCos,
                RenderZ = AoeRenderZ
            };
        }

        private void BindWorld()
        {
            entityWorld = World.DefaultGameObjectInjectionWorld;
            if (entityWorld == null || !entityWorld.IsCreated)
            {
                entityWorld = new World("PlayGround ECS World");
                World.DefaultGameObjectInjectionWorld = entityWorld;
                var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(entityWorld, systems);
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(entityWorld);
            }

            entityManager = entityWorld.EntityManager;
            scopeEntity = entityManager.CreateEntity(typeof(AoeScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<AoeSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<AoeHitElement>(scopeEntity);
            entityManager.AddBuffer<AoeRecycleElement>(scopeEntity);
            allAoeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>());
            submitQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<AoeActiveTag>());
            submitBuffer = new NativeArray<CombatRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
        }

        private bool IsRuntimeReady()
        {
            return runtimeReady
                && HasValidEcsState();
        }

        private bool EnsureRuntimeAvailable()
        {
            if (IsRuntimeReady())
            {
                return true;
            }

            if (!runtimeReady)
            {
                return false;
            }

            BindWorld();
            return HasValidEcsState();
        }

        private bool HasValidEcsState()
        {
            if (entityWorld == null || !entityWorld.IsCreated || entityManager == default || scopeEntity == Entity.Null)
            {
                return false;
            }

            try
            {
                return entityManager.Exists(scopeEntity);
            }
            catch (global::System.NullReferenceException)
            {
                return false;
            }
        }

        private void EnsureRuntimeReady()
        {
            if (!EnsureRuntimeAvailable())
            {
                throw new global::System.InvalidOperationException($"{nameof(AoeRoot)} on {name} has not finished ECS setup.");
            }
        }

        private void SyncTargetsToEcs()
        {
            DynamicBuffer<CombatTargetElement> targetBuffer = entityManager.GetBuffer<CombatTargetElement>(scopeEntity);
            targetBuffer.Clear();

            IReadOnlyList<AoeTargetSnapshot> snapshots = targetSync.Snapshot();
            int count = Mathf.Min(snapshots.Count, maximumTargetCount);
            for (int i = 0; i < count; i++)
            {
                AoeTargetSnapshot snapshot = snapshots[i];
                float2 targetPosition = new(snapshot.Position.x, snapshot.Position.y);
                float targetRadius = snapshot.Shape.Radius;
                float2 targetHalfExtents = new(snapshot.Shape.HalfExtents.x, snapshot.Shape.HalfExtents.y);
                float targetRotationRadians = snapshot.Shape.RotationRadians;
                CombatShapeType targetShapeType = snapshot.Shape.ShapeType;
                CombatCollisionMath.ComputeWorldBounds(
                    targetPosition,
                    targetRadius,
                    targetHalfExtents,
                    targetRotationRadians,
                    targetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                targetBuffer.Add(new CombatTargetElement
                {
                    TargetId = snapshot.TargetId,
                    TargetMask = snapshot.TargetMask,
                    Position = targetPosition,
                    ShapeType = targetShapeType,
                    Radius = targetRadius,
                    HalfExtents = targetHalfExtents,
                    RotationRadians = targetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                });
            }
        }

        private int ActiveAoeCount()
        {
            if (!IsRuntimeReady())
            {
                return 0;
            }

            int count = entityManager.GetBuffer<AoeSpawnRequestElement>(scopeEntity).Length;
            using NativeArray<Entity> entities = allAoeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entity);
                if (identity.Scope == scopeEntity && entityManager.IsComponentEnabled<AoeActiveTag>(entity))
                {
                    count++;
                }
            }

            return count;
        }

        private void TryBuildRenderResource(int typeId)
        {
            if (!spawnVisuals || !typeRegistry.TryGetVisual(typeId, out AoeVisualDefinition visual))
            {
                return;
            }

            renderResourcesByType[typeId] = BuildRenderResourcesFor(
                visual.Sprite,
                visual.VisualScale,
                visual.VisualRotationDegrees,
                visual.Material);
        }

        private CombatSpriteRenderResources BuildRenderResourcesFor(
            Sprite sprite,
            Vector2 scale,
            float visualRotationDegrees,
            Material sourceMaterial = null)
        {
            return BatchedSpriteRenderer.BuildResources(
                sprite,
                scale,
                visualRotationDegrees,
                sourceMaterial,
                AoeRenderQueue,
                "AoeQuadMesh");
        }

        private void SubmitAoes()
        {
            activeVisuals = 0;
            renderBatches = 0;
            if (!spawnVisuals || renderResourcesByType.Count == 0 || submitQuery == null || !submitBuffer.IsCreated)
            {
                return;
            }

            entityManager.CompleteDependencyBeforeRO<CombatRenderElement>();
            DynamicBuffer<AoeRecycleElement> recycleBuffer = entityManager.GetBuffer<AoeRecycleElement>(scopeEntity);
            SubmitRecycledPulseAoes(recycleBuffer);
            SubmitActiveAoes();
        }

        private void SubmitRecycledPulseAoes(DynamicBuffer<AoeRecycleElement> recycleBuffer)
        {
            if (recycleBuffer.Length == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in renderResourcesByType)
            {
                int typeId = pair.Key;
                int batchCount = 0;
                for (int i = 0; i < recycleBuffer.Length; i++)
                {
                    AoeRecycleElement recycle = recycleBuffer[i];
                    if (recycle.TypeId != typeId)
                    {
                        continue;
                    }

                    submitBuffer[batchCount] = recycle.Render;
                    batchCount++;
                    activeVisuals++;

                    if (batchCount == MaxInstancesPerDraw)
                    {
                        SubmitBatch(submitBuffer, 0, batchCount, pair.Value);
                        batchCount = 0;
                    }
                }

                if (batchCount > 0)
                {
                    SubmitBatch(submitBuffer, 0, batchCount, pair.Value);
                }
            }
        }

        private void SubmitActiveAoes()
        {
            submitQuery.SetSharedComponentFilter(new CombatRenderScope { Scope = scopeEntity });
            using NativeArray<AoeIdentityComponent> identities =
                submitQuery.ToComponentDataArray<AoeIdentityComponent>(Allocator.Temp);
            using NativeArray<CombatRenderElement> renderElements =
                submitQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);

            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in renderResourcesByType)
            {
                int typeId = pair.Key;
                int batchCount = 0;
                for (int i = 0; i < identities.Length; i++)
                {
                    if (identities[i].Scope != scopeEntity || identities[i].TypeId != typeId)
                    {
                        continue;
                    }

                    submitBuffer[batchCount] = renderElements[i];
                    batchCount++;
                    activeVisuals++;

                    if (batchCount == MaxInstancesPerDraw)
                    {
                        SubmitBatch(submitBuffer, 0, batchCount, pair.Value);
                        batchCount = 0;
                    }
                }

                if (batchCount > 0)
                {
                    SubmitBatch(submitBuffer, 0, batchCount, pair.Value);
                }
            }

            submitQuery.ResetFilter();
        }

        private void SubmitBatch(NativeArray<CombatRenderElement> instances, int startInstance, int instanceCount, CombatSpriteRenderResources resources)
        {
            BatchedSpriteRenderer.SubmitBatch(instances, startInstance, instanceCount, resources, gameObject.layer, batchBoundsHalfExtent);
            renderBatches++;
        }

        private void DestroyRenderResources()
        {
            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in renderResourcesByType)
            {
                pair.Value.Destroy();
            }

            renderResourcesByType.Clear();
        }

    }
}
