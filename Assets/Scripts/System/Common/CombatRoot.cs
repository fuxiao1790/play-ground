using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.Skills;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    // Unified combat root: one per faction. Owns one ECS world handle and ONE
    // shared scope serving both the projectile and AOE domains (shared target
    // list + damage buffer + per-domain spawn request buffers). Merges the former
    // ProjectileRoot + AoeRoot + CombatRuntimeRoot.
    //
    // Render resources are exposed through a static int-keyed registry (mirrors
    // CombatVfxRoot) so the shared scope can serve both domains' render data
    // without holding a managed per-scope catalog.
    public sealed class CombatRoot : MonoBehaviour
    {
        internal const float ProjectileRenderZ = -0.25f;
        internal const float ProjectileRenderZStep = 0.000001f;
        internal const int ProjectileRenderZSlots = 1_000_000;
        internal const float AoeRenderZ = 0.5f;

        private static readonly Dictionary<int, CombatRoot> Registry = new();
        private static int nextRootId;

        [Header("Targeting")]
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private string targetTag;

        [Header("Projectile visuals")]
        [SerializeField] private Sprite projectileSprite;
        [SerializeField] private float visualScale = 1f;
        [SerializeField] private BasicAttackPrefab[] projectileTemplates = global::System.Array.Empty<BasicAttackPrefab>();
        [SerializeField] private ProjectileRenderDefinition[] renderTypes = global::System.Array.Empty<ProjectileRenderDefinition>();

        [Header("AOE visuals")]
        [SerializeField] private bool spawnVisuals = true;

        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly CombatTargetRegistry<ICombatTarget> targetRegistry = new();
        private CombatTargetSync<ICombatTarget> targetSync;
        private global::System.Func<ICombatTarget, bool> canTargetFilter;
        private global::System.Action<DynamicBuffer<CombatTargetElement>> targetSyncCallback;

        // Projectile state.
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> projectileRenderResourcesByType = new();
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;

        // AOE state.
        private readonly Dictionary<int, CombatSpriteRenderResources> aoeRenderResourcesByType = new();
        private readonly Dictionary<AoeConfig, int> configTypeIds = new();
        private readonly Dictionary<AoeTypeDefinition, int> definitionTypeIds = new();
        private AoeTypeRegistry typeRegistry = new();
        private int spawnedAoes;
        private int nextAoeId;
        private int nextTypeId = 1;

        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allProjectileQuery;
        private EntityQuery allAoeQuery;
        private CombatDamageTargetSource damageTargetSource;
        private int rootId;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;

        public CombatTargetRegistry<ICombatTarget> TargetRegistry => targetRegistry;
        public int TargetMask => targetLayers.value != 0 ? targetLayers.value : ~0;
        public AoeRuntimeCounters Counters => new(ActiveAoeCount(), spawnedAoes, 0, 0, 0, 0);

        internal Entity ScopeEntity => scopeEntity;
        internal EntityManager EntityManager => entityManager;
        internal IReadOnlyDictionary<int, CombatSpriteRenderResources> ProjectileRenderResources => projectileRenderResourcesByType;
        internal IReadOnlyDictionary<int, CombatSpriteRenderResources> AoeRenderResources => aoeRenderResourcesByType;
        internal int RenderLayer => gameObject.layer;
        internal float BatchBoundsHalfExtent => batchBoundsHalfExtent;

        internal static bool TryGetRoot(int id, out CombatRoot root) => Registry.TryGetValue(id, out root);

        private void Awake()
        {
            runtimeReady = false;
            rootId = ++nextRootId;
            Registry[rootId] = this;
            targetSync = new CombatTargetSync<ICombatTarget>(targetRegistry);
            canTargetFilter = CanTarget;
            targetSyncCallback = buffer =>
            {
                targetSync.SyncToBuffer(buffer, additionalFilter: canTargetFilter);
                if (damageTargetSource != null)
                {
                    damageTargetSource.TargetsById = targetSync.TargetsById;
                }
            };
            ApplyTaggedDefaults();
            BindWorld();
            BuildProjectileRenderResources();
            runtimeReady = true;
        }

        private void OnDestroy()
        {
            Registry.Remove(rootId);
            if (HasValidEcsState())
            {
                DestroyScopedEntities(allProjectileQuery, GetProjectileScope);
                DestroyScopedEntities(allAoeQuery, GetAoeScope);
                if (scopeEntity != Entity.Null && entityManager.Exists(scopeEntity))
                {
                    entityManager.DestroyEntity(scopeEntity);
                }
            }

            runtimeReady = false;
            DisposeEcsHandles();
            ReleaseWorld();
            DestroyRenderResources();
        }

        // ---- Projectile API ----

        public void Configure(Sprite sprite) => projectileSprite = sprite;

        public void ConfigureTargetBinding(LayerMask layers, string tag = null)
        {
            targetLayers = layers;
            targetTag = tag;
        }

        public int RegisterTemplate(BasicAttackPrefab template)
        {
            if (template == null || template.Sprite == null)
            {
                return 0;
            }

            if (!templateTypeIds.TryGetValue(template, out int typeId))
            {
                typeId = nextTemplateTypeId++;
                templateTypeIds.Add(template, typeId);
            }

            if (!projectileRenderResourcesByType.ContainsKey(typeId))
            {
                projectileRenderResourcesByType[typeId] = BuildProjectileResources(
                    template.Sprite, template.VisualScale, template.VisualRotationDegrees, template.Material);
            }

            return typeId;
        }

        public bool CanTarget(ICombatTarget target)
        {
            if (target == null || (target.CombatTargetMask & TargetMask) == 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(targetTag) && target is Component component && !component.CompareTag(targetTag))
            {
                return false;
            }

            return true;
        }

        public int Spawn(ProjectileSpawnCommand command, int seedContactGateTargetId = 0)
        {
            EnsureRuntimeReady();
            ValidateSpawnCommand(command);

            int baseProjectileId = nextProjectileId + 1;
            nextProjectileId += command.Count;
            entityManager.GetBuffer<ProjectileSpawnRequestElement>(scopeEntity)
                .Add(ProjectileRequestFor(command, baseProjectileId, seedContactGateTargetId));
            return baseProjectileId;
        }

        // ---- AOE API ----

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
            typeRegistry.Register(typeId, config.CreateTypeDefinition());
            TryBuildAoeRenderResource(typeId);
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
            TryBuildAoeRenderResource(typeId);
            return typeId;
        }

        public int Spawn(ProjectileAoeSpawnRequest request)
        {
            return Spawn(new AoeSpawnCommand(
                request.EffectTypeId,
                request.Position,
                TargetMask,
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

            int aoeId = ++nextAoeId;
            entityManager.GetBuffer<AoeSpawnRequestElement>(scopeEntity).Add(AoeRequestFor(command, aoeId));
            spawnedAoes++;
            return aoeId;
        }

        // ---- Request builders ----

        private ProjectileSpawnRequestElement ProjectileRequestFor(ProjectileSpawnCommand command, int projectileId, int seedContactGateTargetId)
        {
            float2 position = new(command.Position.x, command.Position.y);
            float2 halfExtents = new(command.HalfExtents.x, command.HalfExtents.y);
            ProjectileCollisionMath.ComputeWorldBounds(
                position, command.Radius, halfExtents, command.RotationRadians, command.ShapeType,
                out float2 boundsMin, out float2 boundsMax);

            var request = new ProjectileSpawnRequestElement
            {
                Scope = scopeEntity,
                ProjectileId = projectileId,
                TypeId = command.ProjectileTypeId,
                PierceRemaining = command.PierceCount,
                HasChildSpawner = command.ChildSpawn.Enabled ? 1 : 0,
                SeedContactGateTargetId = seedContactGateTargetId,
                RepeatHitCooldownSeconds = command.RepeatHitCooldownSeconds,
                Lifetime = command.Lifetime,
                Radius = command.Radius,
                RotationRadians = command.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = command.ShapeType,
                HitPayload = command.HitPayload,
                Tracking = TrackingComponentFor(command.Tracking),
                Render = ProjectileRenderComponentFor(command.ProjectileTypeId, projectileId),
                Count = command.Count,
            };

            if (command.Count == 1)
            {
                request.Velocity = new float2(command.Direction.x, command.Direction.y) * command.Speed;
            }
            else
            {
                request.BaseDirection = new float2(command.Direction.x, command.Direction.y);
                request.Speed = command.Speed;
                request.SpreadDegrees = command.SpreadDegrees;
                request.JitterDegrees = command.JitterDegrees;
                request.JitterSeed = (uint)projectileId * 2654435761u;
            }

            if (command.ChildSpawn.Enabled)
            {
                request.ChildSpawner = ChildSpawnerComponentFor(command.ChildSpawn, command.HitPayload.SourceNodeId);
                request.ChildSpawnState = new ProjectileChildSpawnStateComponent
                {
                    ChildSpawnCooldownRemaining = command.ChildSpawn.IntervalSeconds
                        + DeterministicJitter(projectileId, command.ChildSpawn.IntervalJitterSeconds),
                    ChildSpawnTickIndex = 0
                };
            }

            return request;
        }

        private AoeSpawnRequestElement AoeRequestFor(AoeSpawnCommand command, int aoeId)
        {
            AoeSpawnGeometry geometry = command.Geometry;
            float2 position = new(command.Position.x, command.Position.y);
            float2 halfExtents = new(geometry.HalfExtents.x, geometry.HalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position, geometry.Radius, halfExtents, geometry.RotationRadians, geometry.ShapeType,
                out float2 boundsMin, out float2 boundsMax);

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
                Render = AoeRenderComponentFor(command.TypeId, geometry),
                ProjectileBurst = command.ProjectileBurst
            };
        }

        private ProjectileChildSpawnerComponent ChildSpawnerComponentFor(ProjectileChildSpawnConfig config, EntityId sourceNodeId)
        {
            math.sincos(math.radians(config.VisualRotationDegrees), out float sin, out float cos);
            return new ProjectileChildSpawnerComponent
            {
                SpawnerId = config.SpawnerId,
                TypeId = config.TypeId,
                ChildCountPerTick = Mathf.Max(1, config.Behavior.Count),
                SpawnPatternType = config.Behavior.PatternType,
                SideSpreadDegrees = config.Behavior.SpreadDegrees,
                IntervalSeconds = config.IntervalSeconds,
                IntervalJitterSeconds = config.IntervalJitterSeconds,
                Speed = config.Speed,
                Lifetime = config.Lifetime,
                Radius = config.Radius,
                HalfExtents = new float2(config.HalfExtents.x, config.HalfExtents.y),
                RotationRadians = config.RotationRadians,
                ShapeType = config.ShapeType,
                DamageAmount = config.Damage.Amount,
                DirectDamageEnabled = config.DirectDamageEnabled,
                PierceCount = config.PierceCount,
                RepeatHitCooldownSeconds = config.RepeatHitCooldownSeconds,
                VisualScale = config.VisualScale > 0f ? config.VisualScale : 1f,
                VisualRotationSin = sin,
                VisualRotationCos = cos,
                TrackingEnabled = config.Tracking.Enabled,
                TrackingTurnSpeedRadians = math.radians(config.Tracking.TurnSpeedDegrees),
                TrackingQueryIntervalSeconds = config.Tracking.QueryIntervalSeconds,
                TrackingInitialQueryDelaySeconds = config.Tracking.InitialQueryDelaySeconds,
                SourceNodeId = sourceNodeId,
                ImpactAoe = config.ImpactAoe,
                StackEffect = config.StackEffect,
                ImpactProjectile = config.ImpactProjectile
            };
        }

        private static ProjectileTrackingComponent TrackingComponentFor(ProjectileTrackingConfig config)
        {
            return new ProjectileTrackingComponent
            {
                TrackingEnabled = config.Enabled,
                TrackingTurnSpeedRadians = math.radians(config.TurnSpeedDegrees),
                TrackingQueryCooldownRemaining = config.InitialQueryDelaySeconds,
                TrackingQueryIntervalSeconds = config.QueryIntervalSeconds,
                TrackedTargetId = 0,
                TrackedTargetIndex = -1,
                TrackedTargetPosition = default,
                TrackingRandomState = 0
            };
        }

        // ---- Render resources ----

        private void BuildProjectileRenderResources()
        {
            DestroyProjectileRenderResources();
            templateTypeIds.Clear();
            nextTemplateTypeId = 1;
            if (projectileSprite != null)
            {
                projectileRenderResourcesByType[0] = BuildProjectileResources(projectileSprite, visualScale, 0f);
            }

            if (projectileTemplates != null)
            {
                for (int i = 0; i < projectileTemplates.Length; i++)
                {
                    BasicAttackPrefab template = projectileTemplates[i];
                    if (template != null && template.Sprite != null)
                    {
                        RegisterTemplate(template);
                    }
                }
            }

            if (renderTypes != null)
            {
                for (int i = 0; i < renderTypes.Length; i++)
                {
                    ProjectileRenderDefinition definition = renderTypes[i];
                    if (definition == null || definition.Sprite == null)
                    {
                        continue;
                    }

                    projectileRenderResourcesByType[definition.TypeId] =
                        BuildProjectileResources(definition.Sprite, definition.VisualScale, definition.VisualRotationDegrees);
                }
            }
        }

        private CombatSpriteRenderResources BuildProjectileResources(Sprite sprite, float scale, float visualRotationDegrees, Material sourceMaterial = null)
        {
            float positiveScale = scale > 0f ? scale : visualScale;
            return BatchedSpriteRenderer.BuildResources(
                sprite, new Vector2(positiveScale, positiveScale), visualRotationDegrees, sourceMaterial, "ProjectileQuadMesh");
        }

        private CombatRenderComponent ProjectileRenderComponentFor(int projectileTypeId, int projectileId)
        {
            if (!projectileRenderResourcesByType.TryGetValue(projectileTypeId, out CombatSpriteRenderResources resources))
            {
                return default;
            }

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 1,
                VisualScale = new float2(resources.VisualScale.x, resources.VisualScale.y),
                VisualRotationSin = resources.VisualRotationSin,
                VisualRotationCos = resources.VisualRotationCos,
                RenderZ = ProjectileRenderZ - (projectileId % ProjectileRenderZSlots) * ProjectileRenderZStep
            };
        }

        private void TryBuildAoeRenderResource(int typeId)
        {
            if (!spawnVisuals || !typeRegistry.TryGetVisual(typeId, out AoeVisualDefinition visual))
            {
                return;
            }

            aoeRenderResourcesByType[typeId] = BatchedSpriteRenderer.BuildResources(
                visual.Sprite, visual.VisualScale, visual.VisualRotationDegrees, visual.Material, "AoeQuadMesh");
        }

        private CombatRenderComponent AoeRenderComponentFor(int typeId, AoeSpawnGeometry geometry)
        {
            if (!spawnVisuals || !aoeRenderResourcesByType.ContainsKey(typeId))
            {
                return default;
            }

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new float2(geometry.VisualScale.x, geometry.VisualScale.y),
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos,
                RenderZ = AoeRenderZ
            };
        }

        private void DestroyProjectileRenderResources()
        {
            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in projectileRenderResourcesByType)
            {
                pair.Value.Destroy();
            }

            projectileRenderResourcesByType.Clear();
        }

        private void DestroyAoeRenderResources()
        {
            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in aoeRenderResourcesByType)
            {
                pair.Value.Destroy();
            }

            aoeRenderResourcesByType.Clear();
        }

        private void DestroyRenderResources()
        {
            DestroyProjectileRenderResources();
            DestroyAoeRenderResources();
        }

        // ---- World / scope ----

        private void BindWorld()
        {
            DisposeEcsHandles();
            ReleaseWorld();

            entityWorld = CombatEcsWorld.Acquire();
            ecsWorldAcquired = true;
            entityManager = entityWorld.EntityManager;
            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<CombatDamageElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<AoeSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
            entityManager.AddComponentObject(scopeEntity, new CombatTargetSyncSource { Sync = targetSyncCallback });
            damageTargetSource = new CombatDamageTargetSource { TargetsById = targetSync.TargetsById };
            entityManager.AddComponentObject(scopeEntity, damageTargetSource);
            entityManager.AddComponentData(scopeEntity, new CombatScopeRenderCatalog { RootId = rootId });
            allProjectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>());
            allAoeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>());
            ecsHandlesCreated = true;
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

        private Entity GetProjectileScope(Entity entity) =>
            entityManager.GetComponentData<ProjectileIdentityComponent>(entity).Scope;

        private Entity GetAoeScope(Entity entity) =>
            entityManager.GetComponentData<AoeIdentityComponent>(entity).Scope;

        private void DestroyScopedEntities(EntityQuery query, global::System.Func<Entity, Entity> scopeOf)
        {
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (scopeOf(entities[i]) == scopeEntity)
                {
                    entityManager.DestroyEntity(entities[i]);
                }
            }
        }

        private bool IsRuntimeReady() => runtimeReady && HasValidEcsState();

        internal bool EnsureRuntimeAvailable()
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
                throw new global::System.InvalidOperationException($"{nameof(CombatRoot)} on {name} has not finished ECS setup.");
            }
        }

        private void ValidateSpawnCommand(ProjectileSpawnCommand command)
        {
            ValidateRenderableType(command.ProjectileTypeId, nameof(command.ProjectileTypeId));
            if (command.ChildSpawn.Enabled)
            {
                ValidateRenderableType(command.ChildSpawn.TypeId, nameof(command.ChildSpawn));
            }
        }

        private void ValidateRenderableType(int projectileTypeId, string source)
        {
            if (!projectileRenderResourcesByType.ContainsKey(projectileTypeId))
            {
                throw new global::System.InvalidOperationException(
                    $"Projectile render type {projectileTypeId} from {source} has no registered render resources.");
            }
        }

        private void DisposeEcsHandles()
        {
            if (!ecsHandlesCreated)
            {
                scopeEntity = Entity.Null;
                entityManager = default;
                return;
            }

            DisposeQuery(ref allProjectileQuery);
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

        // ---- Faction defaults ----

        private void ApplyTaggedDefaults()
        {
            if (HasTag(GameplayTags.PlayerProjectileRoot))
            {
                ApplyTargetDefaults(GameplayLayers.MobHurtbox, GameplayTags.Mob);
                ApplyObjectLayer(GameplayLayers.PlayerProjectile);
                return;
            }

            if (HasTag(GameplayTags.MobProjectileRoot))
            {
                ApplyTargetDefaults(GameplayLayers.PlayerHurtbox, GameplayTags.Player);
                ApplyObjectLayer(GameplayLayers.MobProjectile);
            }
        }

        private void ApplyTargetDefaults(string targetLayerName, string defaultTargetTag)
        {
            if (targetLayers.value == 0)
            {
                int layer = LayerMask.NameToLayer(targetLayerName);
                if (layer >= 0)
                {
                    targetLayers = 1 << layer;
                }
            }

            if (string.IsNullOrEmpty(targetTag))
            {
                targetTag = defaultTargetTag;
            }
        }

        private void ApplyObjectLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                gameObject.layer = layer;
            }
        }

        private bool HasTag(string tag)
        {
            try
            {
                return CompareTag(tag);
            }
            catch (UnityException)
            {
                return false;
            }
        }

        private static float DeterministicJitter(int projectileId, float maxOffsetSeconds)
        {
            if (maxOffsetSeconds <= 0f)
            {
                return 0f;
            }

            uint hash = (uint)projectileId * 0x9E3779B9u;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return ((hash & 0x00FFFFFFu) + 1u) / 16777217f * maxOffsetSeconds;
        }

        [global::System.Serializable]
        private sealed class ProjectileRenderDefinition
        {
            [SerializeField] private int typeId;
            [SerializeField] private Sprite sprite;
            [SerializeField] private float visualScale = 1f;
            [SerializeField] private float visualRotationDegrees;

            public int TypeId => typeId;
            public Sprite Sprite => sprite;
            public float VisualScale => visualScale;
            public float VisualRotationDegrees => visualRotationDegrees;
        }
    }
}
