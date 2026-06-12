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
    public sealed class AoeRoot : MonoBehaviour, ICombatScopeEndpoint
    {
        private const int MaxInstancesPerDraw = 1023;
        private const int MaxVfxPerFrame = 2048;
        private const float AoeRenderZ = 0.5f;
        private static readonly ProfilerMarker SubmitAoesMarker = new("AoeRoot.SubmitAoes");
        private static readonly ProfilerMarker DrainVfxMarker = new("AoeRoot.DrainVfxRequests");
        private static readonly ProfilerMarker DrainHitsMarker = new("AoeRoot.DrainHits");

        [SerializeField] private int targetMask = 1;
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
        private IReadOnlyDictionary<int, ICombatTarget> runtimeTargetsById;
        private int spawnedAoes;
        private int despawnedAoes;
        private int hitEvents;
        private int activeVisuals;
        private int renderBatches;
        private int nextAoeId;
        private int nextTypeId = 1;
        private global::System.Action<DynamicBuffer<CombatTargetElement>> targetSyncCallback;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;
        private bool combatRuntimeManaged;

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
            targetSyncCallback = buffer => targetSync.SyncToBuffer(buffer);
            BindWorld();
            runtimeReady = true;
        }

        private void LateUpdate()
        {
            if (combatRuntimeManaged)
            {
                return;
            }

            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            using (SubmitAoesMarker.Auto())
            {
                SubmitAoes();
            }

            using (DrainVfxMarker.Auto())
            {
                DrainVfxRequests();
            }

            using (DrainHitsMarker.Auto())
            {
                DrainHits();
            }
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
                request.TickIntervalSeconds,
                request.Geometry));
        }

        public int Spawn(AoeSpawnCommand command)
        {
            EnsureRuntimeReady();
            if (!typeRegistry.TryGetDefinition(command.TypeId, out _))
            {
                throw new global::System.InvalidOperationException($"Missing AOE definition for type id {command.TypeId}.");
            }
            if (!command.Geometry.IsValid)
            {
                throw new global::System.InvalidOperationException($"AOE spawn command for type id {command.TypeId} has unresolved geometry.");
            }

            DynamicBuffer<AoeSpawnRequestElement> spawnRequests =
                entityManager.GetBuffer<AoeSpawnRequestElement>(scopeEntity);
            int aoeId = ++nextAoeId;
            spawnRequests.Add(SpawnRequestFor(command, aoeId));
            spawnedAoes++;
            return aoeId;
        }

        private AoeSpawnRequestElement SpawnRequestFor(AoeSpawnCommand command, int aoeId)
        {
            AoeSpawnGeometry geometry = command.Geometry;
            float2 position = new(command.Position.x, command.Position.y);
            float2 halfExtents = new(geometry.HalfExtents.x, geometry.HalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position,
                geometry.Radius,
                halfExtents,
                geometry.RotationRadians,
                geometry.ShapeType,
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
                AreaSize = geometry.AreaSize,
                Radius = geometry.Radius,
                RotationRadians = geometry.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = geometry.ShapeType,
                Render = RenderComponentFor(command.TypeId, geometry),
                ProjectileBurst = command.ProjectileBurst,
                StackEffect = command.StackEffect
            };
        }

        private void DrainVfxRequests()
        {
            DynamicBuffer<VfxSpawnRequestElement> vfxBuffer =
                entityManager.GetBuffer<VfxSpawnRequestElement>(scopeEntity);
            for (int i = 0; i < vfxBuffer.Length; i++)
            {
                VfxSpawnRequestElement e = vfxBuffer[i];
                vfxDispatcher.StageSpawn(e.TypeId, e.Trigger, e.Position, e.AreaSize);
            }
            vfxBuffer.Clear();
            vfxDispatcher.Dispatch();
        }

        private void DrainHits()
        {
            DynamicBuffer<CombatHitElement> hitBuffer = entityManager.GetBuffer<CombatHitElement>(scopeEntity);
            DynamicBuffer<CombatHitPayloadElement> payloadBuffer = entityManager.GetBuffer<CombatHitPayloadElement>(scopeEntity);
            DynamicBuffer<AoeRecycleElement> recycleBuffer = entityManager.GetBuffer<AoeRecycleElement>(scopeEntity);
            hitEvents += hitBuffer.Length;
            despawnedAoes += recycleBuffer.Length;

            if (combatRuntimeManaged && runtimeTargetsById != null)
            {
                var runtimeAdapter = new AoeHitReplayAdapter<ICombatTarget> { HitHandler = AoeHit };
                CombatHitReplay.ReplayAndClear<ICombatTarget, AoeHitReplayAdapter<ICombatTarget>>(
                    hitBuffer,
                    payloadBuffer,
                    runtimeTargetsById,
                    ref runtimeAdapter);
                return;
            }

            var adapter = new AoeHitReplayAdapter<IAoeTarget> { HitHandler = AoeHit };
            CombatHitReplay.ReplayAndClear<IAoeTarget, AoeHitReplayAdapter<IAoeTarget>>(
                hitBuffer,
                payloadBuffer,
                targetSync.TargetsById,
                ref adapter);
        }

        private struct AoeHitReplayAdapter<TTarget> : ICombatHitReplayAdapter<TTarget>
            where TTarget : class, ICombatTarget
        {
            public AoeHitHandler HitHandler;

            public DamageSnapshot RollDamage(in CombatHitElement hit)
            {
                float baseAmount = Mathf.Max(0f, hit.DamageAmount);
                bool isCrit = UnityEngine.Random.value < hit.CritChance;
                float rolledAmount = isCrit ? baseAmount * hit.CritMultiplier : baseAmount;
                return new DamageSnapshot(rolledAmount, isCrit);
            }

            public void Replay(in CombatHitElement hit, in CombatHitPayloadElement payload, TTarget target, in DamageSnapshot damage)
            {
                var position = new Vector2(hit.Position.x, hit.Position.y);
                IAoeTarget aoeTarget = target as IAoeTarget;
                var context = new AoeHitContext(
                    hit.SourceId,
                    hit.TypeId,
                    hit.TargetId,
                    position,
                    damage,
                    payload.ProjectileBurst,
                    aoeTarget);
                HitHandler?.Invoke(in context);
                if (CombatHitReplay.IsTargetUsable(target))
                {
                    target.ReceiveHit(new CombatHitData(
                        CombatHitKind.Aoe,
                        damage,
                        position,
                        hit.DirectDamageEnabled,
                        payload.StackEffect));
                }
            }
        }

        private CombatRenderComponent RenderComponentFor(int typeId, AoeSpawnGeometry geometry)
        {
            if (!spawnVisuals || !renderResourcesByType.TryGetValue(typeId, out CombatSpriteRenderResources resources))
            {
                return default;
            }

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new Unity.Mathematics.float2(
                    geometry.VisualScale.x,
                    geometry.VisualScale.y),
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos,
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
            entityManager.AddComponentObject(scopeEntity, new CombatTargetSyncSource { Sync = combatRuntimeManaged ? null : targetSyncCallback });
            allAoeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>());
            submitQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<CombatRenderTypeId>(),
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
            vfxDispatcher.Register(typeId, 0, definition.SpawnEffect, MaxVfxPerFrame, requireAreaSizeContract: true);
            vfxDispatcher.Register(typeId, 1, definition.HitEffect, MaxVfxPerFrame, requireAreaSizeContract: true);
            vfxDispatcher.Register(typeId, 2, definition.ExpireEffect, MaxVfxPerFrame, requireAreaSizeContract: true);
            vfxDispatcher.Register(typeId, 3, definition.PulseEffect, MaxVfxPerFrame, requireAreaSizeContract: true);
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
            entityManager.CompleteDependencyBeforeRO<AoeActiveTag>();
            entityManager.CompleteDependencyBeforeRO<CombatRenderElement>();
            activeVisuals = 0;
            renderBatches = 0;
            if (!spawnVisuals || renderResourcesByType.Count == 0 || submitQuery == null || !submitBuffer.IsCreated)
            {
                return;
            }
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
            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in renderResourcesByType)
            {
                submitQuery.SetSharedComponentFilter(
                    new CombatRenderScope { Scope = scopeEntity },
                    new CombatRenderTypeId { TypeId = pair.Key });
                using NativeArray<CombatRenderElement> renderElements =
                    submitQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
                activeVisuals += renderElements.Length;

                for (int start = 0; start < renderElements.Length; start += MaxInstancesPerDraw)
                {
                    int count = Mathf.Min(MaxInstancesPerDraw, renderElements.Length - start);
                    NativeArray<CombatRenderElement>.Copy(renderElements, start, submitBuffer, 0, count);
                    SubmitBatch(submitBuffer, 0, count, pair.Value);
                }

                submitQuery.ResetFilter();
            }
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

        Entity ICombatScopeEndpoint.ScopeEntity => scopeEntity;
        EntityManager ICombatScopeEndpoint.EntityManager => entityManager;
        bool ICombatScopeEndpoint.EnsureRuntimeAvailable() => EnsureRuntimeAvailable();

        void ICombatScopeEndpoint.SetCombatRuntimeManaged(bool managed)
        {
            combatRuntimeManaged = managed;
            if (!managed)
            {
                runtimeTargetsById = null;
            }
            if (HasValidEcsState())
            {
                entityManager.GetComponentObject<CombatTargetSyncSource>(scopeEntity).Sync = managed ? null : targetSyncCallback;
            }
        }

        void ICombatScopeEndpoint.WriteTargets(
            IReadOnlyList<CombatTargetElement> targets,
            IReadOnlyDictionary<int, ICombatTarget> targetsById)
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            DynamicBuffer<CombatTargetElement> targetBuffer = entityManager.GetBuffer<CombatTargetElement>(scopeEntity);
            targetBuffer.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                targetBuffer.Add(targets[i]);
            }

            runtimeTargetsById = targetsById;
        }

        void ICombatScopeEndpoint.PresentFromCombatRuntime()
        {
            using (SubmitAoesMarker.Auto())
            {
                SubmitAoes();
            }

            using (DrainVfxMarker.Auto())
            {
                DrainVfxRequests();
            }

            using (DrainHitsMarker.Auto())
            {
                DrainHits();
            }
        }

    }
}
