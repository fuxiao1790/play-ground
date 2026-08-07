using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Authoring;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using System.Collections.Generic;
using PlayGround.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.U2D;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.System.Combat.Core
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
        public const float AoeRenderZ = 0.5f;
        public const int MaxSpawnChainDepth = SpawnTemplateLimits.MaxSpawnChainDepth;

        [Header("Render Atlas")]
        [Tooltip("Single-page Sprite Atlas that every combat sprite kind's Sprite must be a member " +
            "of. There is no runtime/dynamic atlas packing: the atlas is assembled in the editor " +
            "(Window > 2D > Sprite Atlas), skills register themselves against it, and registration " +
            "throws if a sprite is not part of it.")]
        [SerializeField] private SpriteAtlas combatSpriteAtlas;
        [Tooltip("Persistent scene/prefab MeshRenderer the combat sprite batch draws through. " +
            "Its Sorting Layer / Order in Layer are configured directly in this Renderer's " +
            "Inspector �?CombatRoot only assigns the shared mesh/material onto it, it never " +
            "touches sorting.")]
        [SerializeField] private MeshRenderer combatSpriteRenderer;

        [Header("Projectile visuals")]
        [SerializeField] private Sprite projectileSprite;
        [SerializeField] private float visualScale = 1f;
        [SerializeField] private BasicAttackPrefab[] projectileTemplates = global::System.Array.Empty<BasicAttackPrefab>();
        [SerializeField] private ProjectileRenderDefinition[] renderTypes = global::System.Array.Empty<ProjectileRenderDefinition>();

        private readonly CombatTargetRegistry<ICombatTarget> targetRegistry = new();

        // Per-domain typeId �?renderId maps. The registry owns the render-id counter
        // and GPU resources; these maps translate a behavior type id to its render id.
        private readonly Dictionary<int, int> projectileRenderIdByType = new();
        private readonly Dictionary<int, int> aoeRenderIdByType = new();
        private readonly Dictionary<int, int> targetedRenderIdByType = new();

        // Projectile state.
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;

        // AOE state.
        private readonly Dictionary<AoeTypeDefinition, int> definitionTypeIds = new();
        private AoeTypeRegistry typeRegistry = new();
        private int spawnedAoes;
        private int nextAoeId;
        private int nextTypeId = 1;

        // Targeted state.
        private readonly Dictionary<TargetedTypeDefinition, int> targetedDefinitionTypeIds = new();
        private TargetedTypeRegistry targetedTypeRegistry = new();
        private int nextTargetedId;
        private int nextTargetedTypeId = 1;

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
            ConfigureRenderRegistryAtlas();
            ConfigureRenderRegistryRenderer();
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
                CombatScopeOwner.ReleaseAfterWorldDispose(entityWorld, scopeEntity);
            }

            runtimeReady = false;
            targetRegistry.ClearProxyBinding();
            DisposeEcsHandles();
            ReleaseWorld();
            _renderRegistry?.Unregister();
            _renderRegistry = null;
        }

        // ---- Render atlas API ----

        public void ConfigureAtlas(SpriteAtlas atlas)
        {
            combatSpriteAtlas = atlas;
            ConfigureRenderRegistryAtlas();
            ConfigureRenderRegistryRenderer();
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
                projectileRenderIdByType[typeId] = _renderRegistry?.Register(
                    template.Sprite,
                    ProjectileVisualScale(template.VisualScale),
                    template.VisualRotationDegrees,
                    gameObject.layer) ?? 0;
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

        public int SpawnRegisteredProjectile(
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

        public int SpawnRegisteredProjectile(
            Hash128 templateKey,
            Vector2 position,
            Vector2 direction,
            int count,
            CombatFaction faction,
            Entity caster,
            float manaCost,
            int castToken,
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
            entityManager.GetBuffer<ExternalSpawnRequest>(scopeEntity).Add(new ExternalSpawnRequest
            {
                Kind = IntervalChildKind.Projectile,
                TemplateKey = templateKey,
                Caster = caster,
                ManaCost = math.max(0f, manaCost),
                Position = new float2(position.x, position.y),
                AimDirection = aim,
                Faction = faction,
                SourceId = baseProjectileId,
                JitterSeed = (uint)baseProjectileId * 2654435761u,
                ContactGateSeedTargetId = seedContactGateTargetId,
                CastToken = castToken
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

        // ---- Targeted API ----

        public Hash128 RegisterSpawnTemplate(in TargetedSpawnCommand template)
        {
            EnsureRuntimeReady();

            TargetedSpawnCommand normalizedTemplate = SpawnTemplateFor(in template);
            Hash128 key = SpawnTemplateHash.Of(in normalizedTemplate);
            TargetedSpawnTemplate registry = entityManager.GetComponentData<TargetedSpawnTemplate>(scopeEntity);
            if (!registry.Map.ContainsKey(key))
            {
                entityManager.CompleteAllTrackedJobs();
                registry.Map.Add(key, normalizedTemplate);
            }

            return key;
        }

        public Hash128 RegisterTimedSpawnTemplate(in TargetedSpawnCommand template) =>
            RegisterSpawnTemplate(in template);

        public int SpawnRegisteredTargeted(
            Hash128 templateKey,
            Vector2 origin,
            Vector2 acquireAnchor,
            int count,
            CombatFaction faction,
            IntervalChildKind kind,
            Entity caster = default,
            float manaCost = 0f,
            int castToken = 0)
        {
            EnsureRuntimeReady();
            if (templateKey.Equals(default(Hash128)))
            {
                return 0;
            }

            if (kind != IntervalChildKind.Targeted
                && kind != IntervalChildKind.LingeringTargeted)
            {
                throw new global::System.ArgumentOutOfRangeException(nameof(kind), kind,
                    "Registered targeted spawns require a targeted child kind.");
            }

            int targetedCount = math.max(1, count);
            int targetedId = ++nextTargetedId;
            nextTargetedId += targetedCount - 1;
            entityManager.GetBuffer<ExternalSpawnRequest>(scopeEntity).Add(new ExternalSpawnRequest
            {
                Kind = kind,
                TemplateKey = templateKey,
                Caster = caster,
                ManaCost = math.max(0f, manaCost),
                Position = new float2(origin.x, origin.y),
                AcquireAnchor = new float2(acquireAnchor.x, acquireAnchor.y),
                AimDirection = default,
                Faction = faction,
                SourceId = targetedId,
                JitterSeed = (uint)targetedId * 2654435761u,
                CastToken = castToken
            });
            return targetedId;
        }

        public int SpawnRegisteredAoe(
            Hash128 templateKey,
            Vector2 position,
            int count,
            CombatFaction faction,
            IntervalChildKind kind)
        {
            EnsureRuntimeReady();
            if (templateKey.Equals(default(Hash128)))
            {
                return 0;
            }

            int aoeCount = math.max(1, count);
            int aoeId = ++nextAoeId;
            nextAoeId += aoeCount - 1;
            AppendAoeSpawnEvent(
                kind,
                templateKey,
                new float2(position.x, position.y),
                default,
                faction,
                aoeId,
                (uint)aoeId * 2654435761u);
            spawnedAoes += aoeCount;
            return aoeId;
        }

        public int SpawnRegisteredAoe(
            Hash128 templateKey,
            Vector2 position,
            int count,
            CombatFaction faction,
            IntervalChildKind kind,
            Entity caster,
            float manaCost,
            int castToken)
        {
            EnsureRuntimeReady();
            if (templateKey.Equals(default(Hash128)))
            {
                return 0;
            }

            int aoeCount = math.max(1, count);
            int aoeId = ++nextAoeId;
            nextAoeId += aoeCount - 1;
            entityManager.GetBuffer<ExternalSpawnRequest>(scopeEntity).Add(new ExternalSpawnRequest
            {
                Kind = kind,
                TemplateKey = templateKey,
                Caster = caster,
                ManaCost = math.max(0f, manaCost),
                Position = new float2(position.x, position.y),
                AimDirection = default,
                Faction = faction,
                SourceId = aoeId,
                JitterSeed = (uint)aoeId * 2654435761u,
                CastToken = castToken
            });
            spawnedAoes += aoeCount;
            return aoeId;
        }

        public CombatRenderComponent ProjectileTemplateRenderComponent(int renderId)
        {
            if (_renderRegistry == null) return default;
            return _renderRegistry.GetProjectileRenderComponent(renderId, 0, out _);
        }

        public CombatRenderAuthoring ProjectileTemplateAuthoring(int renderId)
        {
            if (_renderRegistry == null) return default;
            _renderRegistry.GetProjectileRenderComponent(renderId, 0, out CombatRenderAuthoring authoring);
            return authoring;
        }

        public CombatRenderComponent AoeTemplateRenderComponent(int renderId, AoeSpawnGeometry geometry)
        {
            if (_renderRegistry == null) return default;
            return _renderRegistry.GetAoeRenderComponent(renderId, geometry, out _);
        }

        public CombatRenderAuthoring AoeTemplateAuthoring(int renderId, AoeSpawnGeometry geometry)
        {
            if (_renderRegistry == null) return default;
            _renderRegistry.GetAoeRenderComponent(renderId, geometry, out CombatRenderAuthoring authoring);
            return authoring;
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

        public int RegisterTargetedType(TargetedTypeDefinition definition)
        {
            if (definition == null)
            {
                throw new global::System.ArgumentNullException(nameof(definition));
            }

            if (targetedDefinitionTypeIds.TryGetValue(definition, out int existing))
            {
                return existing;
            }

            int typeId = nextTargetedTypeId++;
            targetedDefinitionTypeIds[definition] = typeId;
            targetedTypeRegistry.Register(typeId, definition);
            TryBuildTargetedRenderResource(typeId);
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
            AppendAoeEvent(AoeEventFor(templateKey, request, aoeId, faction));
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
            CombatRenderAuthoring authoring = default;
            CombatRenderComponent render = _renderRegistry != null
                ? _renderRegistry.GetProjectileRenderComponent(renderId, baseProjectileId, out authoring)
                : default;

            return new ProjectileSpawnCommand
            {
                Faction = CombatFaction.None,
                ProjectileId = baseProjectileId,
                TypeId = request.ProjectileTypeId,
                RenderTypeId = renderId,
                PierceRemaining = request.PierceCount,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                ContinuousCollision = request.ContinuousCollision ? 1 : 0,
                SeedContactGateTargetId = seedContactGateTargetId,
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds,
                Lifetime = request.Lifetime,
                ArmSeconds = 0f,
                Radius = request.Radius,
                RotationRadians = request.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                ShapeType = request.ShapeType,
                HitPayload = request.HitPayload,
                Tracking = TrackingComponentFor(request.Tracking),
                Render = render,
                Authoring = authoring,
                Count = request.Count,
                BaseDirection = new float2(request.Direction.x, request.Direction.y),
                Speed = request.Speed,
                SpreadDegrees = request.SpreadDegrees,
                JitterDegrees = request.JitterDegrees,
                JitterSeed = (uint)baseProjectileId * 2654435761u,
                TimedSpawn = timedSpawn
            };
        }

        private ImpactAoeSpawnEvent AoeEventFor(Hash128 templateKey, AoeSpawnRequest request, int aoeId, CombatFaction faction)
        {
            return new ImpactAoeSpawnEvent
            {
                Kind = AoeVariant.AoeChildKindFor(request.LifetimeSeconds),
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
            CombatRenderAuthoring authoring = default;
            CombatRenderComponent render = _renderRegistry != null
                ? _renderRegistry.GetAoeRenderComponent(renderId, geometry, out authoring)
                : default;

            return new AoeSpawnCommand
            {
                Faction = CombatFaction.None,
                AoeId = aoeId,
                TypeId = request.TypeId,
                VfxIds = VfxIdsFor(request.TypeId),
                RenderTypeId = renderId,
                Lifetime = request.LifetimeSeconds,
                ArmSeconds = 0f,
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
                Render = render,
                Authoring = authoring,
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
                    EnergyPerSecond = request.ChildSpawn.EnergyPerSecond,
                    EnergyThreshold = request.ChildSpawn.EnergyThreshold,
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
            timedSpawn.JitterSeed = unchecked((int)((uint)sourceId * 2654435761u));
            return timedSpawn;
        }

        private static bool IsTimedSpawnEnabled(TimedSpawnComponent timedSpawn) =>
            timedSpawn.JitterSeed > 0
            && timedSpawn.EnergyPerSecond > 0f
            && timedSpawn.EnergyThreshold > 0f
            && !timedSpawn.TemplateKey.Equals(default(Hash128));

        private AoeVfxIds VfxIdsFor(int typeId)
        {
            return typeRegistry.TryGetDefinition(typeId, out AoeTypeDefinition definition)
                ? definition.VfxIds
                : default;
        }

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

        private static TargetedSpawnCommand SpawnTemplateFor(in TargetedSpawnCommand command)
        {
            TargetedSpawnCommand template = command;
            template.Faction = CombatFaction.None;
            template.TargetedId = 0;
            template.InstanceIndex = 0;
            template.JitterSeed = 0;
            template.DeterministicIdTickIndex = 0;
            template.Origin = default;
            template.AcquireAnchor = default;
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

        // Render id for a projectile behavior type id (0 when the type has no visual).
        public int ProjectileRenderId(int projectileTypeId) =>
            projectileRenderIdByType.TryGetValue(projectileTypeId, out int renderId) ? renderId : 0;

        // Render id for an AOE behavior type id (0 when the type has no visual).
        public int AoeRenderId(int aoeTypeId) =>
            aoeRenderIdByType.TryGetValue(aoeTypeId, out int renderId) ? renderId : 0;

        // Render id for a targeted behavior type id (0 when the type has no visual).
        public int TargetedRenderId(int targetedTypeId) =>
            targetedRenderIdByType.TryGetValue(targetedTypeId, out int renderId) ? renderId : 0;

        public void SetAoeVfxIds(int aoeTypeId, AoeVfxIds vfxIds)
        {
            typeRegistry.SetVfxIds(aoeTypeId, vfxIds);
        }

        public void SetTargetedVfxIds(int targetedTypeId, TargetedVfxIds vfxIds)
        {
            targetedTypeRegistry.SetVfxIds(targetedTypeId, vfxIds);
        }

        private void BuildProjectileRenderResources()
        {
            _renderRegistry?.Unregister();
            ConfigureRenderRegistryAtlas();
            ConfigureRenderRegistryRenderer();
            projectileRenderIdByType.Clear();
            aoeRenderIdByType.Clear();
            targetedRenderIdByType.Clear();
            templateTypeIds.Clear();
            nextTemplateTypeId = 1;
            if (projectileSprite != null)
            {
                projectileRenderIdByType[0] = _renderRegistry?.Register(
                    projectileSprite, new Vector2(visualScale, visualScale), 0f, gameObject.layer) ?? 0;
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

                    projectileRenderIdByType[definition.TypeId] = _renderRegistry?.Register(
                        definition.Sprite,
                        ProjectileVisualScale(definition.VisualScale),
                        definition.VisualRotationDegrees,
                        gameObject.layer) ?? 0;
                }
            }
        }

        private Vector2 ProjectileVisualScale(float scale)
        {
            float positiveScale = scale > 0f ? scale : visualScale;
            return new Vector2(positiveScale, positiveScale);
        }

        private void TryBuildAoeRenderResource(int typeId)
        {
            if (!typeRegistry.TryGetVisual(typeId, out AoeVisualDefinition visual))
                return;
            ConfigureRenderRegistryAtlas();
            ConfigureRenderRegistryRenderer();
            aoeRenderIdByType[typeId] = _renderRegistry?.Register(
                visual.Sprite, visual.VisualScale, visual.VisualRotationDegrees, gameObject.layer) ?? 0;
        }

        private void TryBuildTargetedRenderResource(int typeId)
        {
            if (!targetedTypeRegistry.TryGetVisual(typeId, out TargetedVisualDefinition visual))
                return;
            ConfigureRenderRegistryAtlas();
            ConfigureRenderRegistryRenderer();
            targetedRenderIdByType[typeId] = _renderRegistry?.Register(
                visual.Sprite, visual.VisualScale, visual.VisualRotationDegrees, gameObject.layer) ?? 0;
        }

        private void ConfigureRenderRegistryAtlas()
        {
            _renderRegistry?.ConfigureAtlas(combatSpriteAtlas);
        }

        private void ConfigureRenderRegistryRenderer()
        {
            if (combatSpriteRenderer == null)
            {
                Debug.LogWarning("[CombatRoot] Combat Sprite Renderer is not assigned; combat sprites will not draw.");
                return;
            }

            MeshFilter meshFilter = combatSpriteRenderer.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                Debug.LogError("[CombatRoot] Combat Sprite Renderer has no MeshFilter.");
                return;
            }

            _renderRegistry?.AttachRenderer(meshFilter, combatSpriteRenderer);
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

            int count = entityManager.GetBuffer<ImpactAoeSpawnEvent>(scopeEntity).Length
                + entityManager.GetBuffer<LingeringAoeSpawnEvent>(scopeEntity).Length;

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

        private void AppendAoeEvent(ImpactAoeSpawnEvent evt)
        {
            AppendAoeSpawnEvent(
                evt.Kind,
                evt.TemplateKey,
                evt.Position,
                evt.AimDirection,
                evt.Faction,
                evt.SourceId,
                evt.JitterSeed,
                evt.DeterministicIdTickIndex,
                evt.ContactGateSeedTargetId);
        }

        private void AppendAoeSpawnEvent(
            IntervalChildKind kind,
            Hash128 templateKey,
            float2 position,
            float2 aimDirection,
            CombatFaction faction,
            int sourceId,
            uint jitterSeed,
            int deterministicIdTickIndex = 0,
            int contactGateSeedTargetId = 0)
        {
            if (kind == IntervalChildKind.Targeted
                || kind == IntervalChildKind.LingeringTargeted)
            {
                throw new global::System.InvalidOperationException(
                    $"Targeted spawn routing is not available for {kind}.");
            }

            if (kind == IntervalChildKind.LingeringAoe)
            {
                entityManager.GetBuffer<LingeringAoeSpawnEvent>(scopeEntity).Add(new LingeringAoeSpawnEvent
                {
                    Kind = kind,
                    TemplateKey = templateKey,
                    Position = position,
                    AimDirection = aimDirection,
                    Faction = faction,
                    SourceId = sourceId,
                    JitterSeed = jitterSeed,
                    DeterministicIdTickIndex = deterministicIdTickIndex,
                    ContactGateSeedTargetId = contactGateSeedTargetId
                });
                return;
            }

            if (kind == IntervalChildKind.ImpactAoe)
            {
                entityManager.GetBuffer<ImpactAoeSpawnEvent>(scopeEntity).Add(new ImpactAoeSpawnEvent
                {
                    Kind = IntervalChildKind.ImpactAoe,
                    TemplateKey = templateKey,
                    Position = position,
                    AimDirection = aimDirection,
                    Faction = faction,
                    SourceId = sourceId,
                    JitterSeed = jitterSeed,
                    DeterministicIdTickIndex = deterministicIdTickIndex,
                    ContactGateSeedTargetId = contactGateSeedTargetId
                });
                return;
            }

            throw new global::System.InvalidOperationException($"Unhandled interval child kind {kind}.");
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
