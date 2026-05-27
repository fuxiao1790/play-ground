using System.Collections.Generic;
using System.Diagnostics;
using PlayGround.Attack;
using PlayGround.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace PlayGround.System.Projectile
{
    public sealed class ProjectileRoot : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private const float ProjectileRenderZ = -0.25f;
        private const int ProjectileRenderQueue = (int)RenderQueue.Transparent + 50;

        [SerializeField] private Sprite projectileSprite;
        [SerializeField] private float visualScale = 1f;
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private string targetTag;
        [SerializeField] private BasicAttackPrefab[] projectileTemplates = global::System.Array.Empty<BasicAttackPrefab>();
        [SerializeField] private ProjectileRenderDefinition[] renderTypes = global::System.Array.Empty<ProjectileRenderDefinition>();

        private readonly ProjectileTargetRegistry targetRegistry = new();
        private readonly Dictionary<int, IProjectileTarget> targetsById = new();
        private readonly Dictionary<BasicAttackPrefab, int> templateTypeIds = new();
        private readonly Dictionary<int, ProjectileRenderResources> renderResourcesByType = new();
        private readonly List<ProjectileHitReplay> pendingHits = new();
        private readonly List<ProjectileChildSpawnRequest> pendingChildSpawnRequests = new();
        private readonly Matrix4x4[] renderBatch = new Matrix4x4[MaxInstancesPerDraw];
        private readonly Stopwatch stopwatch = new();

        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery projectileQuery;
        private EntityQuery allProjectileQuery;
        private Mesh projectileMesh;
        private Material projectileMaterial;
        private MaterialPropertyBlock projectileProperties;
        private int nextProjectileId;
        private int nextTemplateTypeId = 1;
        private int spawnedProjectiles;
        private int despawnedProjectiles;
        private int hitEvents;
        private int childSpawnRequests;
        private float simulationMilliseconds;
        private float renderMilliseconds;

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
            childSpawnRequests,
            simulationMilliseconds,
            renderMilliseconds);

        private void Awake()
        {
            ApplyTaggedDefaults();
            if (projectileSprite == null && !HasAnyRenderSource())
            {
                throw new MissingReferenceException($"{nameof(ProjectileRoot)} on {name} needs a projectile sprite or projectile template.");
            }

            BindWorld();
            BuildRenderResources();
        }

        private void Update()
        {
            SyncTargetsToEcs();
        }

        private void LateUpdate()
        {
            DrainHits();
            DrainChildSpawnRequests();
            DrawProjectiles();
        }

        private void OnDestroy()
        {
            if (entityWorld != null && entityWorld.IsCreated)
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

            if (projectileMaterial != null)
            {
                Destroy(projectileMaterial);
            }

            if (projectileMesh != null)
            {
                Destroy(projectileMesh);
            }
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
                renderResourcesByType[typeId] = BuildRenderResourcesFor(template.Sprite, template.VisualScale, template.VisualRotationDegrees);
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
            Entity entity = entityManager.CreateEntity(typeof(ProjectileComponent), typeof(ProjectileActiveTag));
            int projectileId = ++nextProjectileId;
            entityManager.SetComponentData(entity, new ProjectileComponent
            {
                Scope = scopeEntity,
                ProjectileId = projectileId,
                TypeId = command.ProjectileTypeId,
                TargetMask = command.TargetMask,
                Position = new float2(command.Position.x, command.Position.y),
                Velocity = new float2(command.Direction.x, command.Direction.y) * command.Speed,
                Radius = command.Radius,
                HalfExtents = new float2(command.HalfExtents.x, command.HalfExtents.y),
                RotationRadians = command.RotationRadians,
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
            entityManager.AddBuffer<ProjectileContactGateElement>(entity);
            entityManager.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            spawnedProjectiles++;
            return projectileId;
        }

        public void Step(float deltaTime)
        {
            SyncTargetsToEcs();
            stopwatch.Restart();
            World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimulationSystemGroup>().Update();
            stopwatch.Stop();
            simulationMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
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
            scopeEntity = entityManager.CreateEntity(typeof(ProjectileScope));
            entityManager.AddBuffer<ProjectileTargetElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileHitElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileChildSpawnRequestElement>(scopeEntity);
            projectileQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileComponent>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
            allProjectileQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileComponent>());
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
                targetBuffer.Add(new ProjectileTargetElement
                {
                    TargetId = target.TargetId,
                    TargetMask = target.ProjectileTargetMask,
                    Position = new float2(position.x, position.y),
                    Radius = target.ProjectileTargetRadius,
                    HalfExtents = new float2(target.ProjectileTargetHalfExtents.x, target.ProjectileTargetHalfExtents.y),
                    RotationRadians = target.ProjectileTargetRotationRadians,
                    ShapeType = target.ProjectileTargetShapeType
                });
                targetsById[target.TargetId] = target;
            }
        }

        private void DrainHits()
        {
            DynamicBuffer<ProjectileHitElement> hitBuffer = entityManager.GetBuffer<ProjectileHitElement>(scopeEntity);
            pendingHits.Clear();
            for (int i = 0; i < hitBuffer.Length; i++)
            {
                ProjectileHitElement hit = hitBuffer[i];
                DamageSnapshot damage = new(hit.DamageAmount);
                targetsById.TryGetValue(hit.TargetId, out IProjectileTarget target);
                var context = new ProjectileHitContext(
                    hit.ProjectileId,
                    hit.ProjectileTypeId,
                    hit.TargetId,
                    new Vector2(hit.Position.x, hit.Position.y),
                    damage,
                    target);
                pendingHits.Add(new ProjectileHitReplay(context, hit.DirectDamageEnabled, target));
            }

            hitEvents += hitBuffer.Length;
            hitBuffer.Clear();

            for (int i = 0; i < pendingHits.Count; i++)
            {
                ProjectileHitReplay replay = pendingHits[i];
                ProjectileHitContext context = replay.Context;
                ProjectileHit?.Invoke(context);

                if (replay.DirectDamageEnabled && replay.Target != null)
                {
                    replay.Target.ReceiveProjectileHit(context.Damage);
                }
            }
        }

        private void DrainChildSpawnRequests()
        {
            DynamicBuffer<ProjectileChildSpawnRequestElement> requestBuffer =
                entityManager.GetBuffer<ProjectileChildSpawnRequestElement>(scopeEntity);
            pendingChildSpawnRequests.Clear();
            for (int i = 0; i < requestBuffer.Length; i++)
            {
                ProjectileChildSpawnRequestElement request = requestBuffer[i];
                pendingChildSpawnRequests.Add(new ProjectileChildSpawnRequest(
                    request.ProjectileId,
                    request.ProjectileTypeId,
                    request.ChildSpawnerId,
                    request.TickIndex,
                    new Vector2(request.Position.x, request.Position.y),
                    new Vector2(request.Velocity.x, request.Velocity.y),
                    new DamageSnapshot(request.DamageAmount)));
            }

            childSpawnRequests += requestBuffer.Length;
            requestBuffer.Clear();

            for (int i = 0; i < pendingChildSpawnRequests.Count; i++)
            {
                ChildSpawnRequested?.Invoke(pendingChildSpawnRequests[i]);
            }
        }

        private void BuildRenderResources()
        {
            renderResourcesByType.Clear();
            templateTypeIds.Clear();
            nextTemplateTypeId = 1;
            if (projectileSprite != null)
            {
                projectileMesh = BuildProjectileMesh(projectileSprite);
                Texture texture = projectileSprite.texture;
                projectileMaterial = new Material(FindProjectileShader())
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = ProjectileRenderQueue
                };
                ConfigureProjectileMaterial(projectileMaterial, texture);
                projectileProperties = new MaterialPropertyBlock();
                ConfigureProjectileProperties(projectileProperties, projectileMaterial, texture);
                renderResourcesByType[0] = new ProjectileRenderResources(projectileMesh, projectileMaterial, projectileProperties, visualScale, 0f);
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

        private ProjectileRenderResources BuildRenderResourcesFor(Sprite sprite, float scale, float visualRotationDegrees)
        {
            Mesh mesh = BuildProjectileMesh(sprite);
            Texture texture = sprite.texture;
            Material material = new(FindProjectileShader())
            {
                mainTexture = texture,
                enableInstancing = true,
                renderQueue = ProjectileRenderQueue
            };
            ConfigureProjectileMaterial(material, texture);
            MaterialPropertyBlock properties = new();
            ConfigureProjectileProperties(properties, material, texture);
            return new ProjectileRenderResources(mesh, material, properties, scale > 0f ? scale : visualScale, visualRotationDegrees);
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

        private void DrawProjectiles()
        {
            if (renderResourcesByType.Count == 0)
            {
                return;
            }

            stopwatch.Restart();
            using var projectiles = projectileQuery.ToComponentDataArray<ProjectileComponent>(Unity.Collections.Allocator.Temp);
            foreach (KeyValuePair<int, ProjectileRenderResources> pair in renderResourcesByType)
            {
                int batchCount = 0;
                for (int i = 0; i < projectiles.Length; i++)
                {
                    ProjectileComponent projectile = projectiles[i];
                    if (projectile.Scope != scopeEntity
                        || projectile.TypeId != pair.Key
                        || projectile.RemainingLifetime <= 0f)
                    {
                        continue;
                    }

                    float angle = math.atan2(projectile.Velocity.y, projectile.Velocity.x) * Mathf.Rad2Deg + pair.Value.VisualRotationDegrees;
                    renderBatch[batchCount++] = Matrix4x4.TRS(
                        new Vector3(projectile.Position.x, projectile.Position.y, ProjectileRenderZ),
                        Quaternion.Euler(0f, 0f, angle),
                        Vector3.one * pair.Value.VisualScale);

                    if (batchCount == MaxInstancesPerDraw)
                    {
                        DrawBatch(batchCount, pair.Value);
                        batchCount = 0;
                    }
                }

                if (batchCount > 0)
                {
                    DrawBatch(batchCount, pair.Value);
                }
            }

            stopwatch.Stop();
            renderMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
        }

        private void DrawBatch(int batchCount, ProjectileRenderResources resources)
        {
            Graphics.DrawMeshInstanced(
                resources.Mesh,
                0,
                resources.Material,
                renderBatch,
                batchCount,
                resources.Properties,
                ShadowCastingMode.Off,
                false,
                gameObject.layer);
        }

        private int CountRootProjectiles()
        {
            if (entityWorld == null || !entityWorld.IsCreated)
            {
                return 0;
            }

            using var projectiles = projectileQuery.ToComponentDataArray<ProjectileComponent>(Unity.Collections.Allocator.Temp);
            int count = 0;
            for (int i = 0; i < projectiles.Length; i++)
            {
                ProjectileComponent projectile = projectiles[i];
                if (projectile.Scope == scopeEntity && projectile.RemainingLifetime > 0f)
                {
                    count++;
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

        private readonly struct ProjectileRenderResources
        {
            public ProjectileRenderResources(Mesh mesh, Material material, MaterialPropertyBlock properties, float visualScale, float visualRotationDegrees)
            {
                Mesh = mesh;
                Material = material;
                Properties = properties;
                VisualScale = visualScale;
                VisualRotationDegrees = visualRotationDegrees;
            }

            public Mesh Mesh { get; }
            public Material Material { get; }
            public MaterialPropertyBlock Properties { get; }
            public float VisualScale { get; }
            public float VisualRotationDegrees { get; }
        }

        private readonly struct ProjectileHitReplay
        {
            public ProjectileHitReplay(ProjectileHitContext context, bool directDamageEnabled, IProjectileTarget target)
            {
                Context = context;
                DirectDamageEnabled = directDamageEnabled;
                Target = target;
            }

            public ProjectileHitContext Context { get; }
            public bool DirectDamageEnabled { get; }
            public IProjectileTarget Target { get; }
        }
    }
}
