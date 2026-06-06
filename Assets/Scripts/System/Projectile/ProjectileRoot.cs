using System.Collections.Generic;
using PlayGround.Attack;
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
        private const int MaxStructuralRenderTypes = 16;
        private const float ProjectileRenderZ = -0.25f;
        private const float ProjectileZStep = 0.000001f;
        private const int ProjectileZSlots = 1_000_000;
        private static readonly ProfilerMarker DrainHitsProfilerMarker = new("ProjectileRoot.DrainHits");
        private static readonly ProfilerMarker SubmitProjectilesProfilerMarker = new("ProjectileRoot.SubmitProjectiles");
        private static readonly ProfilerMarker ReplayProjectileHitEventsProfilerMarker = new("ProjectileRoot.ReplayProjectileHitEvents");


        [SerializeField] private Sprite projectileSprite;
        [SerializeField] private float visualScale = 1f;
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private string targetTag;
        [SerializeField] private BasicAttackPrefab[] projectileTemplates = global::System.Array.Empty<BasicAttackPrefab>();
        [SerializeField] private ProjectileRenderDefinition[] renderTypes = global::System.Array.Empty<ProjectileRenderDefinition>();
        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly ProjectileTargetRegistry targetRegistry = new();
        private readonly Dictionary<int, IProjectileTarget> targetsById = new();
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private readonly Dictionary<int, CombatSpriteRenderResources> renderResourcesByType = new();
        private CombatVfxDispatcher vfxDispatcher;

        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allProjectileQuery;
        private EntityQuery[] submitQueriesByType;
        private NativeArray<CombatRenderElement> submitBuffer;
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;
        private bool runtimeReady;
        private bool ecsWorldAcquired;
        private bool ecsHandlesCreated;

        public event global::System.Action<ProjectileHitContext> ProjectileHit;

        public ProjectileTargetRegistry TargetRegistry => targetRegistry;
        public int TargetMask => targetLayers.value != 0 ? targetLayers.value : ~0;

        private void Awake()
        {
            runtimeReady = false;
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

            using (SubmitProjectilesProfilerMarker.Auto())
            {
                SubmitProjectiles();
            }

            entityManager.CompleteDependencyBeforeRO<ProjectileActiveTag>();
            DrainVfxRequests();
            vfxDispatcher.Dispatch();

            using (DrainHitsProfilerMarker.Auto())
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
            if (target == null || (target.ProjectileTargetMask & TargetMask) == 0)
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

        public int Spawn(ProjectileSpawnCommand command)
        {
            EnsureRuntimeReady();
            ValidateSpawnCommand(command);

            int projectileId = ++nextProjectileId;
            entityManager.GetBuffer<ProjectileSpawnRequestElement>(scopeEntity)
                .Add(SpawnRequestFor(command, projectileId));
            return projectileId;
        }

        private ProjectileSpawnRequestElement SpawnRequestFor(ProjectileSpawnCommand command, int projectileId)
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
            entityManager.AddBuffer<ProjectileHitElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileRecycleElement>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
            allProjectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>());
            submitQueriesByType = new EntityQuery[MaxStructuralRenderTypes];
            submitQueriesByType[0]  = SubmitQuery<ProjectileRenderType0Tag>();
            submitQueriesByType[1]  = SubmitQuery<ProjectileRenderType1Tag>();
            submitQueriesByType[2]  = SubmitQuery<ProjectileRenderType2Tag>();
            submitQueriesByType[3]  = SubmitQuery<ProjectileRenderType3Tag>();
            submitQueriesByType[4]  = SubmitQuery<ProjectileRenderType4Tag>();
            submitQueriesByType[5]  = SubmitQuery<ProjectileRenderType5Tag>();
            submitQueriesByType[6]  = SubmitQuery<ProjectileRenderType6Tag>();
            submitQueriesByType[7]  = SubmitQuery<ProjectileRenderType7Tag>();
            submitQueriesByType[8]  = SubmitQuery<ProjectileRenderType8Tag>();
            submitQueriesByType[9]  = SubmitQuery<ProjectileRenderType9Tag>();
            submitQueriesByType[10] = SubmitQuery<ProjectileRenderType10Tag>();
            submitQueriesByType[11] = SubmitQuery<ProjectileRenderType11Tag>();
            submitQueriesByType[12] = SubmitQuery<ProjectileRenderType12Tag>();
            submitQueriesByType[13] = SubmitQuery<ProjectileRenderType13Tag>();
            submitQueriesByType[14] = SubmitQuery<ProjectileRenderType14Tag>();
            submitQueriesByType[15] = SubmitQuery<ProjectileRenderType15Tag>();
            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            submitBuffer = new NativeArray<CombatRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
            ecsHandlesCreated = true;
        }

        private EntityQuery SubmitQuery<T>() where T : unmanaged, IComponentData
        {
            return entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<T>(),
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
            targetBuffer.Clear();
            targetsById.Clear();

            IReadOnlyList<IProjectileTarget> targets = targetRegistry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                IProjectileTarget target = targets[i];
                if (target == null || !target.IsProjectileTargetActive || !CanTarget(target))
                {
                    continue;
                }

                Vector2 position = target.ProjectileTargetPosition;
                float2 targetPosition = new(position.x, position.y);
                float targetRadius = target.ProjectileTargetRadius;
                float2 targetHalfExtents = new(target.ProjectileTargetHalfExtents.x, target.ProjectileTargetHalfExtents.y);
                float targetRotationRadians = target.ProjectileTargetRotationRadians;
                CombatShapeType targetShapeType = target.ProjectileTargetShapeType;
                ProjectileCollisionMath.ComputeWorldBounds(
                    targetPosition,
                    targetRadius,
                    targetHalfExtents,
                    targetRotationRadians,
                    targetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                targetBuffer.Add(new CombatTargetElement
                {
                    TargetId = target.TargetId,
                    TargetMask = target.ProjectileTargetMask,
                    Position = targetPosition,
                    ShapeType = targetShapeType,
                    Radius = targetRadius,
                    HalfExtents = targetHalfExtents,
                    RotationRadians = targetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                });
                targetsById[target.TargetId] = target;
            }
        }

        private void DrainVfxRequests()
        {
            DynamicBuffer<VfxSpawnRequestElement> vfxBuffer =
                entityManager.GetBuffer<VfxSpawnRequestElement>(scopeEntity);
            for (int i = 0; i < vfxBuffer.Length; i++)
            {
                VfxSpawnRequestElement e = vfxBuffer[i];
                vfxDispatcher.StageSpawn(e.TypeId, e.Trigger, e.Position);
            }
            vfxBuffer.Clear();
        }

        // optimization: sort the buffer so that hits to the same target are clustered in the buffer.
        // this can be done using component and entity query, should help reduce cache misses and vtable lookups.
        private void DrainHits()
        {
            DynamicBuffer<ProjectileHitElement> hitBuffer =
                entityManager.GetBuffer<ProjectileHitElement>(scopeEntity);

            using (ReplayProjectileHitEventsProfilerMarker.Auto())
            {
                for (int i = 0; i < hitBuffer.Length; i++)
                {
                    ProjectileHitElement hit = hitBuffer[i];

                    targetsById.TryGetValue(hit.TargetId, out IProjectileTarget target);

                    ProjectileHitPayload payload = hit.HitPayload;

                    var context = new ProjectileHitContext(
                        hit.ProjectileId,
                        hit.ProjectileTypeId,
                        hit.TargetId,
                        new Vector2(hit.Position.x, hit.Position.y),
                        payload.Damage,
                        payload,
                        target);

                    ProjectileHit?.Invoke(context);

                    DispatchSourceHit(payload, context);

                    if (target != null)
                    {
                        target.ReceiveProjectileHitPayload(payload, context, ProjectileHitActorRole.Target);
                    }
                }
            }

            hitBuffer.Clear();
        }

        private static void DispatchSourceHit(ProjectileHitPayload payload, in ProjectileHitContext context)
        {
            if (payload.SourceNodeId.Equals(default(EntityId)))
            {
                return;
            }

            Object sourceObject = Resources.EntityIdToObject(payload.SourceNodeId);
            if (sourceObject is GameObject sourceGameObject
                && sourceGameObject.TryGetComponent(out IProjectileHitActor sourceActor))
            {
                sourceActor.ReceiveProjectileHitPayload(payload, context, ProjectileHitActorRole.Source);
                return;
            }

            if (sourceObject is Component sourceComponent
                && sourceComponent.TryGetComponent(out IProjectileHitActor componentActor))
            {
                componentActor.ReceiveProjectileHitPayload(payload, context, ProjectileHitActorRole.Source);
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
                ImpactAoe = config.ImpactAoe
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
            EnsureSupportedStructuralRenderType(projectileTypeId);
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

        private static void EnsureSupportedStructuralRenderType(int projectileTypeId)
        {
            if (projectileTypeId < 0 || projectileTypeId >= MaxStructuralRenderTypes)
            {
                throw new global::System.InvalidOperationException(
                    $"Projectile render type {projectileTypeId} is outside supported structural render type range 0-{MaxStructuralRenderTypes - 1}.");
            }
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
                submitQueriesByType = null;
                scopeEntity = Entity.Null;
                entityManager = default;
                return;
            }

            if (submitQueriesByType != null)
            {
                for (int i = 0; i < submitQueriesByType.Length; i++)
                {
                    DisposeQuery(ref submitQueriesByType[i]);
                }
            }

            DisposeQuery(ref allProjectileQuery);
            submitQueriesByType = null;
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
            if (renderResourcesByType.Count == 0 || submitQueriesByType == null)
            {
                return;
            }

            for (int typeId = 0; typeId < MaxStructuralRenderTypes; typeId++)
            {
                if (!renderResourcesByType.TryGetValue(typeId, out CombatSpriteRenderResources resources))
                {
                    continue;
                }

                EntityQuery query = submitQueriesByType[typeId];
                query.SetSharedComponentFilter(new CombatRenderScope { Scope = scopeEntity });
                entityManager.CompleteDependencyBeforeRO<CombatRenderElement>();
                NativeArray<CombatRenderElement> active =
                    query.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
                for (int start = 0; start < active.Length; start += MaxInstancesPerDraw)
                {
                    int count = Mathf.Min(MaxInstancesPerDraw, active.Length - start);
                    NativeArray<CombatRenderElement>.Copy(active, start, submitBuffer, 0, count);
                    BatchedSpriteRenderer.SubmitBatch(submitBuffer, 0, count, resources, gameObject.layer, batchBoundsHalfExtent);
                }
                active.Dispose();
                query.ResetFilter();
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
