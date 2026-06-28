using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: common render component; owned by renderable domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatRenderComponent : IComponentData
    {
        public int IsRenderable;
        public int AlignToVelocity;
        public float2 VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
        public float RenderZ;
    }

    // ECS Lifecycle: common render component; owned by renderable domain entities; overwritten during render prep.
    public struct CombatRenderElement : IComponentData
    {
        public Matrix4x4 objectToWorld;
    }

    // ECS Lifecycle: common render enable tag; owned by renderable domain entities; enabled/disabled with the owning domain active tag.
    public struct CombatRenderActiveTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: shared render component; added at entity creation; kept until owning domain root teardown; partitions render chunks by globally unique batch id without structural archetype cost.
    public struct CombatRenderBatchId : ISharedComponentData, global::System.IEquatable<CombatRenderBatchId>
    {
        public int Value;
        public readonly bool Equals(CombatRenderBatchId other) => Value == other.Value;
        public override int GetHashCode() => Value;
    }

    public sealed class CombatRenderResourceEntry
    {
        public CombatSpriteRenderResources Resources;
        public int Layer;
        public float BoundsHalfExtent;
    }

    public sealed class CombatRenderResourceRegistry : IComponentData
    {
        public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();

        private int _nextRenderId = 1;

        public const string ProjectileMeshName = "ProjectileQuadMesh";
        public const string AoeMeshName = "AoeQuadMesh";

        private const float BoundsHalfExtent = 100000f;

        // Mints a render id, builds GPU resources on the calling (main) thread, and publishes
        // into Entries. Returns 0 for a null sprite (== "no visual").
        public int Register(
            Sprite sprite,
            Vector2 visualScale,
            float visualRotationDegrees,
            Material sourceMaterial,
            string meshName,
            int layer)
        {
            if (sprite == null) return 0;

            CombatSpriteRenderResources resources = BuildResources(
                sprite, visualScale, visualRotationDegrees, sourceMaterial, meshName);
            int renderId = _nextRenderId++;
            Entries[renderId] = new CombatRenderResourceEntry
            {
                Resources = resources,
                Layer = layer,
                BoundsHalfExtent = BoundsHalfExtent
            };
            return renderId;
        }

        public CombatRenderComponent GetProjectileRenderComponent(int renderId, int projectileId)
        {
            if (!Entries.TryGetValue(renderId, out var entry)) return default;
            CombatSpriteRenderResources res = entry.Resources;
            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 1,
                VisualScale = new float2(res.VisualScale.x, res.VisualScale.y),
                VisualRotationSin = res.VisualRotationSin,
                VisualRotationCos = res.VisualRotationCos,
                RenderZ = CombatRoot.ProjectileRenderZ
                    - projectileId % CombatRoot.ProjectileRenderZSlots * CombatRoot.ProjectileRenderZStep
            };
        }

        // AOE visual data comes from geometry; the entry only gates whether a visual exists.
        public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
        {
            if (!Entries.ContainsKey(renderId)) return default;
            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new float2(geometry.VisualScale.x, geometry.VisualScale.y),
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos,
                RenderZ = CombatRoot.AoeRenderZ
            };
        }

        // Destroys all GPU resources and clears the store. Called from CombatRoot.OnDestroy.
        public void Unregister()
        {
            foreach (var entry in Entries.Values)
                entry.Resources.Destroy();
            Entries.Clear();
            _nextRenderId = 1;
        }

        private static CombatSpriteRenderResources BuildResources(
            Sprite sprite,
            Vector2 visualScale,
            float visualRotationDegrees,
            Material sourceMaterial,
            string meshName)
        {
            Mesh mesh = BuildSpriteMesh(sprite, meshName);
            Texture texture = sprite.texture;
            int renderQueue = sourceMaterial != null && sourceMaterial.renderQueue >= 0
                ? sourceMaterial.renderQueue
                : (int)RenderQueue.Transparent;
            Material material;
            if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial)
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = renderQueue
                };
            }
            else
            {
                material = new Material(FindSpriteShader())
                {
                    mainTexture = texture,
                    enableInstancing = true,
                    renderQueue = renderQueue
                };
            }

            ConfigureMaterial(material, texture, renderQueue);
            ValidateResources(sprite, material);

            MaterialPropertyBlock properties = new();
            ConfigureProperties(properties, material, texture);
            return new CombatSpriteRenderResources(mesh, material, properties, PositiveScale(visualScale), visualRotationDegrees);
        }

        private static Mesh BuildSpriteMesh(Sprite sprite, string meshName)
        {
            Mesh mesh = new() { name = meshName };
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

        private static Shader FindSpriteShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) return shader;

            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null) return shader;

            shader = Shader.Find("Sprites/Default");
            if (shader != null) return shader;

            throw new MissingReferenceException("No supported batched sprite render shader found.");
        }

        private static void ConfigureMaterial(Material material, Texture texture, int renderQueue)
        {
            material.enableInstancing = true;
            material.renderQueue = renderQueue;
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

        private static void ConfigureProperties(MaterialPropertyBlock properties, Material material, Texture texture)
        {
            properties.SetTexture("_MainTex", texture);

            if (material.HasProperty("_BaseMap"))
                properties.SetTexture("_BaseMap", texture);

            if (material.HasProperty("_Color"))
                properties.SetColor("_Color", Color.white);

            if (material.HasProperty("_BaseColor"))
                properties.SetColor("_BaseColor", Color.white);

            if (material.HasProperty("_RendererColor"))
                properties.SetColor("_RendererColor", Color.white);
        }

        private static void ValidateResources(Sprite sprite, Material material)
        {
            if (material == null)
                throw new MissingReferenceException($"Batched sprite render material could not be created for sprite {sprite.name}.");

            if (material.mainTexture == null)
                throw new MissingReferenceException($"Batched sprite render material for sprite {sprite.name} has no main texture assigned.");

            if (!material.enableInstancing)
                throw new InvalidOperationException($"Batched sprite render material for sprite {sprite.name} does not support GPU instancing.");

            if (material.shader == null || !material.shader.isSupported)
                throw new MissingReferenceException($"Batched sprite render material shader is not supported for sprite {sprite.name}.");
        }

        private static Vector2 PositiveScale(Vector2 scale) =>
            new Vector2(scale.x > 0f ? scale.x : 1f, scale.y > 0f ? scale.y : 1f);

        private static void SetTextureIfPresent(Material material, string propertyName, Texture texture)
        {
            if (material.HasProperty(propertyName))
                material.SetTexture(propertyName, texture);
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, color);
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }
    }

    public sealed class CombatSpriteRenderResources
    {
        public CombatSpriteRenderResources(
            Mesh mesh,
            Material material,
            MaterialPropertyBlock properties,
            Vector2 visualScale,
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
        public Vector2 VisualScale { get; }
        public float VisualRotationSin { get; }
        public float VisualRotationCos { get; }

        public void Destroy()
        {
            if (Material != null)
                Object.Destroy(Material);

            if (Mesh != null)
                Object.Destroy(Mesh);
        }
    }

    public static class CombatRenderMatrixUtility
    {
        private const float MinimumDirectionLengthSquared = 0.000001f;

        public static CombatRenderElement ElementFor(
            CombatKinematicsComponent kinematics,
            CombatRenderComponent render)
        {
            if (render.IsRenderable == 0)
            {
                return default;
            }

            float directionX = 1f;
            float directionY = 0f;
            if (render.AlignToVelocity != 0)
            {
                float velocityLengthSquared = math.lengthsq(kinematics.Velocity);
                if (velocityLengthSquared > MinimumDirectionLengthSquared)
                {
                    float inverseLength = math.rsqrt(velocityLengthSquared);
                    directionX = kinematics.Velocity.x * inverseLength;
                    directionY = kinematics.Velocity.y * inverseLength;
                }
            }

            float cos = directionX * render.VisualRotationCos - directionY * render.VisualRotationSin;
            float sin = directionX * render.VisualRotationSin + directionY * render.VisualRotationCos;
            float2 scale = render.VisualScale;

            return new CombatRenderElement
            {
                objectToWorld = new Matrix4x4
                {
                    m00 = cos * scale.x,
                    m01 = -sin * scale.y,
                    m02 = 0f,
                    m03 = kinematics.Position.x,
                    m10 = sin * scale.x,
                    m11 = cos * scale.y,
                    m12 = 0f,
                    m13 = kinematics.Position.y,
                    m20 = 0f,
                    m21 = 0f,
                    m22 = math.max(scale.x, scale.y),
                    m23 = render.RenderZ,
                    m30 = 0f,
                    m31 = 0f,
                    m32 = 0f,
                    m33 = 1f
                }
            };
        }
    }
}
