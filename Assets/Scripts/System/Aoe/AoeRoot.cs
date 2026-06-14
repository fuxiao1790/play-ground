using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class AoeRoot : MonoBehaviour, ICombatScopeEndpoint
    {
        private const float AoeRenderZ = 0.5f;
        [SerializeField] private int targetMask = 1;
        [SerializeField] private bool spawnVisuals = true;
        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly CombatTargetRegistry<ICombatTarget> targetRegistry = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesByType = new();
        private readonly Dictionary<AoeConfig, int> configTypeIds = new();
        private readonly Dictionary<AoeTypeDefinition, int> definitionTypeIds = new();
        private AoeTypeRegistry typeRegistry = new();
        private CombatTargetSync<ICombatTarget> targetSync;
        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allAoeQuery;
        private IReadOnlyDictionary<int, ICombatTarget> runtimeTargetsById;
        private CombatDamageTargetSource damageTargetSource;
        private int spawnedAoes;
        private int hitEvents;
        private int nextAoeId;
        private int nextTypeId = 1;
        private global::System.Action<DynamicBuffer<CombatTargetElement>> targetSyncCallback;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;
        private bool combatRuntimeManaged;

        // HitSpawn is internal combat routing data for spawn-on-hit effects and
        // must not be treated as external gameplay API. Damage is applied by
        // CombatHitDispatchSystem, not by AOE roots.
        public event CombatSpawnHandler HitSpawn;

        public CombatTargetRegistry<ICombatTarget> TargetRegistry => targetRegistry;
        public int TargetMask => targetMask;
        public AoeRuntimeCounters Counters => new(ActiveAoeCount(), spawnedAoes, 0, hitEvents, 0, 0);

        private void Awake()
        {
            runtimeReady = false;
            targetSync = new CombatTargetSync<ICombatTarget>(targetRegistry);
            targetSyncCallback = buffer =>
            {
                targetSync.SyncToBuffer(buffer);
                if (damageTargetSource != null)
                {
                    damageTargetSource.TargetsById = targetSync.TargetsById;
                }
            };
            BindWorld();
            runtimeReady = true;
        }

        private void OnDestroy()
        {
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
                Lifetime = command.LifetimeSeconds,
                RepeatHitCooldownSeconds = command.TickIntervalSeconds,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = command.Damage.Amount,
                    CritChance = command.CritChance,
                    CritMultiplier = command.CritMultiplier,
                    DirectDamageEnabled = true,
                    SourceNodeId = command.SourceNodeId,
                    StackEffect = command.StackEffect
                },
                AreaSize = geometry.AreaSize,
                Radius = geometry.Radius,
                RotationRadians = geometry.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = geometry.ShapeType,
                Render = RenderComponentFor(command.TypeId, geometry),
                ProjectileBurst = command.ProjectileBurst
            };
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
            entityManager.AddBuffer<CombatDamageElement>(scopeEntity);
            entityManager.AddBuffer<CombatSpawnElement>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
            entityManager.AddComponentObject(scopeEntity, new CombatTargetSyncSource { Sync = combatRuntimeManaged ? null : targetSyncCallback });
            damageTargetSource = new CombatDamageTargetSource
            {
                TargetsById = combatRuntimeManaged ? runtimeTargetsById : targetSync.TargetsById
            };
            entityManager.AddComponentObject(scopeEntity, damageTargetSource);
            allAoeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>());
            entityManager.AddComponentObject(scopeEntity, new CombatScopeRenderCatalog
            {
                Resources = renderResourcesByType,
                Layer = gameObject.layer,
                BoundsHalfExtent = batchBoundsHalfExtent
            });
            entityWorld.GetExistingSystemManaged<CombatHitDispatchSystem>()?.Register(
                scopeEntity, (in CombatHitContext context, in CombatSpawnElement spawn) =>
                {
                    hitEvents++;
                    HitSpawn?.Invoke(in context, in spawn);
                });
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
            if (!ecsHandlesCreated)
            {
                scopeEntity = Entity.Null;
                entityManager = default;
                return;
            }

            if (entityWorld != null && entityWorld.IsCreated)
            entityWorld.GetExistingSystemManaged<CombatHitDispatchSystem>()?.Unregister(scopeEntity);
            DisposeQuery(ref allAoeQuery);
            ecsHandlesCreated = false;
            damageTargetSource = null;
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
                if (damageTargetSource != null)
                {
                    damageTargetSource.TargetsById = targetSync.TargetsById;
                }
            }
            if (HasValidEcsState())
            {
                entityManager.GetComponentObject<CombatTargetSyncSource>(scopeEntity).Sync = managed ? null : targetSyncCallback;
                entityManager.GetComponentObject<CombatDamageTargetSource>(scopeEntity).TargetsById =
                    managed ? runtimeTargetsById : targetSync.TargetsById;
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
            if (damageTargetSource != null)
            {
                damageTargetSource.TargetsById = targetsById;
            }
        }

    }
}
