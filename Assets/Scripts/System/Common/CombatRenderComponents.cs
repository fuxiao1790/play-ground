using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.U2D;

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

        // (uOffset, vOffset, uScale, vScale) into the shared combat atlas. Computed once by the
        // registry when the spawn command is built (CombatRoot.ProjectileCommandFor/AoeCommandFor)
        // and copied onto the entity like every other field here — never looked up per frame.
        public float4 UvRect;
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

    // ECS Lifecycle: common render component; per-entity render resource id copied from spawn commands; identifies which registered sprite kind this entity uses.
    public struct CombatRenderBatchId : IComponentData
    {
        public int Value;
    }

    // Per-kind metadata: the folded visual scale (authored scale * native sprite size in world
    // units, since the shared unit-quad mesh does not bake native pixel size into vertices) and
    // the UV rect of this kind's sprite within the manually-assembled atlas texture.
    public sealed class CombatRenderResourceEntry
    {
        public Vector2 VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
        public Vector4 UvRect;
    }

    // Owns the shared render resources for every registered combat sprite kind: one unit-quad
    // Mesh, one atlas Material, and per-kind UV rects computed from a single, manually-assembled
    // SpriteAtlas (assigned via CombatRoot's serialized field, not built at runtime). One shared
    // atlas so all kinds draw in as few Graphics.RenderMeshInstanced calls as possible instead of
    // one call per kind.
    public sealed class CombatRenderResourceRegistry : IComponentData
    {
        public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();
        public const float BoundsHalfExtent = 100000f;

        public Mesh SharedMesh { get; private set; }
        public Material SharedMaterial { get; private set; }
        public SpriteAtlas Atlas { get; private set; }
        public Texture2D AtlasTexture { get; private set; }
        public int Layer { get; private set; }

        private int _nextRenderId = 1;

        // Assigns the single, manually-assembled SpriteAtlas (one page; no runtime packing).
        // Must be called before any Register(...) call that passes a non-null sprite. Safe to call
        // multiple times (e.g. once per CombatRoot instance sharing the same registry); the last
        // call wins, matching how Layer is captured. The atlas's packed texture is not resolved
        // here (Atlas.GetSprite(...) may still return null if packing hasn't happened yet) but
        // lazily in Register(...), so packing order relative to CombatRoot.Awake() doesn't matter.
        public void ConfigureAtlas(SpriteAtlas atlas)
        {
            EnsureSharedResources();
            Atlas = atlas;
            AtlasTexture = null;
        }

        // Mints a render id and stores the folded visual scale plus this sprite's UV rect within
        // the configured atlas, on the calling (main) thread. Returns 0 for a null sprite (== "no
        // visual"). Throws if the atlas isn't configured, or if the sprite wasn't manually placed
        // in the configured atlas (skills must be assembled into the shared atlas in the editor
        // ahead of time; this is not a dynamic/auto-packing atlas). Membership is checked via
        // SpriteAtlas.GetSprite(name), the documented runtime lookup API (there is no
        // SpriteAtlas.GetTexture() — that was an earlier, incorrect assumption); it returns null
        // both when the atlas hasn't been packed yet and when the sprite simply isn't a packable,
        // so both cases collapse into one honest "not part of the atlas" error rather than a
        // distinction this API can't actually make.
        public int Register(Sprite sprite, Vector2 visualScale, float visualRotationDegrees, int layer)
        {
            EnsureSharedResources();
            Layer = layer;

            if (sprite == null) return 0;

            if (Atlas == null)
                throw new InvalidOperationException(
                    $"Combat sprite atlas is not configured; cannot register sprite '{sprite.name}'. Assign the Sprite Atlas on CombatRoot before registering skills.");

            Sprite packedSprite = Atlas.GetSprite(sprite.name);
            if (packedSprite == null)
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' is not part of the combat sprite atlas '{Atlas.name}' (GetSprite returned null — either it isn't a packable of this atlas, or the atlas hasn't been packed yet). Add it to the atlas's packables in the editor and repack.");

            Texture2D atlasTexture = packedSprite.texture;
            if (AtlasTexture != null && AtlasTexture != atlasTexture)
                throw new InvalidOperationException(
                    $"Sprite '{sprite.name}' resolved to a different packed texture than a previously registered sprite in atlas '{Atlas.name}'. The combat atlas must be a single page; this indicates the atlas has grown to multiple pages.");

            AtlasTexture = atlasTexture;
            SharedMaterial.mainTexture = atlasTexture;

            Vector4 uvRect = new(
                packedSprite.rect.x / atlasTexture.width,
                packedSprite.rect.y / atlasTexture.height,
                packedSprite.rect.width / atlasTexture.width,
                packedSprite.rect.height / atlasTexture.height);

            Vector2 nativeSize = new(packedSprite.rect.width / packedSprite.pixelsPerUnit, packedSprite.rect.height / packedSprite.pixelsPerUnit);
            Vector2 authoredScale = PositiveScale(visualScale);
            math.sincos(math.radians(visualRotationDegrees), out float sin, out float cos);

            int renderId = _nextRenderId++;
            Entries[renderId] = new CombatRenderResourceEntry
            {
                VisualScale = new Vector2(authoredScale.x * nativeSize.x, authoredScale.y * nativeSize.y),
                VisualRotationSin = sin,
                VisualRotationCos = cos,
                UvRect = uvRect
            };
            return renderId;
        }

        public CombatRenderComponent GetProjectileRenderComponent(int renderId, int projectileId)
        {
            if (!Entries.TryGetValue(renderId, out CombatRenderResourceEntry entry)) return default;
            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 1,
                VisualScale = new float2(entry.VisualScale.x, entry.VisualScale.y),
                VisualRotationSin = entry.VisualRotationSin,
                VisualRotationCos = entry.VisualRotationCos,
                RenderZ = CombatRoot.ProjectileRenderZ
                    - projectileId % CombatRoot.ProjectileRenderZSlots * CombatRoot.ProjectileRenderZStep,
                UvRect = new float4(entry.UvRect.x, entry.UvRect.y, entry.UvRect.z, entry.UvRect.w)
            };
        }

        // AOE visual data comes from geometry (spawn-time area/scale); the entry supplies the
        // sprite's own native-size scale, folded in since the shared unit-quad mesh does not
        // bake native pixel size into vertices (each kind used to bake it into its own mesh).
        public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
        {
            if (!Entries.TryGetValue(renderId, out CombatRenderResourceEntry entry)) return default;
            float2 scale = new float2(geometry.VisualScale.x, geometry.VisualScale.y)
                * new float2(entry.VisualScale.x, entry.VisualScale.y);
            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = scale,
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos,
                RenderZ = CombatRoot.AoeRenderZ,
                UvRect = new float4(entry.UvRect.x, entry.UvRect.y, entry.UvRect.z, entry.UvRect.w)
            };
        }

        // Destroys the shared GPU resources this registry created and clears the store. Called
        // from CombatRoot.OnDestroy. The SpriteAtlas and its packed texture are manually-assigned
        // project assets, not created by this registry, so they are dereferenced here but never
        // destroyed.
        public void Unregister()
        {
            if (SharedMaterial != null) UnityEngine.Object.Destroy(SharedMaterial);
            if (SharedMesh != null) UnityEngine.Object.Destroy(SharedMesh);
            SharedMaterial = null;
            SharedMesh = null;
            Atlas = null;
            AtlasTexture = null;

            Entries.Clear();
            _nextRenderId = 1;
        }

        private void EnsureSharedResources()
        {
            if (SharedMesh != null) return;

            SharedMesh = BuildUnitQuadMesh();

            Shader shader = Shader.Find("Combat/AtlasInstancedSprite");
            if (shader == null)
                throw new MissingReferenceException("Combat/AtlasInstancedSprite shader not found.");

            SharedMaterial = new Material(shader) { enableInstancing = true };
        }

        private static Mesh BuildUnitQuadMesh()
        {
            Mesh mesh = new() { name = "CombatAtlasUnitQuad" };

            var vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f)
            };
            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2 PositiveScale(Vector2 scale) =>
            new(scale.x > 0f ? scale.x : 1f, scale.y > 0f ? scale.y : 1f);
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
