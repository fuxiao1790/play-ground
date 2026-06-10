using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Vfx;
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
        private const int MaxVfxPerFrame = 2048;
        private const float AoeRenderZ = 0.5f;
        private static readonly ProfilerMarker SubmitAoesMarker = new("AoeRoot.SubmitAoes");

        [SerializeField] private int targetMask = 1;
        [SerializeField, Min(0)] private int maximumAoeCount = 10000;
        [SerializeField, Min(0)] private int maximumTargetCount = 100;
        [SerializeField] private bool spawnVisuals = true;
        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly CombatTargetRegistry<IAoeTarget> targetRegistry = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesByType = new();
        private readonly Dictionary<AoeConfig, int> configTypeIds = new();
        private readonly Dictionary<AoeTypeDefinition, int> definitionTypeIds = new();
        private AoeTypeRegistry typeRegistry = new();
        private CombatTargetSync<IAoeTarget> targetSync;
        private CombatVfxDispatcher vfxDispatcher;
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
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;

        public delegate void AoeHitHandler(in AoeHitContext context);

        public event AoeHitHandler AoeHit;

        public CombatTargetRegistry<IAoeTarget> TargetRegistry => targetRegistry;
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
            vfxDispatcher ??= new CombatVfxDispatcher(transform);
            targetSync = new CombatTargetSync<IAoeTarget>(targetRegistry);
            BindWorld();
            runtimeReady = true;
        }

        private void Update()
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            SyncTargetsToEcs();
        }

        private void LateUpdate()
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            using (SubmitAoesMarker.Auto())
            {
                SubmitAoes();
            }

            DrainEvents();
        }

        private void OnDestroy()
        {
            vfxDispatcher?.Dispose();
            vfxDispatcher = null;

            if (HasValidEcsState())
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
            DisposeEcsHandles();
            ReleaseWorld();
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
            vfxDispatcher?.Dispose();
            vfxDispatcher = new CombatVfxDispatcher(transform);
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
            RegisterVfxForDefinition(typeId, definition);
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
            RegisterVfxForDefinition(typeId, definition);
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
                CritChance = command.CritChance,
                CritMultiplier = command.CritMultiplier,
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
            DynamicBuffer<CombatHitElement> hitBuffer = entityManager.GetBuffer<CombatHitElement>(scopeEntity);
            DynamicBuffer<CombatHitPayloadElement> payloadBuffer = entityManager.GetBuffer<CombatHitPayloadElement>(scopeEntity);
            DynamicBuffer<AoeRecycleElement> recycleBuffer = entityManager.GetBuffer<AoeRecycleElement>(scopeEntity);
            int hitCount = hitBuffer.Length;
            hitEvents += hitCount;
            despawnedAoes += recycleBuffer.Length;

            var adapter = new AoeHitReplayAdapter { HitHandler = AoeHit };
            CombatHitReplay.ReplayAndClear<IAoeTarget, AoeHitReplayAdapter>(
                hitBuffer,
                payloadBuffer,
                targetSync.TargetsById,
                ref adapter);

            DynamicBuffer<VfxSpawnRequestElement> vfxBuffer =
                entityManager.GetBuffer<VfxSpawnRequestElement>(scopeEntity);
            for (int i = 0; i < vfxBuffer.Length; i++)
            {
                VfxSpawnRequestElement e = vfxBuffer[i];
                vfxDispatcher.StageSpawn(e.TypeId, e.Trigger, e.Position);
            }
            vfxBuffer.Clear();
            vfxDispatcher.Dispatch();
        }

        private struct AoeHitReplayAdapter : ICombatHitReplayAdapter<IAoeTarget>
        {
            public AoeHitHandler HitHandler;

            public DamageSnapshot RollDamage(in CombatHitElement hit)
            {
                float baseAmount = Mathf.Max(0f, hit.DamageAmount);
                bool isCrit = UnityEngine.Random.value < hit.CritChance;
                float rolledAmount = isCrit ? baseAmount * hit.CritMultiplier : baseAmount;
                return new DamageSnapshot(rolledAmount, isCrit);
            }

            public void Replay(in CombatHitElement hit, in CombatHitPayloadElement payload, IAoeTarget target, in DamageSnapshot damage)
            {
                var position = new Vector2(hit.Position.x, hit.Position.y);
                var context = new AoeHitContext(
                    hit.SourceId,
                    hit.TypeId,
                    hit.TargetId,
                    position,
                    damage,
                    payload.ProjectileBurst,
                    target);
                HitHandler?.Invoke(in context);
                target?.ReceiveHit(new CombatHitData(
                    CombatHitKind.Aoe,
                    damage,
                    position));
            }
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
            DisposeEcsHandles();
            ReleaseWorld();

            entityWorld = CombatEcsWorld.Acquire();
            ecsWorldAcquired = true;
            entityManager = entityWorld.EntityManager;
            scopeEntity = entityManager.CreateEntity(typeof(AoeScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<AoeSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitPayloadElement>(scopeEntity);
            entityManager.AddBuffer<AoeRecycleElement>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
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
            ecsHandlesCreated = true;
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
            targetSync.SyncToBuffer(targetBuffer, maxCount: maximumTargetCount);
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

        private void RegisterVfxForDefinition(int typeId, AoeTypeDefinition definition)
        {
            vfxDispatcher ??= new CombatVfxDispatcher(transform);
            vfxDispatcher.Register(typeId, 0, definition.SpawnEffect, MaxVfxPerFrame);
            vfxDispatcher.Register(typeId, 1, definition.HitEffect, MaxVfxPerFrame);
            vfxDispatcher.Register(typeId, 2, definition.ExpireEffect, MaxVfxPerFrame);
            vfxDispatcher.Register(typeId, 3, definition.PulseEffect, MaxVfxPerFrame);
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

        private void DisposeEcsHandles()
        {
            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            if (!ecsHandlesCreated)
            {
                scopeEntity = Entity.Null;
                entityManager = default;
                return;
            }

            DisposeQuery(ref submitQuery);
            DisposeQuery(ref allAoeQuery);
            ecsHandlesCreated = false;
            scopeEntity = Entity.Null;
            entityManager = default;
        }

        private void ReleaseWorld()
        {
            if (!ecsWorldAcquired)
            {
                entityWorld = null;
                return;
            }

            CombatEcsWorld.Release(entityWorld);
            entityWorld = null;
            ecsWorldAcquired = false;
        }

        private static void DisposeQuery(ref EntityQuery query)
        {
            try
            {
                query.Dispose();
            }
            catch (global::System.InvalidOperationException)
            {
            }
            catch (global::System.NullReferenceException)
            {
            }

            query = default;
        }

    }
}
