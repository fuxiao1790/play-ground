using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: common render component; owned by renderable domain entities; lifecycle is defined by each domain tag/scope.
    [StructLayout(LayoutKind.Sequential)]
    public struct CombatRenderComponent : IComponentData
    {
        public float4 Rotation; // m00, m01, m10, m11; written by render prep and uploaded directly.
        public float3 Position; // world x, world y, RenderZ.
        public int RenderMeta;  // bits 0..30 = render id; bit 31 = align-to-velocity flag.

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
            readonly get => (RenderMeta >> 31) & 1;
            set => RenderMeta = value != 0
                ? RenderMeta | unchecked((int)0x80000000)
                : RenderMeta & 0x7FFFFFFF;
        }

        public float RenderZ
        {
            readonly get => Position.z;
            set => Position.z = value;
        }

        public int RenderTypeId
        {
            readonly get => RenderMeta & 0x7FFFFFFF;
            set => RenderMeta = (RenderMeta & unchecked((int)0x80000000)) | (value & 0x7FFFFFFF);
        }

    }

    // ECS Lifecycle: common render authoring component; seeded by spawn commands and read by render prep; never toggled separately from render lifecycle.
    [StructLayout(LayoutKind.Sequential)]
    public struct CombatRenderAuthoring : IComponentData
    {
        public float2 BaseScale;
        public float BaseSin;
        public float BaseCos;

        public float2 VisualScale
        {
            readonly get => BaseScale;
            set => BaseScale = value;
        }

        public float VisualRotationSin
        {
            readonly get => BaseSin;
            set => BaseSin = value;
        }

        public float VisualRotationCos
        {
            readonly get => BaseSin == 0f && BaseCos == 0f ? 1f : BaseCos;
            set => BaseCos = value;
        }

        public void SetVisualTransform(
            float2 visualScale,
            float visualRotationSin,
            float visualRotationCos)
        {
            SetVisual2D(visualScale, visualRotationSin, visualRotationCos);
        }

        private void SetVisual2D(float2 scale, float sin, float cos)
        {
            BaseScale = scale;
            BaseSin = sin;
            BaseCos = cos;
        }
    }

    // ECS Lifecycle: common render component; per-entity render resource id copied from spawn commands; identifies which registered sprite kind this entity uses.
    public struct CombatRenderKindId : IComponentData
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

    [StructLayout(LayoutKind.Sequential)]
    public struct CombatUvBasis
    {
        public Vector4 OriginU;
        public Vector4 V;
    }

    public sealed class CombatRenderResourceRegistry : IComponentData
    {
        public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();
        public const float BoundsHalfExtent = 100000f;
        private const int InstanceDataStride = 32;
        private const int InitialMeshCapacity = 1024;
        private const int QuadVertexCount = 4;
        private const int QuadIndexCount = 6;
        private const int UvBasisStride = 32;
        private static readonly int InstanceDataProperty = Shader.PropertyToID("_InstanceData");
        private static readonly int UvBasisProperty = Shader.PropertyToID("_UvBasis");

        public Mesh SharedMesh { get; private set; }
        public Material SharedMaterial { get; private set; }
        public SpriteAtlas Atlas { get; private set; }
        public int Layer { get; private set; }
        public int MeshCapacity => _meshCapacity;

        private int _nextRenderId = 1;
        private GraphicsBuffer _uvBasisBuffer;
        private GraphicsBuffer _fallbackInstanceBuffer;
        private GraphicsBuffer _fallbackUvBasisBuffer;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private int _meshCapacity;
        private bool _uvDirty = true;

        public void ConfigureAtlas(SpriteAtlas atlas)
        {
            EnsureSharedResources();
            Atlas = atlas;
        }

        // The MeshFilter/MeshRenderer/Material are owned by the scene/prefab, not this
        // registry. Sorting Layer / Order in Layer and the Material (shader
        // Combat/AtlasIndirectSprite) are configured directly on that Renderer's
        // Inspector; this only reads the already-assigned material and pushes the
        // code-built capacity mesh onto the MeshFilter.
        public void AttachRenderer(MeshFilter meshFilter, MeshRenderer meshRenderer)
        {
            EnsureSharedResources();
            _meshFilter = meshFilter;
            _meshRenderer = meshRenderer;
            _meshFilter.sharedMesh = SharedMesh;

            if (SharedMaterial == null)
            {
                if (meshRenderer.sharedMaterial == null)
                    throw new MissingReferenceException(
                        $"'{meshRenderer.name}' has no Material assigned; assign a Material using the "
                        + "Combat/AtlasIndirectSprite shader in its Inspector before combat sprites can render.");

                SharedMaterial = meshRenderer.sharedMaterial;
                BindFallbackBuffers();
            }
            else
            {
                meshRenderer.sharedMaterial = SharedMaterial;
            }
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
            _uvDirty = true;
            return renderId;
        }

        public GraphicsBuffer EnsureUvBasisBuffer()
        {
            if (!_uvDirty && _uvBasisBuffer != null)
            {
                return _uvBasisBuffer;
            }

            int count = _nextRenderId;
            if (_uvBasisBuffer == null || _uvBasisBuffer.count < count)
            {
                _uvBasisBuffer?.Dispose();
                _uvBasisBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, UvBasisStride);
            }

            CombatUvBasis[] data = new CombatUvBasis[count];
            foreach (KeyValuePair<int, CombatRenderResourceEntry> pair in Entries)
            {
                int renderId = pair.Key;
                if ((uint)renderId >= (uint)count) continue;

                CombatRenderResourceEntry entry = pair.Value;
                data[renderId] = new CombatUvBasis
                {
                    OriginU = entry.UvOriginU,
                    V = entry.UvV
                };
            }

            _uvBasisBuffer.SetData(data);
            _uvDirty = false;
            return _uvBasisBuffer;
        }

        public CombatRenderComponent GetProjectileRenderComponent(
            int renderId,
            int projectileId,
            out CombatRenderAuthoring authoring)
        {
            authoring = default;
            if (!Entries.TryGetValue(renderId, out CombatRenderResourceEntry entry)) return default;

            authoring.SetVisualTransform(
                new float2(entry.VisualScale.x, entry.VisualScale.y),
                entry.VisualRotationSin,
                entry.VisualRotationCos);

            var component = new CombatRenderComponent
            {
                RenderZ = CombatRoot.ProjectileRenderZ
                    - projectileId % CombatRoot.ProjectileRenderZSlots * CombatRoot.ProjectileRenderZStep,
                AlignToVelocity = 1,
                RenderTypeId = renderId
            };
            return component;
        }

        public CombatRenderComponent GetAoeRenderComponent(
            int renderId,
            AoeSpawnGeometry geometry,
            out CombatRenderAuthoring authoring)
        {
            authoring = default;
            if (!Entries.TryGetValue(renderId, out CombatRenderResourceEntry entry)) return default;

            float2 scale = new float2(geometry.VisualScale.x, geometry.VisualScale.y)
                * new float2(entry.VisualScale.x, entry.VisualScale.y);
            authoring.SetVisualTransform(
                scale,
                geometry.VisualRotationSin,
                geometry.VisualRotationCos);

            var component = new CombatRenderComponent
            {
                RenderZ = CombatRoot.AoeRenderZ,
                AlignToVelocity = 0,
                RenderTypeId = renderId
            };
            return component;
        }

        public void Unregister()
        {
            // The MeshFilter/MeshRenderer are scene/prefab-owned and never destroyed here.
            // The Material is also scene/prefab-owned (Inspector-assigned on the
            // MeshRenderer) as of the material-authoring fix — only the code-built mesh
            // is this registry's to destroy.
            if (_meshFilter != null) _meshFilter.sharedMesh = null;
            if (SharedMesh != null) UnityEngine.Object.Destroy(SharedMesh);
            _uvBasisBuffer?.Dispose();
            _fallbackInstanceBuffer?.Dispose();
            _fallbackUvBasisBuffer?.Dispose();
            _meshFilter = null;
            _meshRenderer = null;
            SharedMaterial = null;
            SharedMesh = null;
            _uvBasisBuffer = null;
            _fallbackInstanceBuffer = null;
            _fallbackUvBasisBuffer = null;
            Atlas = null;
            _meshCapacity = 0;

            Entries.Clear();
            _nextRenderId = 1;
            _uvDirty = true;
        }

        public void EnsureMeshCapacity(int requiredCount)
        {
            EnsureSharedResources();
            if (requiredCount <= _meshCapacity) return;

            int newCapacity = math.max(
                requiredCount,
                _meshCapacity == 0 ? InitialMeshCapacity : _meshCapacity * 2);
            while (newCapacity < requiredCount)
                newCapacity *= 2;

            ReplaceSharedMesh(BuildCapacityMesh(newCapacity), newCapacity);
        }

        public void SetActiveInstanceCount(int activeCount)
        {
            if (SharedMesh == null) return;

            EnsureMeshCapacity(activeCount);
            int indexCount = math.max(0, activeCount) * QuadIndexCount;
            SharedMesh.SetSubMesh(
                0,
                new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds);
        }

        // Only the capacity mesh is built here — its size genuinely grows at runtime.
        // The Material is authored in the Inspector on the attached MeshRenderer and
        // picked up by AttachRenderer; it is never constructed here.
        private void EnsureSharedResources()
        {
            if (SharedMesh != null) return;

            ReplaceSharedMesh(BuildCapacityMesh(InitialMeshCapacity), InitialMeshCapacity);
            if (_meshFilter != null) _meshFilter.sharedMesh = SharedMesh;
        }

        private void BindFallbackBuffers()
        {
            _fallbackInstanceBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                InstanceDataStride);
            _fallbackInstanceBuffer.SetData(new CombatRenderComponent[1]);

            _fallbackUvBasisBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                UvBasisStride);
            _fallbackUvBasisBuffer.SetData(new CombatUvBasis[1]);

            SharedMaterial.SetBuffer(InstanceDataProperty, _fallbackInstanceBuffer);
            SharedMaterial.SetBuffer(UvBasisProperty, _fallbackUvBasisBuffer);
        }

        private void ReplaceSharedMesh(Mesh mesh, int capacity)
        {
            Mesh oldMesh = SharedMesh;
            SharedMesh = mesh;
            _meshCapacity = capacity;
            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = SharedMesh;
            }

            if (oldMesh != null)
            {
                UnityEngine.Object.Destroy(oldMesh);
            }
        }

        private static Mesh BuildCapacityMesh(int capacity)
        {
            Mesh mesh = new()
            {
                name = "CombatAtlasCapacityMesh",
                indexFormat = IndexFormat.UInt32
            };

            Vector3[] corners =
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f)
            };
            Vector2[] uvCorners =
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            int[] indexPattern = { 0, 1, 2, 0, 2, 3 };

            Vector3[] vertices = new Vector3[capacity * QuadVertexCount];
            Vector2[] uvs = new Vector2[capacity * QuadVertexCount];
            Vector2[] slotIndices = new Vector2[capacity * QuadVertexCount];
            int[] indices = new int[capacity * QuadIndexCount];

            for (int slot = 0; slot < capacity; slot++)
            {
                int vertexStart = slot * QuadVertexCount;
                for (int corner = 0; corner < QuadVertexCount; corner++)
                {
                    vertices[vertexStart + corner] = corners[corner];
                    uvs[vertexStart + corner] = uvCorners[corner];
                    slotIndices[vertexStart + corner] = new Vector2(slot, 0f);
                }

                int indexStart = slot * QuadIndexCount;
                for (int index = 0; index < QuadIndexCount; index++)
                {
                    indices[indexStart + index] = vertexStart + indexPattern[index];
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, slotIndices);
            mesh.SetTriangles(indices, 0, false);
            mesh.SetSubMesh(
                0,
                new SubMeshDescriptor(0, 0, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (BoundsHalfExtent * 2f));
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

        public static CombatRenderComponent ElementFor(
            CombatKinematicsComponent kinematics,
            CombatRenderAuthoring authoring,
            CombatRenderComponent render)
        {
            if (render.IsRenderable == 0)
            {
                return DegenerateInstance(render);
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

            float cos = directionX * authoring.VisualRotationCos - directionY * authoring.VisualRotationSin;
            float sin = directionX * authoring.VisualRotationSin + directionY * authoring.VisualRotationCos;
            float2 scale = authoring.VisualScale;

            render.Rotation = new float4(
                cos * scale.x,
                -sin * scale.y,
                sin * scale.x,
                cos * scale.y);
            render.Position = new float3(kinematics.Position.x, kinematics.Position.y, render.Position.z);
            return render;
        }

        public static CombatRenderComponent DegenerateInstance(CombatRenderComponent render)
        {
            render.Rotation = default;
            return render;
        }
    }
}
