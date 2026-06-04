using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace PlayGround.System.Common
{
    public static class BatchedSpriteRenderer
    {
        public static CombatSpriteRenderResources BuildResources(
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

        public static void SubmitBatch<T>(
            NativeArray<T> instances,
            int startInstance,
            int instanceCount,
            CombatSpriteRenderResources resources,
            int layer,
            float boundsHalfExtent)
            where T : unmanaged
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
                    layer = layer,
                    worldBounds = new Bounds(
                        Vector3.zero,
                        new Vector3(boundsHalfExtent, boundsHalfExtent, boundsHalfExtent) * 2f)
                },
                resources.Mesh,
                0,
                instances,
                instanceCount,
                startInstance);
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

        private static void ValidateResources(Sprite sprite, Material material)
        {
            if (material == null)
            {
                throw new MissingReferenceException($"Batched sprite render material could not be created for sprite {sprite.name}.");
            }

            if (material.mainTexture == null)
            {
                throw new MissingReferenceException($"Batched sprite render material for sprite {sprite.name} has no main texture assigned.");
            }

            if (!material.enableInstancing)
            {
                throw new global::System.InvalidOperationException($"Batched sprite render material for sprite {sprite.name} does not support GPU instancing.");
            }

            if (material.shader == null || !material.shader.isSupported)
            {
                throw new MissingReferenceException($"Batched sprite render material shader is not supported for sprite {sprite.name}.");
            }
        }

        private static Vector2 PositiveScale(Vector2 scale)
        {
            return new Vector2(
                scale.x > 0f ? scale.x : 1f,
                scale.y > 0f ? scale.y : 1f);
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
            {
                Object.Destroy(Material);
            }

            if (Mesh != null)
            {
                Object.Destroy(Mesh);
            }
        }
    }
}
