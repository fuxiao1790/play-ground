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
    [StructLayout(LayoutKind.Sequential)]
    public struct CombatRenderComponent : IComponentData
    {
        public Matrix4x4 objectToWorld; // 64 bytes; written by render prep and uploaded directly.
        public Vector4 uvOriginU;       // 16 bytes: xy = origin, zw = U axis.
        public Vector4 uvV;             // 16 bytes: xy = V axis, z = render id, w = align-to-velocity flag.

        public int IsRenderable
        {
            readonly get => RenderTypeId != 0 ? 1 : 0;
            set
            {
                if (value == 0)
                {
                    RenderTypeId = 0;
                }
                else if (RenderTypeId == 0)
                {
                    RenderTypeId = 1;
                }
            }
        }

        public int AlignToVelocity
        {
            readonly get => uvV.w != 0f ? 1 : 0;
            set => uvV.w = value != 0 ? 1f : 0f;
        }

        public float2 VisualScale
        {
            readonly get => new(
                math.sqrt(objectToWorld.m00 * objectToWorld.m00 + objectToWorld.m10 * objectToWorld.m10),
                math.sqrt(objectToWorld.m01 * objectToWorld.m01 + objectToWorld.m11 * objectToWorld.m11));
            set => SetVisual2D(value, VisualRotationSin, VisualRotationCos);
        }

        public float VisualRotationSin
        {
            readonly get => objectToWorld.m02;
            set => SetVisual2D(VisualScale, value, VisualRotationCos);
        }

        public float VisualRotationCos
        {
            readonly get => objectToWorld.m02 == 0f && objectToWorld.m12 == 0f ? 1f : objectToWorld.m12;
            set => SetVisual2D(VisualScale, VisualRotationSin, value);
        }

        public float RenderZ
        {
            readonly get => objectToWorld.m23;
            set => objectToWorld.m23 = value;
        }

        public float4 UvOriginU
        {
            readonly get => new(uvOriginU.x, uvOriginU.y, uvOriginU.z, uvOriginU.w);
            set => uvOriginU = new Vector4(value.x, value.y, value.z, value.w);
        }

        public float4 UvV
        {
            readonly get => new(uvV.x, uvV.y, uvV.z, uvV.w);
            set => uvV = new Vector4(
                value.x,
                value.y,
                value.z != 0f ? value.z : uvV.z,
                value.w != 0f ? value.w : uvV.w);
        }

        public int RenderTypeId
        {
            readonly get => (int)uvV.z;
            set => uvV.z = value;
        }

        public void SetVisualTransform(
            float2 visualScale,
            float visualRotationSin,
            float visualRotationCos,
            float renderZ,
            int alignToVelocity,
            int renderTypeId)
        {
            SetVisual2D(visualScale, visualRotationSin, visualRotationCos);
            objectToWorld.m23 = renderZ;
            objectToWorld.m33 = 1f;
            AlignToVelocity = alignToVelocity;
            RenderTypeId = renderTypeId;
        }

        private void SetVisual2D(float2 scale, float sin, float cos)
        {
            objectToWorld.m00 = cos * scale.x;
            objectToWorld.m01 = -sin * scale.y;
            objectToWorld.m02 = sin;
            objectToWorld.m10 = sin * scale.x;
            objectToWorld.m11 = cos * scale.y;
            objectToWorld.m12 = cos;
            objectToWorld.m22 = math.max(scale.x, scale.y);
            objectToWorld.m33 = 1f;
        }
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

    public sealed class CombatRenderResourceEntry
    {
        public Vector2 VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
        public Vector4 UvOriginU;
        public Vector4 UvV;
    }

    public sealed class CombatRenderResourceRegistry : IComponentData
    {
        public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();
        public const float BoundsHalfExtent = 100000f;

        public Mesh SharedMesh { get; private set; }
        public Material SharedMaterial { get; private set; }
        public SpriteAtlas Atlas { get; private set; }
        public int Layer { get; private set; }

        private int _nextRenderId = 1;

        public void ConfigureAtlas(SpriteAtlas atlas)
        {
            EnsureSharedResources();
            Atlas = atlas;
        }

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
                    $"Sprite '{sprite.name}' is not part of the combat sprite atlas '{Atlas.name}' (GetSprite returned null). Add it to the atlas's packables in the editor and repack.");

            SharedMaterial.mainTexture = packedSprite.texture;

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
            var component = new CombatRenderComponent
            {
                UvOriginU = new float4(entry.UvOriginU.x, entry.UvOriginU.y, entry.UvOriginU.z, entry.UvOriginU.w),
                UvV = new float4(entry.UvV.x, entry.UvV.y, entry.UvV.z, entry.UvV.w)
            };
            component.SetVisualTransform(
                new float2(entry.VisualScale.x, entry.VisualScale.y),
                entry.VisualRotationSin,
                entry.VisualRotationCos,
                CombatRoot.ProjectileRenderZ
                    - projectileId % CombatRoot.ProjectileRenderZSlots * CombatRoot.ProjectileRenderZStep,
                1,
                renderId);
            return component;
        }

        public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
        {
            if (!Entries.TryGetValue(renderId, out CombatRenderResourceEntry entry)) return default;
            float2 scale = new float2(geometry.VisualScale.x, geometry.VisualScale.y)
                * new float2(entry.VisualScale.x, entry.VisualScale.y);
            var component = new CombatRenderComponent
            {
                UvOriginU = new float4(entry.UvOriginU.x, entry.UvOriginU.y, entry.UvOriginU.z, entry.UvOriginU.w),
                UvV = new float4(entry.UvV.x, entry.UvV.y, entry.UvV.z, entry.UvV.w)
            };
            component.SetVisualTransform(
                scale,
                geometry.VisualRotationSin,
                geometry.VisualRotationCos,
                CombatRoot.AoeRenderZ,
                0,
                renderId);
            return component;
        }

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

        public static Matrix4x4 ElementFor(
            CombatKinematicsComponent kinematics,
            CombatRenderComponent render)
        {
            if (render.IsRenderable == 0)
            {
                return DegenerateMatrix(render);
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

            return new Matrix4x4
            {
                m00 = cos * scale.x,
                m01 = -sin * scale.y,
                m02 = render.VisualRotationSin,
                m03 = kinematics.Position.x,
                m10 = sin * scale.x,
                m11 = cos * scale.y,
                m12 = render.VisualRotationCos,
                m13 = kinematics.Position.y,
                m20 = 0f,
                m21 = 0f,
                m22 = math.max(scale.x, scale.y),
                m23 = render.RenderZ,
                m30 = 0f,
                m31 = 0f,
                m32 = 0f,
                m33 = 1f
            };
        }

        public static Matrix4x4 DegenerateMatrix(CombatRenderComponent render)
        {
            Matrix4x4 matrix = render.objectToWorld;
            matrix.m00 = 0f;
            matrix.m01 = 0f;
            matrix.m10 = 0f;
            matrix.m11 = 0f;
            matrix.m20 = 0f;
            matrix.m21 = 0f;
            matrix.m22 = 0f;
            matrix.m33 = 1f;
            return matrix;
        }
    }
}
