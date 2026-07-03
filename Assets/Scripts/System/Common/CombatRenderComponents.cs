using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

        // Per-instance UV basis into the shared combat atlas, computed once by the registry when the
        // spawn command is built and copied onto the entity like every other field here (never looked
        // up per frame). An affine basis rather than a plain rect so it survives the atlas packer
        // rotating a sprite 90°: atlasUV = UvOriginU.xy + quadUV.x * UvOriginU.zw + quadUV.y * UvV.xy.
        public float4 UvOriginU; // xy = origin, zw = U axis (per unit quad-x)
        public float4 UvV;       // xy = V axis (per unit quad-y)
    }

    // ECS Lifecycle: common render component; owned by renderable domain entities; overwritten during render prep.
    public struct CombatRenderElement : IComponentData
    {
        public Matrix4x4 objectToWorld;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CombatInstanceData
    {
        public Matrix4x4 objectToWorld; // 64
        public Vector4 uvOriginU;       // 16: origin.xy, uAxis.xy
        public Vector4 uvV;             // 16: vAxis.xy, 0, 0
    }                                    // stride = 96

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
        public Vector4 UvOriginU;
        public Vector4 UvV;
    }

    // Owns the shared render resources for every registered combat sprite kind: one unit-quad
    // Mesh, one atlas Material, and per-kind UV rects computed from a single, manually-assembled
    // SpriteAtlas (assigned via CombatRoot's serialized field, not built at runtime). One shared
    // atlas so all kinds draw through one indirect submission instead of one call per kind.
    public sealed class CombatRenderResourceRegistry : IComponentData
    {
        public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();
        public const float BoundsHalfExtent = 100000f;

        public Mesh SharedMesh { get; private set; }
        public Material SharedMaterial { get; private set; }
        public SpriteAtlas Atlas { get; private set; }
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

            // Bind the atlas page as the material's texture. Every sprite in a single-page atlas
            // resolves to the same page, so binding per-registration is idempotent (last wins). We
            // deliberately do NOT assert reference-equality across sprites: that's a proxy for
            // "single page" that's trivially true when packed and falsely fails when the atlas is
            // momentarily unpacked (sprites resolve to their source textures). Single-page is an
            // atlas-authoring property (Pack Preview), enforced there, not re-checked here.
            SharedMaterial.mainTexture = packedSprite.texture;

            // The atlas packer may rotate a sprite 90° when packing (enableRotation), so a plain
            // axis-aligned UV rect can't reproduce its orientation. Build an affine UV basis from the
            // sprite's own vertex->UV mapping instead: identify the bottom-left/right/top corners by
            // local vertex position and read their atlas UVs. atlasUV = origin + qx*uAxis + qy*vAxis
            // then renders the sprite upright regardless of how the atlas packed it.
            ComputeUvBasis(packedSprite, out Vector2 uvOrigin, out Vector2 uAxis, out Vector2 vAxis);
            Vector4 uvOriginU = new(uvOrigin.x, uvOrigin.y, uAxis.x, uAxis.y);
            Vector4 uvV = new(vAxis.x, vAxis.y, 0f, 0f);

            Vector2 nativeSize = new(packedSprite.rect.width / packedSprite.pixelsPerUnit, packedSprite.rect.height / packedSprite.pixelsPerUnit);
            Vector2 authoredScale = PositiveScale(visualScale);
            math.sincos(math.radians(visualRotationDegrees), out float sin, out float cos);

            int renderId = _nextRenderId++;
            Entries[renderId] = new CombatRenderResourceEntry
            {
                VisualScale = new Vector2(authoredScale.x * nativeSize.x, authoredScale.y * nativeSize.y),
                VisualRotationSin = sin,
                VisualRotationCos = cos,
                UvOriginU = uvOriginU,
                UvV = uvV
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
                UvOriginU = new float4(entry.UvOriginU.x, entry.UvOriginU.y, entry.UvOriginU.z, entry.UvOriginU.w),
                UvV = new float4(entry.UvV.x, entry.UvV.y, entry.UvV.z, entry.UvV.w)
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
                UvOriginU = new float4(entry.UvOriginU.x, entry.UvOriginU.y, entry.UvOriginU.z, entry.UvOriginU.w),
                UvV = new float4(entry.UvV.x, entry.UvV.y, entry.UvV.z, entry.UvV.w)
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

            Entries.Clear();
            _nextRenderId = 1;
        }

        private void EnsureSharedResources()
        {
            if (SharedMesh != null) return;

            SharedMesh = BuildUnitQuadMesh();

            Shader shader = Shader.Find("Combat/AtlasIndirectSprite");
            if (shader == null)
                throw new MissingReferenceException("Combat/AtlasIndirectSprite shader not found.");

            SharedMaterial = new Material(shader);
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

        // Builds an affine atlas-UV basis from a sprite's vertex->UV mapping. Identifies the
        // bottom-left / bottom-right / top-left corners by local vertex position and reads their
        // real atlas UVs, so a sprite the packer rotated 90° still maps onto the unit quad upright.
        // Corner scores: BL minimizes x+y, BR maximizes x-y, TL maximizes y-x (exact for the 4-corner
        // Full Rect sprites this atlas uses; enableTightPacking is off).
        private static void ComputeUvBasis(Sprite sprite, out Vector2 origin, out Vector2 uAxis, out Vector2 vAxis)
        {
            Vector2[] verts = sprite.vertices;
            Vector2[] uvs = sprite.uv;

            int bl = 0, br = 0, tl = 0;
            float blScore = verts[0].x + verts[0].y;
            float brScore = verts[0].x - verts[0].y;
            float tlScore = verts[0].y - verts[0].x;
            for (int i = 1; i < verts.Length; i++)
            {
                float sBl = verts[i].x + verts[i].y;
                float sBr = verts[i].x - verts[i].y;
                float sTl = verts[i].y - verts[i].x;
                if (sBl < blScore) { blScore = sBl; bl = i; }
                if (sBr > brScore) { brScore = sBr; br = i; }
                if (sTl > tlScore) { tlScore = sTl; tl = i; }
            }

            origin = uvs[bl];
            uAxis = uvs[br] - uvs[bl];
            vAxis = uvs[tl] - uvs[bl];
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
