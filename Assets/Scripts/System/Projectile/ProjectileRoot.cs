using System.Collections.Generic;
using PlayGround.Attack;
using PlayGround.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace PlayGround.System.Projectile
{
    public sealed class ProjectileRoot : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private const int MaxStructuralRenderTypes = 16;
        private const int ProjectileRenderQueue = (int)RenderQueue.Transparent + 50;
        private static readonly global::System.Type[] RenderTagTypes =
        {
            typeof(ProjectileRenderType0Tag),
            typeof(ProjectileRenderType1Tag),
            typeof(ProjectileRenderType2Tag),
            typeof(ProjectileRenderType3Tag),
            typeof(ProjectileRenderType4Tag),
            typeof(ProjectileRenderType5Tag),
            typeof(ProjectileRenderType6Tag),
            typeof(ProjectileRenderType7Tag),
            typeof(ProjectileRenderType8Tag),
            typeof(ProjectileRenderType9Tag),
            typeof(ProjectileRenderType10Tag),
            typeof(ProjectileRenderType11Tag),
            typeof(ProjectileRenderType12Tag),
            typeof(ProjectileRenderType13Tag),
            typeof(ProjectileRenderType14Tag),
            typeof(ProjectileRenderType15Tag)
        };
        private static readonly global::System.Type[] RenderBatchTagTypes =
        {
            typeof(ProjectileRenderType0BatchTag),
            typeof(ProjectileRenderType1BatchTag),
            typeof(ProjectileRenderType2BatchTag),
            typeof(ProjectileRenderType3BatchTag),
            typeof(ProjectileRenderType4BatchTag),
            typeof(ProjectileRenderType5BatchTag),
            typeof(ProjectileRenderType6BatchTag),
            typeof(ProjectileRenderType7BatchTag),
            typeof(ProjectileRenderType8BatchTag),
            typeof(ProjectileRenderType9BatchTag),
            typeof(ProjectileRenderType10BatchTag),
            typeof(ProjectileRenderType11BatchTag),
            typeof(ProjectileRenderType12BatchTag),
            typeof(ProjectileRenderType13BatchTag),
            typeof(ProjectileRenderType14BatchTag),
            typeof(ProjectileRenderType15BatchTag)
        };
        private static readonly ProfilerMarker DrainHitsProfilerMarker = new("ProjectileRoot.DrainHits");
        private static readonly ProfilerMarker DrainChildSpawnRequestsProfilerMarker = new("ProjectileRoot.DrainChildSpawnRequests");
        private static readonly ProfilerMarker SubmitProjectilesProfilerMarker = new("ProjectileRoot.SubmitProjectiles");
        private static readonly ProfilerMarker ReplayProjectileHitEventsProfilerMarker = new("ProjectileRoot.ReplayProjectileHitEvents");
        private static readonly ProfilerMarker StepSimulationProfilerMarker = new("ProjectileRoot.StepSimulation");

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
        private readonly Dictionary<int, ProjectileRenderResources> renderResourcesByType = new();
        private readonly Dictionary<int, Entity> renderBatchEntitiesByType = new();
        private readonly Dictionary<int, EntityArchetype> projectileArchetypesByType = new();
        private readonly List<ProjectileHitReplay> pendingHits = new();
        private readonly List<ProjectileChildSpawnReplay> pendingChildSpawnRequests = new();
        private static readonly ProjectileHitReplayOrderComparer HitReplayOrderComparer = new();
        private static readonly ProjectileChildSpawnReplayOrderComparer ChildSpawnReplayOrderComparer = new();

        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery projectileQuery;
        private EntityQuery allProjectileQuery;
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;
        private int spawnedProjectiles;
        private int despawnedProjectiles;
        private int hitEvents;
        private int childSpawnRequests;
        private bool runtimeReady;

        public event global::System.Action<ProjectileHitContext> ProjectileHit;
        public event global::System.Action<ProjectileChildSpawnRequest> ChildSpawnRequested;

        public ProjectileTargetRegistry TargetRegistry => targetRegistry;
        public int TargetMask => targetLayers.value != 0 ? targetLayers.value : ~0;
        public int ActiveProjectileCount => CountRootProjectiles();
        public ProjectileRuntimeCounters Counters => new(
            ActiveProjectileCount,
            spawnedProjectiles,
            despawnedProjectiles,
            hitEvents,
            childSpawnRequests);

        private void Awake()
        {
            runtimeReady = false;
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

            using (DrainHitsProfilerMarker.Auto())
            {
                DrainHits();
            }

            using (DrainChildSpawnRequestsProfilerMarker.Auto())
            {
                DrainChildSpawnRequests();
            }

            using (SubmitProjectilesProfilerMarker.Auto())
            {
                SubmitProjectiles();
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

                using var projectileEntities = allProjectileQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < projectileEntities.Length; i++)
                {
                    Entity entity = projectileEntities[i];
                    ProjectileComponent projectile = entityManager.GetComponentData<ProjectileComponent>(entity);
                    if (projectile.Scope == scopeEntity)
                    {
                        entityManager.DestroyEntity(entity);
                    }
                }
            }
            runtimeReady = false;

            DestroyRenderBatchEntities();
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

            EnsureRenderBatchForType(typeId);
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
            Entity entity = entityManager.CreateEntity(ProjectileArchetypeFor(command.ProjectileTypeId));
            int projectileId = ++nextProjectileId;
            float2 position = new(command.Position.x, command.Position.y);
            float2 halfExtents = new(command.HalfExtents.x, command.HalfExtents.y);
            ProjectileCollisionMath.ComputeWorldBounds(
                position,
                command.Radius,
                halfExtents,
                command.RotationRadians,
                command.ShapeType,
                out float2 boundsMin,
                out float2 boundsMax);

            entityManager.SetComponentData(entity, new ProjectileComponent
            {
                Scope = scopeEntity,
                ProjectileId = projectileId,
                TypeId = command.ProjectileTypeId,
                TargetMask = command.TargetMask,
                Position = position,
                Velocity = new float2(command.Direction.x, command.Direction.y) * command.Speed,
                Radius = command.Radius,
                HalfExtents = halfExtents,
                RotationRadians = command.RotationRadians,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                RemainingLifetime = command.Lifetime,
                DamageAmount = command.Damage.Amount,
                DirectDamageEnabled = command.DirectDamageEnabled,
                ShapeType = command.ShapeType,
                PierceRemaining = command.PierceCount,
                RepeatHitCooldownSeconds = command.RepeatHitCooldownSeconds,
                TrackingEnabled = command.Tracking.Enabled,
                TrackingRangeSquared = command.Tracking.Range * command.Tracking.Range,
                TrackingTurnSpeedRadians = math.radians(command.Tracking.TurnSpeedDegrees),
                TrackingQueryCooldownRemaining = command.Tracking.InitialQueryDelaySeconds,
                TrackingQueryIntervalSeconds = command.Tracking.QueryIntervalSeconds,
                TrackedTargetId = 0,
                TrackedTargetIndex = -1,
                ChildSpawnerId = command.ChildSpawn.SpawnerId,
                ChildSpawnIntervalSeconds = command.ChildSpawn.IntervalSeconds,
                ChildSpawnCooldownRemaining = command.ChildSpawn.Enabled
                    ? command.ChildSpawn.IntervalSeconds + DeterministicJitter(projectileId, command.ChildSpawn.IntervalJitterSeconds)
                    : 0f,
                ChildSpawnTickIndex = 0
            });
            entityManager.SetComponentData(entity, RenderComponentFor(command.ProjectileTypeId));
            entityManager.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            spawnedProjectiles++;
            return projectileId;
        }

        public void Step(float deltaTime)
        {
            EnsureRuntimeReady();
            SyncTargetsToEcs();
            using (StepSimulationProfilerMarker.Auto())
            {
                World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimulationSystemGroup>().Update();
            }
            DrainHits();
            DrainChildSpawnRequests();
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
            projectileArchetypesByType.Clear();
            scopeEntity = entityManager.CreateEntity(typeof(ProjectileScope));
            entityManager.AddBuffer<ProjectileTargetElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileHitElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileChildSpawnRequestElement>(scopeEntity);
            projectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileComponent>(),
                ComponentType.ReadOnly<ProjectileRenderComponent>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
            allProjectileQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileComponent>());
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
            DynamicBuffer<ProjectileTargetElement> targetBuffer = entityManager.GetBuffer<ProjectileTargetElement>(scopeEntity);
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
                ProjectileShapeType targetShapeType = target.ProjectileTargetShapeType;
                ProjectileCollisionMath.ComputeWorldBounds(
                    targetPosition,
                    targetRadius,
                    targetHalfExtents,
                    targetRotationRadians,
                    targetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                targetBuffer.Add(new ProjectileTargetElement
                {
                    TargetId = target.TargetId,
                    TargetMask = target.ProjectileTargetMask,
                    Position = targetPosition,
                    Radius = targetRadius,
                    HalfExtents = targetHalfExtents,
                    RotationRadians = targetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    ShapeType = targetShapeType
                });
                targetsById[target.TargetId] = target;
            }
        }

        // optimization: sort the buffer so that hits to the same target are clustered in the buffer. 
        // this can be done using component and entity query, should help reduce cache misses and vtable lookups.
        private void DrainHits()
        {
            DynamicBuffer<ProjectileHitElement> hitBuffer =
                entityManager.GetBuffer<ProjectileHitElement>(scopeEntity);

            hitEvents += hitBuffer.Length;


            using (ReplayProjectileHitEventsProfilerMarker.Auto())
            {
                for (int i = 0; i < hitBuffer.Length; i++)
                {
                    ProjectileHitElement hit = hitBuffer[i];

                    targetsById.TryGetValue(hit.TargetId, out IProjectileTarget target);

                    DamageSnapshot damage = new(hit.DamageAmount);

                    var context = new ProjectileHitContext(
                        hit.ProjectileId,
                        hit.ProjectileTypeId,
                        hit.TargetId,
                        new Vector2(hit.Position.x, hit.Position.y),
                        damage,
                        target);

                    ProjectileHit?.Invoke(context);

                    if (hit.DirectDamageEnabled && target != null)
                    {
                        target.ReceiveProjectileHit(context.Damage);
                    }
                }
            }

            hitBuffer.Clear();
        }

        private void DrainChildSpawnRequests()
        {
            DynamicBuffer<ProjectileChildSpawnRequestElement> requestBuffer =
                entityManager.GetBuffer<ProjectileChildSpawnRequestElement>(scopeEntity);
            pendingChildSpawnRequests.Clear();
            for (int i = 0; i < requestBuffer.Length; i++)
            {
                ProjectileChildSpawnRequestElement request = requestBuffer[i];
                pendingChildSpawnRequests.Add(new ProjectileChildSpawnReplay(
                    new ProjectileChildSpawnRequest(
                        request.ProjectileId,
                        request.ProjectileTypeId,
                        request.ChildSpawnerId,
                        request.TickIndex,
                        new Vector2(request.Position.x, request.Position.y),
                        new Vector2(request.Velocity.x, request.Velocity.y),
                        new DamageSnapshot(request.DamageAmount)),
                    request.Order));
            }

            childSpawnRequests += requestBuffer.Length;
            requestBuffer.Clear();
            if (pendingChildSpawnRequests.Count > 1)
            {
                pendingChildSpawnRequests.Sort(ChildSpawnReplayOrderComparer);
            }

            for (int i = 0; i < pendingChildSpawnRequests.Count; i++)
            {
                ChildSpawnRequested?.Invoke(pendingChildSpawnRequests[i].Request);
            }
        }

        private void BuildRenderResources()
        {
            DestroyRenderBatchEntities();
            renderResourcesByType.Clear();
            templateTypeIds.Clear();
            nextTemplateTypeId = 1;
            if (projectileSprite != null)
            {
                renderResourcesByType[0] = BuildRenderResourcesFor(projectileSprite, visualScale, 0f);
                EnsureRenderBatchForType(0);
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
                    EnsureRenderBatchForType(definition.TypeId);
                }
            }
        }

        private ProjectileRenderResources BuildRenderResourcesFor(Sprite sprite, float scale, float visualRotationDegrees, Material sourceMaterial = null)
        {
            Mesh mesh = BuildProjectileMesh(sprite);
            Texture texture = sprite.texture;
            Material material;
            if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial)
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = ProjectileRenderQueue
                };
                // Ensure cloned authoring materials receive the same property setup
                // as the fallback material so URP/Shader graph properties like
                // _BaseMap are populated and keywords/tags are configured.
                ConfigureProjectileMaterial(material, texture);
            }
            else
            {
                material = new(FindProjectileShader())
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = ProjectileRenderQueue
                };
                ConfigureProjectileMaterial(material, texture);
            }

            // Sanity checks: instanced rendering requires a valid material with a main texture
            if (material == null)
            {
                throw new MissingReferenceException($"Projectile render material could not be created for sprite {sprite.name}.");
            }

            if (material.mainTexture == null)
            {
                throw new MissingReferenceException($"Projectile render material for sprite {sprite.name} has no main texture assigned.");
            }

            if (!material.enableInstancing)
            {
                throw new global::System.InvalidOperationException($"Projectile render material for sprite {sprite.name} does not support GPU instancing.");
            }

            if (material.shader == null || !material.shader.isSupported)
            {
                throw new MissingReferenceException($"Projectile render material shader is not supported for sprite {sprite.name}.");
            }
            MaterialPropertyBlock properties = new();
            ConfigureProjectileProperties(properties, material, texture);
            return new ProjectileRenderResources(mesh, material, properties, scale > 0f ? scale : visualScale, visualRotationDegrees);
        }

        private ProjectileRenderComponent RenderComponentFor(int projectileTypeId)
        {
            if (!renderResourcesByType.TryGetValue(projectileTypeId, out ProjectileRenderResources resources))
            {
                return default;
            }

            return new ProjectileRenderComponent
            {
                IsRenderable = 1,
                VisualScale = resources.VisualScale,
                VisualRotationSin = resources.VisualRotationSin,
                VisualRotationCos = resources.VisualRotationCos
            };
        }

        private void EnsureRenderBatchForType(int projectileTypeId)
        {
            EnsureSupportedStructuralRenderType(projectileTypeId);
            if (!HasValidEcsState() || renderBatchEntitiesByType.ContainsKey(projectileTypeId))
            {
                return;
            }

            Entity batchEntity = entityManager.CreateEntity(typeof(ProjectileRenderBatch), RenderBatchTagTypeFor(projectileTypeId));
            entityManager.SetComponentData(batchEntity, new ProjectileRenderBatch
            {
                Scope = scopeEntity,
                TypeId = projectileTypeId
            });
            entityManager.AddBuffer<ProjectileRenderElement>(batchEntity);
            renderBatchEntitiesByType.Add(projectileTypeId, batchEntity);
        }

        private EntityArchetype ProjectileArchetypeFor(int projectileTypeId)
        {
            EnsureSupportedStructuralRenderType(projectileTypeId);
            if (projectileArchetypesByType.TryGetValue(projectileTypeId, out EntityArchetype archetype))
            {
                return archetype;
            }

            archetype = entityManager.CreateArchetype(
                typeof(ProjectileComponent),
                typeof(ProjectileRenderComponent),
                RenderTagTypeFor(projectileTypeId),
                typeof(ProjectileActiveTag),
                typeof(ProjectileContactGateElement));
            projectileArchetypesByType.Add(projectileTypeId, archetype);
            return archetype;
        }

        private static void EnsureSupportedStructuralRenderType(int projectileTypeId)
        {
            if (projectileTypeId < 0 || projectileTypeId >= MaxStructuralRenderTypes)
            {
                throw new global::System.InvalidOperationException(
                    $"Projectile render type {projectileTypeId} is outside supported structural render type range 0-{MaxStructuralRenderTypes - 1}.");
            }
        }

        private static global::System.Type RenderTagTypeFor(int projectileTypeId)
        {
            EnsureSupportedStructuralRenderType(projectileTypeId);
            return RenderTagTypes[projectileTypeId];
        }

        private static global::System.Type RenderBatchTagTypeFor(int projectileTypeId)
        {
            EnsureSupportedStructuralRenderType(projectileTypeId);
            return RenderBatchTagTypes[projectileTypeId];
        }

        private void DestroyRenderBatchEntities()
        {
            if (entityWorld == null || !entityWorld.IsCreated || entityManager == default)
            {
                renderBatchEntitiesByType.Clear();
                return;
            }

            foreach (KeyValuePair<int, Entity> pair in renderBatchEntitiesByType)
            {
                Entity batchEntity = pair.Value;
                if (batchEntity != Entity.Null && entityManager.Exists(batchEntity))
                {
                    entityManager.DestroyEntity(batchEntity);
                }
            }

            renderBatchEntitiesByType.Clear();
        }

        private void DestroyRenderResources()
        {
            foreach (KeyValuePair<int, ProjectileRenderResources> pair in renderResourcesByType)
            {
                pair.Value.Destroy();
            }

            renderResourcesByType.Clear();
        }

        private static Mesh BuildProjectileMesh(Sprite sprite)
        {
            Mesh mesh = new() { name = "ProjectileQuadMesh" };
            Rect rect = sprite.rect;
            float pixelsPerUnit = sprite.pixelsPerUnit;
            float width = rect.width / pixelsPerUnit;
            float height = rect.height / pixelsPerUnit;
            Vector4 outerUv = DataUtility.GetOuterUV(sprite);

            var vertices = new[]
            {
                new Vector3(-width * 0.5f, -height * 0.5f, 0f),
                new Vector3(-width * 0.5f, height * 0.5f, 0f),
                new Vector3(width * 0.5f, height * 0.5f, 0f),
                new Vector3(width * 0.5f, -height * 0.5f, 0f)
            };
            var uvs = new[]
            {
                new Vector2(outerUv.x, outerUv.y),
                new Vector2(outerUv.x, outerUv.w),
                new Vector2(outerUv.z, outerUv.w),
                new Vector2(outerUv.z, outerUv.y)
            };

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Shader FindProjectileShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                return shader;
            }

            throw new MissingReferenceException("No supported projectile render shader found.");
        }

        private static void ConfigureProjectileMaterial(Material material, Texture texture)
        {
            material.enableInstancing = true;
            material.renderQueue = ProjectileRenderQueue;
            material.SetTexture("_MainTex", texture);

            SetTextureIfPresent(material, "_BaseMap", texture);
            SetColorIfPresent(material, "_Color", Color.white);
            SetColorIfPresent(material, "_BaseColor", Color.white);
            SetColorIfPresent(material, "_RendererColor", Color.white);
            SetFloatIfPresent(material, "_Surface", 1f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);

            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        private static void ConfigureProjectileProperties(MaterialPropertyBlock properties, Material material, Texture texture)
        {
            properties.SetTexture("_MainTex", texture);

            if (material.HasProperty("_BaseMap"))
            {
                properties.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_Color"))
            {
                properties.SetColor("_Color", Color.white);
            }

            if (material.HasProperty("_BaseColor"))
            {
                properties.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_RendererColor"))
            {
                properties.SetColor("_RendererColor", Color.white);
            }
        }

        private static void SetTextureIfPresent(Material material, string propertyName, Texture texture)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, texture);
            }
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        // this function should ONLY submit projectiles rendering data.
        // DO NOT loop over individual projectiles.
        private void SubmitProjectiles()
        {
            if (renderResourcesByType.Count == 0 || renderBatchEntitiesByType.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, Entity> pair in renderBatchEntitiesByType)
            {
                int typeId = pair.Key;
                Entity batchEntity = pair.Value;
                if (batchEntity == Entity.Null
                    || !entityManager.Exists(batchEntity)
                    || !entityManager.HasBuffer<ProjectileRenderElement>(batchEntity)
                    || !renderResourcesByType.TryGetValue(typeId, out ProjectileRenderResources resources))
                {
                    continue;
                }

                DynamicBuffer<ProjectileRenderElement> renderBuffer = entityManager.GetBuffer<ProjectileRenderElement>(batchEntity);
                NativeArray<ProjectileRenderElement> instances = renderBuffer.AsNativeArray();
                for (int startInstance = 0; startInstance < renderBuffer.Length; startInstance += MaxInstancesPerDraw)
                {
                    int instanceCount = Mathf.Min(MaxInstancesPerDraw, renderBuffer.Length - startInstance);
                    SubmitBatch(instances, startInstance, instanceCount, resources);
                }
            }
        }

        private void SubmitBatch(
            NativeArray<ProjectileRenderElement> instances,
            int startInstance,
            int instanceCount,
            ProjectileRenderResources resources)
        {
            if (instanceCount <= 0)
            {
                return;
            }
            Graphics.RenderMeshInstanced(
                new RenderParams(resources.Material)
                {
                    matProps = resources.Properties,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    layer = gameObject.layer,
                    worldBounds = new Bounds(
                        Vector3.zero, 
                        new Vector3(batchBoundsHalfExtent, batchBoundsHalfExtent, batchBoundsHalfExtent) * 2f)
                },
                resources.Mesh,
                0,
                instances,
                instanceCount,
                startInstance);
        }

        private int CountRootProjectiles()
        {
            if (!IsRuntimeReady())
            {
                return 0;
            }

            ComponentTypeHandle<ProjectileComponent> projectileTypeHandle =
                entityManager.GetComponentTypeHandle<ProjectileComponent>(true);
            ComponentTypeHandle<ProjectileActiveTag> activeTypeHandle =
                entityManager.GetComponentTypeHandle<ProjectileActiveTag>(true);
            using NativeArray<ArchetypeChunk> chunks = projectileQuery.ToArchetypeChunkArray(Allocator.Temp);
            int count = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                ArchetypeChunk chunk = chunks[chunkIndex];
                NativeArray<ProjectileComponent> projectiles = chunk.GetNativeArray(ref projectileTypeHandle);
                EnabledMask activeMask = chunk.GetEnabledMask(ref activeTypeHandle);
                for (int i = 0; i < projectiles.Length; i++)
                {
                    if (!activeMask.GetBit(i))
                    {
                        continue;
                    }

                    ProjectileComponent projectile = projectiles[i];
                    if (projectile.Scope == scopeEntity)
                    {
                        count++;
                    }
                }
            }

            return count;
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

        private sealed class ProjectileRenderResources
        {
            public ProjectileRenderResources(Mesh mesh, Material material, MaterialPropertyBlock properties, float visualScale, float visualRotationDegrees)
            {
                Mesh = mesh;
                Material = material;
                Properties = properties;
                VisualScale = visualScale;
                math.sincos(math.radians(visualRotationDegrees), out float visualRotationSin, out float visualRotationCos);
                VisualRotationSin = visualRotationSin;
                VisualRotationCos = visualRotationCos;
            }

            public Mesh Mesh { get; }
            public Material Material { get; }
            public MaterialPropertyBlock Properties { get; }
            public float VisualScale { get; }
            public float VisualRotationSin { get; }
            public float VisualRotationCos { get; }

            public void Destroy()
            {
                if (Material != null)
                {
                    UnityEngine.Object.Destroy(Material);
                }

                if (Mesh != null)
                {
                    UnityEngine.Object.Destroy(Mesh);
                }
            }
        }

        private readonly struct ProjectileHitReplay
        {
            public ProjectileHitReplay(ProjectileHitContext context, bool directDamageEnabled, IProjectileTarget target, uint order)
            {
                Context = context;
                DirectDamageEnabled = directDamageEnabled;
                Target = target;
                Order = order;
            }

            public ProjectileHitContext Context { get; }
            public bool DirectDamageEnabled { get; }
            public IProjectileTarget Target { get; }
            public uint Order { get; }
        }

        private readonly struct ProjectileChildSpawnReplay
        {
            public ProjectileChildSpawnReplay(ProjectileChildSpawnRequest request, uint order)
            {
                Request = request;
                Order = order;
            }

            public ProjectileChildSpawnRequest Request { get; }
            public uint Order { get; }
        }

        private sealed class ProjectileHitReplayOrderComparer : IComparer<ProjectileHitReplay>
        {
            public int Compare(ProjectileHitReplay left, ProjectileHitReplay right)
            {
                return left.Order.CompareTo(right.Order);
            }
        }

        private sealed class ProjectileChildSpawnReplayOrderComparer : IComparer<ProjectileChildSpawnReplay>
        {
            public int Compare(ProjectileChildSpawnReplay left, ProjectileChildSpawnReplay right)
            {
                return left.Order.CompareTo(right.Order);
            }
        }
    }
}
