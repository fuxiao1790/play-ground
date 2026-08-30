using System;
using PlayGround.System.Combat.Collision.Narrowphase;
using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Broadphase
{
    /// <summary>
    /// Bounding circle used for every BVH child lane, every object, and every query source.
    /// </summary>
    public struct BvhCircle
    {
        public float2 Center;
        public float Radius;

        public BvhCircle(float2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        /// <summary>
        /// Conservative broadphase circle for a combat collision shape. Reuses the
        /// narrowphase bounding-radius authority so a rectangle or capsule circle can
        /// never shrink below the exact shape and prune a real hit.
        /// </summary>
        public static BvhCircle FromShape(
            float2 position,
            float radius,
            float2 halfExtents,
            CombatShapeType shapeType)
        {
            return new BvhCircle(
                position,
                CombatCollisionMath.BoundingRadius(radius, halfExtents, shapeType));
        }
    }

    /// <summary>
    /// One object's Morton key paired with its index into the caller's object array.
    /// Sorting is a total order (key, then index) so the build stays deterministic even
    /// though the underlying sort is not stable.
    /// </summary>
    public struct BvhMortonEntry : IComparable<BvhMortonEntry>
    {
        public uint Key;
        public int ObjectIndex;

        public BvhMortonEntry(uint key, int objectIndex)
        {
            Key = key;
            ObjectIndex = objectIndex;
        }

        public int CompareTo(BvhMortonEntry other)
        {
            if (Key != other.Key)
            {
                return Key < other.Key ? -1 : 1;
            }

            return ObjectIndex.CompareTo(other.ObjectIndex);
        }
    }

    /// <summary>
    /// A built wide bounding-circle BVH. Leaves reference the caller's object indices
    /// directly; the tree stores no copy of object data.
    ///
    /// Native ownership: whoever calls <see cref="Create"/> owns the buffers and must call
    /// <see cref="Dispose"/>. Buffers grow geometrically during build and never shrink.
    /// </summary>
    public struct BvhTree : IDisposable
    {
        /// <summary>Hot bounds, BvhLayout.BoundsVectorsPerNode float4 vectors per node.</summary>
        public NativeList<float4> NodeBounds;

        /// <summary>Cold child references, BvhConfig.ChildCount per node; -1 on unused lanes.</summary>
        public NativeList<int> ChildReferences;

        /// <summary>Cold child kinds (<see cref="BvhChildKind"/>), BvhConfig.ChildCount per node.</summary>
        public NativeList<byte> ChildKinds;

        /// <summary>One active-lane bitmask per node; bit i set means lane i is a real child.</summary>
        public NativeList<uint> NodeActiveMasks;

        public int NodeCount;

        /// <summary>Index of the root node, or -1 when the tree holds no objects.</summary>
        public int RootNodeIndex;

        /// <summary>Number of node levels built (0 for an empty tree).</summary>
        public int Depth;

        public int ObjectCount;

        public static BvhTree Create(int initialNodeCapacity, Allocator allocator)
        {
            int nodeCapacity = math.max(1, initialNodeCapacity);
            return new BvhTree
            {
                NodeBounds = new NativeList<float4>(nodeCapacity * BvhLayout.BoundsVectorsPerNode, allocator),
                ChildReferences = new NativeList<int>(nodeCapacity * BvhConfig.ChildCount, allocator),
                ChildKinds = new NativeList<byte>(nodeCapacity * BvhConfig.ChildCount, allocator),
                NodeActiveMasks = new NativeList<uint>(nodeCapacity, allocator),
                NodeCount = 0,
                RootNodeIndex = -1,
                Depth = 0,
                ObjectCount = 0
            };
        }

        public bool IsCreated => NodeBounds.IsCreated;

        public bool IsEmpty => RootNodeIndex < 0;

        public uint ActiveMask(int nodeIndex) => NodeActiveMasks[nodeIndex];

        public BvhChildKind LaneKind(int nodeIndex, int lane) =>
            (BvhChildKind)ChildKinds[BvhLayout.MetaIndex(nodeIndex, lane)];

        public int LaneReference(int nodeIndex, int lane) =>
            ChildReferences[BvhLayout.MetaIndex(nodeIndex, lane)];

        public BvhCircle LaneCircle(int nodeIndex, int lane)
        {
            int block = lane >> 2;
            int component = lane & 3;
            float4 centerX = NodeBounds[BvhLayout.CenterXVector(nodeIndex, block)];
            float4 centerY = NodeBounds[BvhLayout.CenterYVector(nodeIndex, block)];
            float4 radius = NodeBounds[BvhLayout.RadiusVector(nodeIndex, block)];
            return new BvhCircle(
                new float2(centerX[component], centerY[component]),
                radius[component]);
        }

        public void Dispose()
        {
            if (NodeBounds.IsCreated)
            {
                NodeBounds.Dispose();
            }

            if (ChildReferences.IsCreated)
            {
                ChildReferences.Dispose();
            }

            if (ChildKinds.IsCreated)
            {
                ChildKinds.Dispose();
            }

            if (NodeActiveMasks.IsCreated)
            {
                NodeActiveMasks.Dispose();
            }
        }
    }

    /// <summary>
    /// Reusable build workspace. Holds the Morton ordering and the build-level records
    /// (one circle plus one child reference per level item, appended level after level).
    ///
    /// Native ownership: whoever calls <see cref="Create"/> owns the buffers and must call
    /// <see cref="Dispose"/>. It exists so a repeated full rebuild allocates nothing.
    /// </summary>
    public struct BvhBuildScratch : IDisposable
    {
        public NativeList<BvhMortonEntry> MortonEntries;

        /// <summary>Bounding circle of every build-level item, level 0 first.</summary>
        public NativeList<BvhCircle> LevelCircles;

        /// <summary>Object index (level 0) or node index (later levels) of every build-level item.</summary>
        public NativeList<int> LevelReferences;

        public static BvhBuildScratch Create(int initialObjectCapacity, Allocator allocator)
        {
            int objectCapacity = math.max(1, initialObjectCapacity);
            return new BvhBuildScratch
            {
                MortonEntries = new NativeList<BvhMortonEntry>(objectCapacity, allocator),
                LevelCircles = new NativeList<BvhCircle>(objectCapacity * 2, allocator),
                LevelReferences = new NativeList<int>(objectCapacity * 2, allocator)
            };
        }

        public bool IsCreated => MortonEntries.IsCreated;

        public void Dispose()
        {
            if (MortonEntries.IsCreated)
            {
                MortonEntries.Dispose();
            }

            if (LevelCircles.IsCreated)
            {
                LevelCircles.Dispose();
            }

            if (LevelReferences.IsCreated)
            {
                LevelReferences.Dispose();
            }
        }
    }
}
