using System.Collections.Generic;
using PlayGround.Skills;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayGround.System.Projectile
{
    public sealed class ProjectileRoot : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private const int MaxVfxPerFrame = 2048;
        private const float ProjectileRenderZ = -0.25f;
        private const float ProjectileZStep = 0.000001f;
        private const int ProjectileZSlots = 1_000_000;
        private static readonly ProfilerMarker SubmitProjectilesMarker = new("ProjectileRoot.SubmitProjectiles");
        private static readonly ProfilerMarker DrainVfxMarker = new("ProjectileRoot.DrainVfxRequests");
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

        private readonly CombatTargetRegistry<IProjectileTarget> targetRegistry = new();
        private CombatTargetSync<IProjectileTarget> targetSync;
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesByType = new();
        private CombatVfxDispatcher vfxDispatcher;

        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allProjectileQuery;
        private EntityQuery submitQuery;
        private NativeArray<CombatRenderElement> submitBuffer;
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;

        public delegate void ProjectileHitHandler(in ProjectileHitContext context, in ProjectileHitPayload payload);

        public event ProjectileHitHandler ProjectileHit;

        public CombatTargetRegistry<IProjectileTarget> TargetRegistry => targetRegistry;
        public int TargetMask => targetLayers.value != 0 ? targetLayers.value : ~0;

        private void Awake()
        {
            runtimeReady = false;
            targetSync = new CombatTargetSync<IProjectileTarget>(targetRegistry);
            vfxDispatcher ??= new CombatVfxDispatcher(transform);
            ApplyTaggedDefaults();
            if (projectileSprite == null && !HasAnyRenderSource())
            {
                throw new MissingReferenceException($"{nameof(ProjectileRoot)} on {name} needs a projectile sprite or projectile template.");
            }

            BindWorld();
            BuildRenderResources();
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

            using (SubmitProjectilesMarker.Auto())
            {
                SubmitProjectiles();
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
                vfxDispatcher ??= new CombatVfxDispatcher(transform);
                vfxDispatcher.Register(typeId, 0, template.SpawnEffect, MaxVfxPerFrame);
                vfxDispatcher.Register(typeId, 1, template.HitEffect, MaxVfxPerFrame);
                vfxDispatcher.Register(typeId, 2, template.ExpireEffect, MaxVfxPerFrame);
            }

            return typeId;
        }

        public void ConfigureTargetBinding(LayerMask layers, string tag = null)
        {
            targetLayers = layers;
            targetTag = tag;
        }

        public bool CanTarget(IProjectileTarget target)
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
            entityManager.AddBuffer<ProjectileSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileRecycleElement>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
            allProjectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>());
            submitQuery = SubmitQuery();
            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            submitBuffer = new NativeArray<CombatRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
            ecsHandlesCreated = true;
        }

        private EntityQuery SubmitQuery()
        {
            return entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<CombatRenderTypeId>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
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

        private void SyncTargetsToEcs()
        {
            DynamicBuffer<CombatTargetElement> targetBuffer = entityManager.GetBuffer<CombatTargetElement>(scopeEntity);
            targetSync.SyncToBuffer(targetBuffer, additionalFilter: CanTarget);
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
            DynamicBuffer<CombatHitElement> hitBuffer =
                entityManager.GetBuffer<CombatHitElement>(scopeEntity);
            DynamicBuffer<CombatHitPayloadElement> payloadBuffer =
                entityManager.GetBuffer<CombatHitPayloadElement>(scopeEntity);
            var adapter = new ProjectileHitReplayAdapter { HitHandler = ProjectileHit };
            CombatHitReplay.ReplayAndClear<IProjectileTarget, ProjectileHitReplayAdapter>(
                hitBuffer,
                payloadBuffer,
                targetSync.TargetsById,
                ref adapter);
        }

        private struct ProjectileHitReplayAdapter : ICombatHitReplayAdapter<IProjectileTarget>
        {
            public ProjectileHitHandler HitHandler;

            public DamageSnapshot RollDamage(in CombatHitElement hit)
            {
                bool isCrit = UnityEngine.Random.value < hit.CritChance;
                float rolledAmount = isCrit ? hit.DamageAmount * hit.CritMultiplier : hit.DamageAmount;
                return new DamageSnapshot(Mathf.Max(0f, rolledAmount), isCrit);
            }

            public void Replay(in CombatHitElement hit, in CombatHitPayloadElement payload, IProjectileTarget target, in DamageSnapshot damage)
            {
                if (HitHandler != null)
                {
                    var hitPayload = new ProjectileHitPayload(
                        hit.SourceNodeId,
                        hit.DamageAmount,
                        hit.DirectDamageEnabled,
                        payload.ImpactAoe,
                        payload.StackEffect,
                        payload.ImpactProjectile,
                        hit.CritChance,
                        hit.CritMultiplier);
                    var context = new ProjectileHitContext(
                        hit.SourceId,
                        hit.TypeId,
                        hit.TargetId,
                        new Vector2(hit.Position.x, hit.Position.y),
                        damage,
                        target);
                    HitHandler.Invoke(in context, in hitPayload);
                }
                target?.ReceiveHit(new CombatHitData(
                    CombatHitKind.Projectile,
                    damage,
                    new Vector2(hit.Position.x, hit.Position.y),
                    hit.DirectDamageEnabled,
                    payload.StackEffect));
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
                TrackedTargetPosition = default
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

        // this function should ONLY submit projectiles rendering data.
        // DO NOT loop over individual projectiles.
        private void SubmitProjectiles()
        {
            entityManager.CompleteDependencyBeforeRO<ProjectileActiveTag>();
            entityManager.CompleteDependencyBeforeRO<CombatRenderElement>();
            if (renderResourcesByType.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in renderResourcesByType)
            {
                submitQuery.SetSharedComponentFilter(
                    new CombatRenderScope { Scope = scopeEntity },
                    new CombatRenderTypeId { TypeId = pair.Key });
                NativeArray<CombatRenderElement> active =
                    submitQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
                for (int start = 0; start < active.Length; start += MaxInstancesPerDraw)
                {
                    int count = Mathf.Min(MaxInstancesPerDraw, active.Length - start);
                    NativeArray<CombatRenderElement>.Copy(active, start, submitBuffer, 0, count);
                    BatchedSpriteRenderer.SubmitBatch(submitBuffer, 0, count, pair.Value, gameObject.layer, batchBoundsHalfExtent);
                }
                active.Dispose();
                submitQuery.ResetFilter();
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
