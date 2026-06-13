using System.Collections.Generic;
using PlayGround.Skills;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public sealed class ProjectileRoot : MonoBehaviour, ICombatScopeEndpoint
    {
        private const float ProjectileRenderZ = -0.25f;
        private const float ProjectileZStep = 0.000001f;
        private const int ProjectileZSlots = 1_000_000;
        private static readonly ProfilerMarker DrainHitsMarker = new("ProjectileRoot.DrainHits");

        [SerializeField] private Sprite projectileSprite;
        [SerializeField] private float visualScale = 1f;
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private string targetTag;
        [SerializeField] private BasicAttackPrefab[] projectileTemplates = global::System.Array.Empty<BasicAttackPrefab>();
        [SerializeField] private ProjectileRenderDefinition[] renderTypes = global::System.Array.Empty<ProjectileRenderDefinition>();
        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly CombatTargetRegistry<ICombatTarget> targetRegistry = new();
        private CombatTargetSync<ICombatTarget> targetSync;
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesByType = new();
        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allProjectileQuery;
        private IReadOnlyDictionary<int, ICombatTarget> runtimeTargetsById;
        private global::System.Func<ICombatTarget, bool> canTargetFilter;
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;
        private global::System.Action<DynamicBuffer<CombatTargetElement>> targetSyncCallback;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;
        private bool combatRuntimeManaged;

        public event CombatHitHandler Hit;
        public event CombatHitEffectHandler HitEffect;

        public CombatTargetRegistry<ICombatTarget> TargetRegistry => targetRegistry;
        public int TargetMask => targetLayers.value != 0 ? targetLayers.value : ~0;

        private void Awake()
        {
            runtimeReady = false;
            targetSync = new CombatTargetSync<ICombatTarget>(targetRegistry);
            canTargetFilter = CanTarget;
            targetSyncCallback = buffer => targetSync.SyncToBuffer(buffer, additionalFilter: canTargetFilter);
            ApplyTaggedDefaults();
            if (projectileSprite == null && !HasAnyRenderSource())
            {
                throw new MissingReferenceException($"{nameof(ProjectileRoot)} on {name} needs a projectile sprite or projectile template.");
            }

            BindWorld();
            BuildRenderResources();
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

            using (DrainHitsMarker.Auto())
            {
                DrainHits();
            }
        }

        private void OnDestroy()
        {
            if (HasValidEcsState())
            {
                if (scopeEntity != Entity.Null && entityManager.Exists(scopeEntity))
                {
                    entityManager.DestroyEntity(scopeEntity);
                }

                using var projectileEntities = allProjectileQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < projectileEntities.Length; i++)
                {
                    Entity entity = projectileEntities[i];
                    ProjectileIdentityComponent identity = entityManager.GetComponentData<ProjectileIdentityComponent>(entity);
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

        public void Configure(Sprite sprite)
        {
            projectileSprite = sprite;
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

            if (!renderResourcesByType.ContainsKey(typeId))
            {
                renderResourcesByType[typeId] = BuildRenderResourcesFor(template.Sprite, template.VisualScale, template.VisualRotationDegrees, template.Material);
            }

            return typeId;
        }

        public void ConfigureTargetBinding(LayerMask layers, string tag = null)
        {
            targetLayers = layers;
            targetTag = tag;
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

        public int Spawn(ProjectileSpawnCommand command, int seedContactGateTargetId = 0)
        {
            EnsureRuntimeReady();
            ValidateSpawnCommand(command);

            int projectileId = ++nextProjectileId;
            entityManager.GetBuffer<ProjectileSpawnRequestElement>(scopeEntity)
                .Add(SpawnRequestFor(command, projectileId, seedContactGateTargetId));
            return projectileId;
        }

        private ProjectileSpawnRequestElement SpawnRequestFor(ProjectileSpawnCommand command, int projectileId, int seedContactGateTargetId = 0)
        {
            float2 position = new(command.Position.x, command.Position.y);
            float2 velocity = new float2(command.Direction.x, command.Direction.y) * command.Speed;
            float2 halfExtents = new(command.HalfExtents.x, command.HalfExtents.y);
            ProjectileCollisionMath.ComputeWorldBounds(
                position,
                command.Radius,
                halfExtents,
                command.RotationRadians,
                command.ShapeType,
                out float2 boundsMin,
                out float2 boundsMax);

            var request = new ProjectileSpawnRequestElement
            {
                ProjectileId = projectileId,
                TypeId = command.ProjectileTypeId,
                TargetMask = command.TargetMask,
                PierceRemaining = command.PierceCount,
                HasChildSpawner = command.ChildSpawn.Enabled ? 1 : 0,
                SeedContactGateTargetId = seedContactGateTargetId,
                RepeatHitCooldownSeconds = command.RepeatHitCooldownSeconds,
                Lifetime = command.Lifetime,
                Radius = command.Radius,
                RotationRadians = command.RotationRadians,
                Position = position,
                Velocity = velocity,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = command.ShapeType,
                HitPayload = command.HitPayload,
                Tracking = TrackingComponentFor(command.Tracking),
                Render = RenderComponentFor(command.ProjectileTypeId, projectileId)
            };

            if (command.ChildSpawn.Enabled)
            {
                request.ChildSpawner = ChildSpawnerComponentFor(command.ChildSpawn);
                request.ChildSpawnState = new ProjectileChildSpawnStateComponent
                {
                    ChildSpawnCooldownRemaining = command.ChildSpawn.IntervalSeconds
                        + DeterministicJitter(projectileId, command.ChildSpawn.IntervalJitterSeconds),
                    ChildSpawnTickIndex = 0
                };
            }

            return request;
        }

        private void BindWorld()
        {
            DisposeEcsHandles();
            ReleaseWorld();

            entityWorld = CombatEcsWorld.Acquire();
            ecsWorldAcquired = true;
            entityManager = entityWorld.EntityManager;
            scopeEntity = entityManager.CreateEntity(typeof(ProjectileScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitPayloadElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitEffectElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileRecycleElement>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
            entityManager.AddComponentObject(scopeEntity, new CombatTargetSyncSource { Sync = combatRuntimeManaged ? null : targetSyncCallback });
            allProjectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>());
            entityManager.AddComponentObject(scopeEntity, new CombatScopeRenderCatalog
            {
                Resources = renderResourcesByType,
                Layer = gameObject.layer,
                BoundsHalfExtent = batchBoundsHalfExtent
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
                throw new global::System.InvalidOperationException($"{nameof(ProjectileRoot)} on {name} has not finished ECS setup.");
            }
        }

        private void DrainHits()
        {
            DynamicBuffer<CombatHitElement> hitBuffer =
                entityManager.GetBuffer<CombatHitElement>(scopeEntity);
            DynamicBuffer<CombatHitPayloadElement> payloadBuffer =
                entityManager.GetBuffer<CombatHitPayloadElement>(scopeEntity);
            DynamicBuffer<CombatHitEffectElement> effectBuffer =
                entityManager.GetBuffer<CombatHitEffectElement>(scopeEntity);
            if (combatRuntimeManaged && runtimeTargetsById != null)
            {
                var runtimeAdapter = new ProjectileHitReplayAdapter<ICombatTarget> { HitHandler = Hit, EffectHandler = HitEffect };
                CombatHitReplay.ReplayAndClear<ICombatTarget, ProjectileHitReplayAdapter<ICombatTarget>>(
                    hitBuffer,
                    payloadBuffer,
                    effectBuffer,
                    runtimeTargetsById,
                    ref runtimeAdapter);
                return;
            }

            var adapter = new ProjectileHitReplayAdapter<ICombatTarget> { HitHandler = Hit, EffectHandler = HitEffect };
            CombatHitReplay.ReplayAndClear<ICombatTarget, ProjectileHitReplayAdapter<ICombatTarget>>(
                hitBuffer,
                payloadBuffer,
                effectBuffer,
                targetSync.TargetsById,
                ref adapter);
        }

        private struct ProjectileHitReplayAdapter<TTarget> : ICombatHitReplayAdapter<TTarget>
            where TTarget : class, ICombatTarget
        {
            public CombatHitHandler HitHandler;
            public CombatHitEffectHandler EffectHandler;

            public DamageSnapshot RollDamage(in CombatHitElement hit)
            {
                bool isCrit = UnityEngine.Random.value < hit.CritChance;
                float rolledAmount = isCrit ? hit.DamageAmount * hit.CritMultiplier : hit.DamageAmount;
                return new DamageSnapshot(Mathf.Max(0f, rolledAmount), isCrit);
            }

            public void Replay(
                in CombatHitElement hit,
                in CombatHitPayloadElement payload,
                in CombatHitEffectElement effect,
                TTarget target,
                in DamageSnapshot damage)
            {
                var position = new Vector2(hit.Position.x, hit.Position.y);
                ICombatTarget combatTarget = target as ICombatTarget;
                var context = new CombatHitContext(
                    hit.Kind,
                    hit.SourceId,
                    hit.TypeId,
                    hit.TargetId,
                    position,
                    damage,
                    combatTarget,
                    hit.SourceNodeId);
                if (hit.EffectIndex >= 0)
                {
                    EffectHandler?.Invoke(in context, in effect);
                }

                HitHandler?.Invoke(in context);
                if (CombatHitReplay.IsTargetUsable(target))
                {
                    target.ReceiveHit(new CombatHitData(
                        CombatHitKind.Projectile,
                        damage,
                        position,
                        hit.DirectDamageEnabled,
                        payload.StackEffect));
                }
            }
        }

        private ProjectileChildSpawnerComponent ChildSpawnerComponentFor(ProjectileChildSpawnConfig config)
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
                TargetMask = config.TargetMask != 1 ? config.TargetMask : TargetMask,
                VisualScale = config.VisualScale > 0f ? config.VisualScale : 1f,
                VisualRotationSin = sin,
                VisualRotationCos = cos,
                TrackingEnabled = config.Tracking.Enabled,
                TrackingRangeSquared = config.Tracking.Range * config.Tracking.Range,
                TrackingTurnSpeedRadians = math.radians(config.Tracking.TurnSpeedDegrees),
                TrackingQueryIntervalSeconds = config.Tracking.QueryIntervalSeconds,
                TrackingInitialQueryDelaySeconds = config.Tracking.InitialQueryDelaySeconds,
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
                TrackingRangeSquared = config.Range * config.Range,
                TrackingTurnSpeedRadians = math.radians(config.TurnSpeedDegrees),
                TrackingQueryCooldownRemaining = config.InitialQueryDelaySeconds,
                TrackingQueryIntervalSeconds = config.QueryIntervalSeconds,
                TrackedTargetId = 0,
                TrackedTargetIndex = -1,
                TrackedTargetPosition = default,
                TrackingRandomState = 0
            };
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
            if (!renderResourcesByType.ContainsKey(projectileTypeId))
            {
                throw new global::System.InvalidOperationException(
                    $"Projectile render type {projectileTypeId} from {source} has no registered render resources.");
            }
        }

        private void BuildRenderResources()
        {
            DestroyRenderResources();
            templateTypeIds.Clear();
            nextTemplateTypeId = 1;
            if (projectileSprite != null)
            {
                renderResourcesByType[0] = BuildRenderResourcesFor(projectileSprite, visualScale, 0f);
            }

            if (projectileTemplates != null)
            {
                for (int i = 0; i < projectileTemplates.Length; i++)
                {
                    BasicAttackPrefab template = projectileTemplates[i];
                    if (template == null || template.Sprite == null)
                    {
                        continue;
                    }
                    RegisterTemplate(template);
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

                    renderResourcesByType[definition.TypeId] = BuildRenderResourcesFor(definition.Sprite, definition.VisualScale, definition.VisualRotationDegrees);
                }
            }
        }

        private CombatSpriteRenderResources BuildRenderResourcesFor(Sprite sprite, float scale, float visualRotationDegrees, Material sourceMaterial = null)
        {
            float positiveScale = scale > 0f ? scale : visualScale;
            return BatchedSpriteRenderer.BuildResources(
                sprite,
                new Vector2(positiveScale, positiveScale),
                visualRotationDegrees,
                sourceMaterial,
                "ProjectileQuadMesh");
        }

        private CombatRenderComponent RenderComponentFor(int projectileTypeId, int projectileId)
        {
            if (!renderResourcesByType.TryGetValue(projectileTypeId, out CombatSpriteRenderResources resources))
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
                RenderZ = ProjectileRenderZ - (projectileId % ProjectileZSlots) * ProjectileZStep
            };
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

            DisposeQuery(ref allProjectileQuery);
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

        private bool HasAnyRenderSource()
        {
            if (projectileTemplates != null)
            {
                for (int i = 0; i < projectileTemplates.Length; i++)
                {
                    if (projectileTemplates[i] != null && projectileTemplates[i].Sprite != null)
                    {
                        return true;
                    }
                }
            }

            if (renderTypes != null)
            {
                for (int i = 0; i < renderTypes.Length; i++)
                {
                    if (renderTypes[i] != null && renderTypes[i].Sprite != null)
                    {
                        return true;
                    }
                }
            }

            return false;
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
            using (DrainHitsMarker.Auto())
            {
                DrainHits();
            }
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
