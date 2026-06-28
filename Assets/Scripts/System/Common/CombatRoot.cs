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
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.System.Common
{
    // Unified combat root: one instance serves all factions. Owns one ECS world handle
    // and ONE shared scope serving both the projectile and AOE domains (shared target
    // list + damage buffer + per-domain spawn request buffers). Faction is a per-spawn
    // argument, not a property of the root.
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
        internal const int MaxSpawnChainDepth = SpawnTemplateLimits.MaxSpawnChainDepth;

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

        // Render-resource state. Render identity is decoupled from the per-domain
        // behavior type ids: projectile and AOE type id spaces overlap, so render
        // resources are minted into one shared id space (renderResourcesById) and the
        // per-domain maps translate a behavior type id to its render id.
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesById = new();
        private readonly Dictionary<int, int> projectileRenderIdByType = new();
        private readonly Dictionary<int, int> aoeRenderIdByType = new();
        private int nextRenderId = 1;

        // Projectile state.
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;

        // AOE state.
        private readonly Dictionary<AoeConfig, int> configTypeIds = new();
        private readonly Dictionary<AoeTypeDefinition, int> definitionTypeIds = new();
        private AoeTypeRegistry typeRegistry = new();
        private int spawnedAoes;
        private int nextAoeId;
        private int nextTypeId = 1;

        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private CombatRenderResourceRegistry _renderRegistry;
        private EntityQuery allProjectileQuery;
        private EntityQuery allAoeQuery;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;

        public CombatTargetRegistry<ICombatTarget> TargetRegistry => targetRegistry;
        public AoeRuntimeCounters Counters => new(ActiveAoeCount(), spawnedAoes, 0, 0, 0, 0);

        internal Entity ScopeEntity => scopeEntity;
        internal EntityManager EntityManager => entityManager;
        internal IReadOnlyDictionary<int, ICombatTarget> TargetsById => targetRegistry.TargetsById;

        private void Awake()
        {
            runtimeReady = false;
            BindWorld();
            using (EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatRenderResourceRegistry>()))
            {
                _renderRegistry = q.IsEmpty
                    ? null
                    : entityManager.GetComponentObject<CombatRenderResourceRegistry>(q.GetSingletonEntity());
            }
            if (_renderRegistry == null)
                Debug.LogWarning("[CombatRoot] CombatRenderResourceRegistry singleton not found; render registry will not be populated.");
            BuildProjectileRenderResources();
            runtimeReady = true;
        }

        private void OnDestroy()
        {
            if (HasValidEcsState())
            {
                DestroyScopedEntities(allProjectileQuery);
                DestroyScopedEntities(allAoeQuery);
                CombatScopeOwner.Release(entityManager, scopeEntity);
            }
            else if (scopeEntity != Entity.Null)
            {
                CombatScopeOwner.ReleaseAfterWorldDispose(scopeEntity);
            }

            runtimeReady = false;
            targetRegistry.ClearProxyBinding();
            DisposeEcsHandles();
            ReleaseWorld();
            if (_renderRegistry != null)
            {
                foreach (int renderId in renderResourcesById.Keys)
                    _renderRegistry.Entries.Remove(renderId);
                _renderRegistry = null;
            }
            DestroyRenderResources();
        }

        // ---- Projectile API ----

        public void Configure(Sprite sprite) => projectileSprite = sprite;

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

            if (!projectileRenderIdByType.ContainsKey(typeId))
            {
                projectileRenderIdByType[typeId] = RegisterRenderResource(
                    template.Sprite,
                    ProjectileVisualScale(template.VisualScale),
                    template.VisualRotationDegrees,
                    template.Material,
                    ProjectileMeshName);
            }

            return typeId;
        }

        public int Spawn(ProjectileSpawnRequest request, CombatFaction faction, int seedContactGateTargetId = 0)
        {
            EnsureRuntimeReady();
            ValidateSpawnRequest(request);

            int baseProjectileId = nextProjectileId + 1;
            nextProjectileId += request.Count;
            ProjectileSpawnCommand template = ProjectileCommandFor(request, baseProjectileId, seedContactGateTargetId);
            Hash128 templateKey = RegisterSpawnTemplate(in template);
            entityManager.GetBuffer<ProjectileSpawnEvent>(scopeEntity)
                .Add(ProjectileEventFor(templateKey, request, baseProjectileId, seedContactGateTargetId, faction));
            return baseProjectileId;
        }

        public Hash128 RegisterSpawnTemplate(in ProjectileSpawnCommand template)
        {
            EnsureRuntimeReady();

            ProjectileSpawnCommand normalizedTemplate = SpawnTemplateFor(in template);
            Hash128 key = SpawnTemplateHash.Of(in normalizedTemplate);
            ProjectileSpawnTemplate registry = entityManager.GetComponentData<ProjectileSpawnTemplate>(scopeEntity);
            if (!registry.Map.ContainsKey(key))
            {
                entityManager.CompleteAllTrackedJobs();
                registry.Map.Add(key, normalizedTemplate);
            }

            return key;
        }

        public Hash128 RegisterTimedSpawnTemplate(in ProjectileSpawnCommand template) =>
            RegisterSpawnTemplate(in template);

        internal int SpawnRegisteredProjectile(
            Hash128 templateKey,
            Vector2 position,
            Vector2 direction,
            int count,
            CombatFaction faction,
            int seedContactGateTargetId = 0)
        {
            EnsureRuntimeReady();
            if (templateKey.Equals(default(Hash128)))
            {
                return 0;
            }

            int projectileCount = math.max(1, count);
            int baseProjectileId = nextProjectileId + 1;
            nextProjectileId += projectileCount;
            float2 aim = direction.sqrMagnitude > 0f
                ? new float2(direction.normalized.x, direction.normalized.y)
                : new float2(1f, 0f);

            entityManager.GetBuffer<ProjectileSpawnEvent>(scopeEntity)
                .Add(new ProjectileSpawnEvent
                {
                    Kind = IntervalChildKind.Projectile,
                    TemplateKey = templateKey,
                    Position = new float2(position.x, position.y),
                    AimDirection = aim,
                    Faction = faction,
                    SourceId = baseProjectileId,
                    JitterSeed = (uint)baseProjectileId * 2654435761u,
                    ContactGateSeedTargetId = seedContactGateTargetId
                });

            return baseProjectileId;
        }

        // ---- AOE API ----

        public Hash128 RegisterSpawnTemplate(in AoeSpawnCommand template)
        {
            EnsureRuntimeReady();

            AoeSpawnCommand normalizedTemplate = SpawnTemplateFor(in template);
            Hash128 key = SpawnTemplateHash.Of(in normalizedTemplate);
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(scopeEntity);
            if (!registry.Map.ContainsKey(key))
            {
                entityManager.CompleteAllTrackedJobs();
                registry.Map.Add(key, normalizedTemplate);
            }

            return key;
        }

        public Hash128 RegisterTimedSpawnTemplate(in AoeSpawnCommand template) =>
            RegisterSpawnTemplate(in template);

        internal int SpawnRegisteredAoe(Hash128 templateKey, Vector2 position, int count, CombatFaction faction)
        {
            EnsureRuntimeReady();
            if (templateKey.Equals(default(Hash128)))
            {
                return 0;
            }

            int aoeCount = math.max(1, count);
            int aoeId = ++nextAoeId;
            nextAoeId += aoeCount - 1;
            entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity)
                .Add(new AoeSpawnEvent
                {
                    Kind = IntervalChildKind.Aoe,
                    TemplateKey = templateKey,
                    Position = new float2(position.x, position.y),
                    Faction = faction,
                    SourceId = aoeId,
                    JitterSeed = (uint)aoeId * 2654435761u
                });
            spawnedAoes += aoeCount;
            return aoeId;
        }

        internal CombatRenderComponent ProjectileTemplateRenderComponent(int renderId) =>
            ProjectileRenderComponentForRenderId(renderId, 0);

        internal CombatRenderComponent AoeTemplateRenderComponent(int renderId, AoeSpawnGeometry geometry) =>
            AoeRenderComponentForRenderId(renderId, geometry);

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

        public int Spawn(ProjectileAoeSpawnRequest request, CombatFaction faction)
        {
            return Spawn(new AoeSpawnRequest(
                request.EffectTypeId,
                request.Position,
                request.Damage,
                request.LifetimeSeconds,
                request.TickIntervalSeconds,
                request.Geometry), faction);
        }

        public int Spawn(AoeSpawnRequest request, CombatFaction faction)
        {
            EnsureRuntimeReady();
            if (!typeRegistry.TryGetDefinition(request.TypeId, out _))
            {
                throw new global::System.InvalidOperationException($"Missing AOE definition for type id {request.TypeId}.");
            }
            if (!request.Geometry.IsValid)
            {
                throw new global::System.InvalidOperationException($"AOE spawn request for type id {request.TypeId} has unresolved geometry.");
            }

            int aoeId = ++nextAoeId;
            AoeSpawnCommand template = AoeCommandFor(request, aoeId);
            Hash128 templateKey = RegisterSpawnTemplate(in template);
            entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity).Add(AoeEventFor(templateKey, request, aoeId, faction));
            spawnedAoes++;
            return aoeId;
        }

        // ---- Request builders ----

        private ProjectileSpawnEvent ProjectileEventFor(
            Hash128 templateKey,
            ProjectileSpawnRequest request,
            int baseProjectileId,
            int seedContactGateTargetId,
            CombatFaction faction)
        {
            return new ProjectileSpawnEvent
            {
                Kind = IntervalChildKind.Projectile,
                TemplateKey = templateKey,
                Position = new float2(request.Position.x, request.Position.y),
                AimDirection = new float2(request.Direction.x, request.Direction.y),
                Faction = faction,
                SourceId = baseProjectileId,
                JitterSeed = (uint)baseProjectileId * 2654435761u,
                ContactGateSeedTargetId = seedContactGateTargetId
            };
        }

        private ProjectileSpawnCommand ProjectileCommandFor(ProjectileSpawnRequest request, int baseProjectileId, int seedContactGateTargetId)
        {
            float2 position = new(request.Position.x, request.Position.y);
            float2 halfExtents = new(request.HalfExtents.x, request.HalfExtents.y);
            TimedSpawnComponent timedSpawn = TimedSpawnFor(request, baseProjectileId);
            bool hasTimedSpawner = IsTimedSpawnEnabled(timedSpawn);
            int renderId = ProjectileRenderId(request.ProjectileTypeId);

            return new ProjectileSpawnCommand
            {
                Faction = CombatFaction.None,
                ProjectileId = baseProjectileId,
                TypeId = request.ProjectileTypeId,
                RenderTypeId = renderId,
                PierceRemaining = request.PierceCount,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                SeedContactGateTargetId = seedContactGateTargetId,
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds,
                Lifetime = request.Lifetime,
                Radius = request.Radius,
                RotationRadians = request.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                ShapeType = request.ShapeType,
                HitPayload = request.HitPayload,
                Tracking = TrackingComponentFor(request.Tracking),
                Render = ProjectileRenderComponentForRenderId(renderId, baseProjectileId),
                Count = request.Count,
                BaseDirection = new float2(request.Direction.x, request.Direction.y),
                Speed = request.Speed,
                SpreadDegrees = request.SpreadDegrees,
                JitterDegrees = request.JitterDegrees,
                JitterSeed = (uint)baseProjectileId * 2654435761u,
                TimedSpawn = timedSpawn
            };
        }

        private AoeSpawnEvent AoeEventFor(Hash128 templateKey, AoeSpawnRequest request, int aoeId, CombatFaction faction)
        {
            return new AoeSpawnEvent
            {
                Kind = IntervalChildKind.Aoe,
                TemplateKey = templateKey,
                Position = new float2(request.Position.x, request.Position.y),
                Faction = faction,
                SourceId = aoeId,
                JitterSeed = (uint)aoeId * 2654435761u
            };
        }

        private AoeSpawnCommand AoeCommandFor(AoeSpawnRequest request, int aoeId)
        {
            AoeSpawnGeometry geometry = request.Geometry;
            float2 position = new(request.Position.x, request.Position.y);
            float2 halfExtents = new(geometry.HalfExtents.x, geometry.HalfExtents.y);
            int renderId = AoeRenderId(request.TypeId);

            return new AoeSpawnCommand
            {
                Faction = CombatFaction.None,
                AoeId = aoeId,
                TypeId = request.TypeId,
                RenderTypeId = renderId,
                Lifetime = request.LifetimeSeconds,
                RepeatHitCooldownSeconds = request.TickIntervalSeconds,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = request.Damage.Amount,
                    CritChance = request.CritChance,
                    CritMultiplier = request.CritMultiplier,
                    DirectDamageEnabled = true,
                    SourceNodeId = request.SourceNodeId,
                    StackEffect = request.StackEffect
                },
                AreaSize = geometry.AreaSize,
                Radius = geometry.Radius,
                RotationRadians = geometry.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                ShapeType = geometry.ShapeType,
                Render = AoeRenderComponentForRenderId(renderId, geometry),
                OnHitSpawn = request.OnHitSpawn,
                HasTimedSpawner = request.HasTimedSpawner ? 1 : 0,
                TimedSpawn = StampTimedSpawn(request.TimedSpawn, CombatFaction.None, aoeId)
            };
        }

        private static TimedSpawnComponent TimedSpawnFor(
            ProjectileSpawnRequest request,
            int sourceId)
        {
            TimedSpawnComponent timedSpawn = request.TimedSpawn;
            if (!IsTimedSpawnEnabled(timedSpawn)
                && request.ChildKind == IntervalChildKind.Projectile
                && request.ChildSpawn.Enabled)
            {
                timedSpawn = new TimedSpawnComponent
                {
                    ChildKind = IntervalChildKind.Projectile,
                    TemplateKey = request.ChildSpawn.TemplateKey,
                    IntervalSeconds = request.ChildSpawn.IntervalSeconds,
                    IntervalJitterSeconds = request.ChildSpawn.IntervalJitterSeconds,
                    JitterSeed = request.ChildSpawn.JitterSeed
                };
            }

            return StampTimedSpawn(timedSpawn, CombatFaction.None, sourceId);
        }

        private static TimedSpawnComponent StampTimedSpawn(
            TimedSpawnComponent timedSpawn,
            CombatFaction faction,
            int sourceId)
        {
            if (!IsTimedSpawnEnabled(timedSpawn))
            {
                return default;
            }

            timedSpawn.Faction = faction;
            timedSpawn.SourceId = sourceId;
            return timedSpawn;
        }

        private static bool IsTimedSpawnEnabled(TimedSpawnComponent timedSpawn) =>
            timedSpawn.JitterSeed > 0
            && timedSpawn.IntervalSeconds > 0f
            && !timedSpawn.TemplateKey.Equals(default(Hash128));

        private static ProjectileSpawnCommand SpawnTemplateFor(in ProjectileSpawnCommand command)
        {
            ProjectileSpawnCommand template = command;
            template.Faction = CombatFaction.None;
            template.ProjectileId = 0;
            template.SeedContactGateTargetId = 0;
            template.Position = default;
            template.Velocity = default;
            template.BoundsMin = default;
            template.BoundsMax = default;
            template.JitterSeed = 0;
            template.DeterministicIdTickIndex = 0;
            CombatRenderComponent render = template.Render;
            render.RenderZ = 0f;
            template.Render = render;
            TimedSpawnComponent timedSpawn = template.TimedSpawn;
            timedSpawn.Faction = CombatFaction.None;
            timedSpawn.SourceId = 0;
            template.TimedSpawn = timedSpawn;
            ProjectileHitPayload hp = template.HitPayload;
            CombatHitPayload combat = hp.HitPayload;
            StackEffectSnapshot stack = combat.StackEffect;
            stack.Faction = CombatFaction.None;
            combat.StackEffect = stack;
            template.HitPayload = new ProjectileHitPayload(combat, hp.OnHitSpawn);
            return template;
        }

        private static AoeSpawnCommand SpawnTemplateFor(in AoeSpawnCommand command)
        {
            AoeSpawnCommand template = command;
            template.Faction = CombatFaction.None;
            template.AoeId = 0;
            template.Position = default;
            template.BoundsMin = default;
            template.BoundsMax = default;
            template.JitterSeed = 0;
            template.DeterministicIdTickIndex = 0;
            TimedSpawnComponent timedSpawn = template.TimedSpawn;
            timedSpawn.Faction = CombatFaction.None;
            timedSpawn.SourceId = 0;
            template.TimedSpawn = timedSpawn;
            CombatHitPayload hitPayload = template.HitPayload;
            StackEffectSnapshot stack = hitPayload.StackEffect;
            stack.Faction = CombatFaction.None;
            hitPayload.StackEffect = stack;
            template.HitPayload = hitPayload;
            return template;
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

        private const string ProjectileMeshName = "ProjectileQuadMesh";
        private const string AoeMeshName = "AoeQuadMesh";

        // The one render-resource registration entry point. Mints a render id from
        // the shared space, builds the GPU resources, and publishes them to the ECS
        // render registry under the batch id (== renderId). "A sprite is a sprite":
        // projectiles and AOEs both register here, distinguished only by mesh name.
        public int RegisterRenderResource(
            Sprite sprite,
            Vector2 visualScale,
            float visualRotationDegrees,
            Material sourceMaterial,
            string meshName)
        {
            if (sprite == null)
            {
                return 0;
            }

            CombatSpriteRenderResources resources = BatchedSpriteRenderer.BuildResources(
                sprite, visualScale, visualRotationDegrees, sourceMaterial, meshName);
            int renderId = nextRenderId++;
            renderResourcesById[renderId] = resources;
            if (_renderRegistry != null)
                _renderRegistry.Entries[renderId] = new CombatRenderResourceEntry
                {
                    Resources = resources,
                    Layer = gameObject.layer,
                    BoundsHalfExtent = batchBoundsHalfExtent
                };
            return renderId;
        }

        // Render id for a projectile behavior type id (0 when the type has no visual).
        internal int ProjectileRenderId(int projectileTypeId) =>
            projectileRenderIdByType.TryGetValue(projectileTypeId, out int renderId) ? renderId : 0;

        // Render id for an AOE behavior type id (0 when the type has no visual).
        internal int AoeRenderId(int aoeTypeId) =>
            aoeRenderIdByType.TryGetValue(aoeTypeId, out int renderId) ? renderId : 0;

        private void BuildProjectileRenderResources()
        {
            DestroyRenderResources();
            templateTypeIds.Clear();
            nextTemplateTypeId = 1;
            if (projectileSprite != null)
            {
                projectileRenderIdByType[0] = RegisterRenderResource(
                    projectileSprite, new Vector2(visualScale, visualScale), 0f, null, ProjectileMeshName);
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

                    projectileRenderIdByType[definition.TypeId] = RegisterRenderResource(
                        definition.Sprite,
                        ProjectileVisualScale(definition.VisualScale),
                        definition.VisualRotationDegrees,
                        null,
                        ProjectileMeshName);
                }
            }
        }

        private Vector2 ProjectileVisualScale(float scale)
        {
            float positiveScale = scale > 0f ? scale : visualScale;
            return new Vector2(positiveScale, positiveScale);
        }

        internal CombatRenderComponent ProjectileRenderComponentForRenderId(int renderId, int projectileId)
        {
            if (!renderResourcesById.TryGetValue(renderId, out CombatSpriteRenderResources resources))
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

            aoeRenderIdByType[typeId] = RegisterRenderResource(
                visual.Sprite, visual.VisualScale, visual.VisualRotationDegrees, visual.Material, AoeMeshName);
        }

        internal CombatRenderComponent AoeRenderComponentForRenderId(int renderId, AoeSpawnGeometry geometry)
        {
            if (!spawnVisuals || !renderResourcesById.ContainsKey(renderId))
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

        private void DestroyRenderResources()
        {
            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in renderResourcesById)
            {
                pair.Value.Destroy();
            }

            renderResourcesById.Clear();
            projectileRenderIdByType.Clear();
            aoeRenderIdByType.Clear();
        }

        // ---- World / scope ----

        private void BindWorld()
        {
            DisposeEcsHandles();
            ReleaseWorld();

            entityWorld = CombatEcsWorld.Acquire();
            ecsWorldAcquired = true;
            entityManager = entityWorld.EntityManager;
            scopeEntity = CombatScopeOwner.Acquire(entityManager);
            allProjectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>());
            allAoeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>());
            targetRegistry.ConfigureProxyBinding(entityManager);
            ecsHandlesCreated = true;
        }

        private int ActiveAoeCount()
        {
            if (!IsRuntimeReady())
            {
                return 0;
            }

            int count = entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity).Length;

            using NativeArray<Entity> entities = allAoeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.IsComponentEnabled<Active>(entities[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private void DestroyScopedEntities(EntityQuery query)
        {
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                entityManager.DestroyEntity(entities[i]);
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

        private void ValidateSpawnRequest(ProjectileSpawnRequest request)
        {
            ValidateRenderableType(request.ProjectileTypeId, nameof(request.ProjectileTypeId));
            if (request.ChildSpawn.Enabled)
            {
                ValidateRenderableType(request.ChildSpawn.TypeId, nameof(request.ChildSpawn));
            }
        }

        private void ValidateRenderableType(int projectileTypeId, string source)
        {
            if (!projectileRenderIdByType.ContainsKey(projectileTypeId))
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
