using System.Collections.Generic;
using PlayGround.Attack;
using PlayGround.Common;
using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace PlayGround.System.Aoe
{
    public sealed class AoeRoot : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private const int AoeRenderQueue = (int)RenderQueue.Transparent + 45;
        private static readonly ProfilerMarker SyncTargetsProfilerMarker = new("AoeRoot.SyncTargets");
        private static readonly ProfilerMarker SubmitAoesProfilerMarker = new("AoeRoot.SubmitAoes");
        private static readonly ProfilerMarker DrainEventsProfilerMarker = new("AoeRoot.DrainEvents");
        private static readonly ProfilerMarker StepSimulationProfilerMarker = new("AoeRoot.StepSimulation");

        [SerializeField] private AoeTypeDefinition[] aoeTypes = global::System.Array.Empty<AoeTypeDefinition>();
        [SerializeField] private int targetMask = 1;
        [SerializeField, Min(0)] private int maximumAoeCount = 10000;
        [SerializeField, Min(0)] private int maximumTargetCount = 100;
        [SerializeField] private bool spawnVisuals = true;
        [SerializeField, Tooltip("Half-extent used for the batch world bounds. Increase to avoid GPU culling; decrease for tighter culling.")]
        [Min(0f)]
        private float batchBoundsHalfExtent = 100000f;

        private readonly AoeTargetRegistry targetRegistry = new();
        private readonly Dictionary<int, AoeRenderResources> renderResourcesByType = new();

        private AoeTypeRegistry typeRegistry;
        private AoeTargetSync targetSync;
        private World entityWorld;
        private EntityManager entityManager;
        private Entity scopeEntity;
        private EntityQuery allAoeQuery;
        private EntityQuery submitQuery;
        private NativeArray<AoeRenderElement> submitBuffer;
        private int spawnedAoes;
        private int despawnedAoes;
        private int hitEvents;
        private int activeVisuals;
        private int nextAoeId;
        private bool runtimeReady;

        public event global::System.Action<AoeHitContext> AoeHit;

        public AoeTargetRegistry TargetRegistry => targetRegistry;
        public AoeRuntimeCounters Counters => new(
            ActiveAoeCount(),
            spawnedAoes,
            despawnedAoes,
            hitEvents,
            activeVisuals);

        private void Awake()
        {
            runtimeReady = false;
            typeRegistry = new AoeTypeRegistry();
            targetSync = new AoeTargetSync(targetRegistry);
            BindWorld();

            for (int i = 0; i < aoeTypes.Length; i++)
            {
                typeRegistry.Register(aoeTypes[i]);
            }

            BuildRenderResources();
            runtimeReady = true;
        }

        private void Update()
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            using (SyncTargetsProfilerMarker.Auto())
            {
                SyncTargetsToEcs();
            }
        }

        private void LateUpdate()
        {
            if (!EnsureRuntimeAvailable())
            {
                return;
            }

            using (SubmitAoesProfilerMarker.Auto())
            {
                SubmitAoes();
            }

            using (DrainEventsProfilerMarker.Auto())
            {
                DrainEvents();
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

            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            DestroyRenderResources();
        }

        public void Configure(AoeTypeDefinition[] definitions, int mask)
        {
            aoeTypes = definitions ?? global::System.Array.Empty<AoeTypeDefinition>();
            targetMask = mask;
            if (!runtimeReady)
            {
                return;
            }

            typeRegistry = new AoeTypeRegistry();
            for (int i = 0; i < aoeTypes.Length; i++)
            {
                typeRegistry.Register(aoeTypes[i]);
            }

            DestroyRenderResources();
            BuildRenderResources();
        }

        public int Spawn(ProjectileAoeSpawnRequest request)
        {
            return Spawn(new AoeSpawnCommand(
                request.EffectTypeId,
                request.Position,
                targetMask,
                request.Damage,
                request.LifetimeSeconds,
                request.TickIntervalSeconds));
        }

        public int Spawn(AoeSpawnCommand command)
        {
            EnsureRuntimeReady();
            if (!typeRegistry.TryGetShape(command.TypeId, out AoeShape shape))
            {
                throw new global::System.InvalidOperationException($"Missing AOE collision definition for type id {command.TypeId}.");
            }

            if (ActiveAoeCount() >= maximumAoeCount)
            {
                return 0;
            }

            int aoeId = ++nextAoeId;
            entityManager.GetBuffer<AoeSpawnRequestElement>(scopeEntity)
                .Add(SpawnRequestFor(command, shape, aoeId));
            spawnedAoes++;
            return aoeId;
        }

        public void Step(float deltaTime)
        {
            EnsureRuntimeReady();
            SyncTargetsToEcs();
            using (StepSimulationProfilerMarker.Auto())
            {
                World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimulationSystemGroup>().Update();
            }
            DrainEvents();
        }

        private AoeSpawnRequestElement SpawnRequestFor(AoeSpawnCommand command, AoeShape shape, int aoeId)
        {
            float2 position = new(command.Position.x, command.Position.y);
            float2 halfExtents = new(shape.HalfExtents.x, shape.HalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position,
                shape.Radius,
                halfExtents,
                shape.RotationRadians,
                shape.ShapeType,
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
                Radius = shape.Radius,
                RotationRadians = shape.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = shape.ShapeType,
                Render = RenderComponentFor(command.TypeId)
            };
        }

        private void DrainEvents()
        {
            DynamicBuffer<AoeHitElement> hitBuffer = entityManager.GetBuffer<AoeHitElement>(scopeEntity);
            DynamicBuffer<AoeRecycleElement> recycleBuffer = entityManager.GetBuffer<AoeRecycleElement>(scopeEntity);
            hitEvents += hitBuffer.Length;
            despawnedAoes += recycleBuffer.Length;

            for (int i = 0; i < hitBuffer.Length; i++)
            {
                AoeHitElement hit = hitBuffer[i];
                targetSync.TargetsById.TryGetValue(hit.TargetId, out IAoeTarget target);
                var damage = new DamageSnapshot(Mathf.Max(0f, hit.DamageAmount));
                var context = new AoeHitContext(
                    hit.AoeId,
                    hit.TypeId,
                    hit.TargetId,
                    new Vector2(hit.Position.x, hit.Position.y),
                    damage,
                    target);
                AoeHit?.Invoke(context);
                target?.ReceiveAoeHit(damage);
            }

            hitBuffer.Clear();
        }

        private AoeRenderComponent RenderComponentFor(int typeId)
        {
            if (!spawnVisuals || !renderResourcesByType.TryGetValue(typeId, out AoeRenderResources resources))
            {
                return default;
            }

            return new AoeRenderComponent
            {
                IsRenderable = 1,
                VisualScale = resources.VisualScale,
                VisualRotationSin = resources.VisualRotationSin,
                VisualRotationCos = resources.VisualRotationCos
            };
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
            scopeEntity = entityManager.CreateEntity(typeof(AoeScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<AoeSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<AoeHitElement>(scopeEntity);
            entityManager.AddBuffer<AoeRecycleElement>(scopeEntity);
            allAoeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>());
            submitQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<AoeRenderElement>());
            submitBuffer = new NativeArray<AoeRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
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

        private void SyncTargetsToEcs()
        {
            DynamicBuffer<CombatTargetElement> targetBuffer = entityManager.GetBuffer<CombatTargetElement>(scopeEntity);
            targetBuffer.Clear();

            IReadOnlyList<AoeTargetSnapshot> snapshots = targetSync.Snapshot();
            int count = Mathf.Min(snapshots.Count, maximumTargetCount);
            for (int i = 0; i < count; i++)
            {
                AoeTargetSnapshot snapshot = snapshots[i];
                float2 targetPosition = new(snapshot.Position.x, snapshot.Position.y);
                float targetRadius = snapshot.Shape.Radius;
                float2 targetHalfExtents = new(snapshot.Shape.HalfExtents.x, snapshot.Shape.HalfExtents.y);
                float targetRotationRadians = snapshot.Shape.RotationRadians;
                CombatShapeType targetShapeType = snapshot.Shape.ShapeType;
                CombatCollisionMath.ComputeWorldBounds(
                    targetPosition,
                    targetRadius,
                    targetHalfExtents,
                    targetRotationRadians,
                    targetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                targetBuffer.Add(new CombatTargetElement
                {
                    TargetId = snapshot.TargetId,
                    TargetMask = snapshot.TargetMask,
                    Position = targetPosition,
                    ShapeType = targetShapeType,
                    Radius = targetRadius,
                    HalfExtents = targetHalfExtents,
                    RotationRadians = targetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                });
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

        private void BuildRenderResources()
        {
            renderResourcesByType.Clear();
            if (!spawnVisuals || aoeTypes == null)
            {
                return;
            }

            for (int i = 0; i < aoeTypes.Length; i++)
            {
                AoeTypeDefinition definition = aoeTypes[i];
                if (definition == null || !typeRegistry.TryGetVisual(definition.TypeId, out AoeVisualDefinition visual))
                {
                    continue;
                }

                renderResourcesByType[definition.TypeId] = BuildRenderResourcesFor(
                    visual.Sprite,
                    visual.VisualScale,
                    visual.VisualRotationDegrees,
                    visual.Material);
            }
        }

        private AoeRenderResources BuildRenderResourcesFor(
            Sprite sprite,
            float scale,
            float visualRotationDegrees,
            Material sourceMaterial = null)
        {
            Mesh mesh = BuildAoeMesh(sprite);
            Texture texture = sprite.texture;
            Material material;
            if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial)
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = AoeRenderQueue
                };
                ConfigureAoeMaterial(material, texture);
            }
            else
            {
                material = new(FindAoeShader())
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = AoeRenderQueue
                };
                ConfigureAoeMaterial(material, texture);
            }

            if (material == null)
            {
                throw new MissingReferenceException($"AOE render material could not be created for sprite {sprite.name}.");
            }

            if (material.mainTexture == null)
            {
                throw new MissingReferenceException($"AOE render material for sprite {sprite.name} has no main texture assigned.");
            }

            if (!material.enableInstancing)
            {
                throw new global::System.InvalidOperationException($"AOE render material for sprite {sprite.name} does not support GPU instancing.");
            }

            if (material.shader == null || !material.shader.isSupported)
            {
                throw new MissingReferenceException($"AOE render material shader is not supported for sprite {sprite.name}.");
            }

            MaterialPropertyBlock properties = new();
            ConfigureAoeProperties(properties, material, texture);
            return new AoeRenderResources(mesh, material, properties, scale > 0f ? scale : 1f, visualRotationDegrees);
        }

        private static Mesh BuildAoeMesh(Sprite sprite)
        {
            Mesh mesh = new() { name = "AoeQuadMesh" };
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

        private static Shader FindAoeShader()
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

            throw new MissingReferenceException("No supported AOE render shader found.");
        }

        private static void ConfigureAoeMaterial(Material material, Texture texture)
        {
            material.enableInstancing = true;
            material.renderQueue = AoeRenderQueue;
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

        private static void ConfigureAoeProperties(MaterialPropertyBlock properties, Material material, Texture texture)
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

        private void SubmitAoes()
        {
            activeVisuals = 0;
            if (!spawnVisuals || renderResourcesByType.Count == 0 || submitQuery == null || !submitBuffer.IsCreated)
            {
                return;
            }

            entityManager.CompleteDependencyBeforeRO<AoeRenderElement>();
            DynamicBuffer<AoeRecycleElement> recycleBuffer = entityManager.GetBuffer<AoeRecycleElement>(scopeEntity);
            using NativeArray<Entity> entities =
                submitQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<AoeIdentityComponent> identities =
                submitQuery.ToComponentDataArray<AoeIdentityComponent>(Allocator.Temp);
            using NativeArray<AoeRenderElement> renderElements =
                submitQuery.ToComponentDataArray<AoeRenderElement>(Allocator.Temp);

            foreach (KeyValuePair<int, AoeRenderResources> pair in renderResourcesByType)
            {
                int typeId = pair.Key;
                int batchCount = 0;
                for (int i = 0; i < identities.Length; i++)
                {
                    if (identities[i].Scope != scopeEntity || identities[i].TypeId != typeId)
                    {
                        continue;
                    }

                    if (!entityManager.IsComponentEnabled<AoeActiveTag>(entities[i])
                        && !WasRecycledThisFrame(entities[i], recycleBuffer))
                    {
                        continue;
                    }

                    submitBuffer[batchCount] = renderElements[i];
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

        private static bool WasRecycledThisFrame(Entity entity, DynamicBuffer<AoeRecycleElement> recycleBuffer)
        {
            for (int i = 0; i < recycleBuffer.Length; i++)
            {
                if (recycleBuffer[i].AoeEntity == entity)
                {
                    return true;
                }
            }

            return false;
        }

        private void SubmitBatch(
            NativeArray<AoeRenderElement> instances,
            int startInstance,
            int instanceCount,
            AoeRenderResources resources)
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

        private void DestroyRenderResources()
        {
            foreach (KeyValuePair<int, AoeRenderResources> pair in renderResourcesByType)
            {
                pair.Value.Destroy();
            }

            renderResourcesByType.Clear();
        }

        private sealed class AoeRenderResources
        {
            public AoeRenderResources(
                Mesh mesh,
                Material material,
                MaterialPropertyBlock properties,
                float visualScale,
                float visualRotationDegrees)
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
    }
}
