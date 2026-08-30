using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Broadphase
{
    /// <summary>
    /// Deterministic full rebuild of a wide bounding-circle BVH.
    ///
    /// Objects are normalized into the build bounds, quantized, Morton interleaved and
    /// sorted by (key, object index). The sorted sequence is then grouped bottom-up into
    /// nodes of at most <see cref="BvhConfig.ChildCount"/> lanes; each new node appends its
    /// own parent circle as an item of the next level, and grouping repeats until one node
    /// remains. That last node is the root, so the root is always an internal node whenever
    /// there is at least one object.
    /// </summary>
    public static class BvhBuilder
    {
        private const int MortonBitsPerAxis = 16;
        private const float MortonMaxCoordinate = (1 << MortonBitsPerAxis) - 1;

        /// <summary>
        /// Rebuilds <paramref name="tree"/> from scratch. Leaf lanes reference
        /// <paramref name="objects"/> by index; no object data is copied into the tree.
        /// </summary>
        public static void Build(
            ref BvhTree tree,
            ref BvhBuildScratch scratch,
            NativeArray<BvhCircle> objects,
            int objectCount)
        {
            tree.NodeBounds.Clear();
            tree.ChildReferences.Clear();
            tree.ChildKinds.Clear();
            tree.NodeActiveMasks.Clear();
            scratch.MortonEntries.Clear();
            scratch.LevelCircles.Clear();
            scratch.LevelReferences.Clear();

            tree.NodeCount = 0;
            tree.RootNodeIndex = -1;
            tree.Depth = 0;
            tree.ObjectCount = math.max(0, objectCount);

            if (tree.ObjectCount == 0)
            {
                return;
            }

            int nodeCount = ComputeNodeCount(tree.ObjectCount);
            AllocateNodes(ref tree, nodeCount);
            AllocateLevels(ref scratch, tree.ObjectCount, nodeCount);
            BuildMortonOrder(ref scratch, objects, tree.ObjectCount);
            GroupBottomUp(ref tree, ref scratch, tree.ObjectCount);
        }

        /// <summary>
        /// Exact number of nodes a tree of <paramref name="objectCount"/> objects produces.
        /// </summary>
        public static int ComputeNodeCount(int objectCount)
        {
            if (objectCount <= 0)
            {
                return 0;
            }

            int total = 0;
            int level = objectCount;
            do
            {
                level = (level + BvhConfig.ChildCount - 1) / BvhConfig.ChildCount;
                total += level;
            }
            while (level > 1);

            return total;
        }

        /// <summary>Number of node levels a tree of <paramref name="objectCount"/> objects produces.</summary>
        public static int ComputeDepth(int objectCount)
        {
            if (objectCount <= 0)
            {
                return 0;
            }

            int depth = 0;
            int level = objectCount;
            do
            {
                level = (level + BvhConfig.ChildCount - 1) / BvhConfig.ChildCount;
                depth++;
            }
            while (level > 1);

            return depth;
        }

        /// <summary>
        /// Conservative circle containing every circle in <paramref name="circles"/> over
        /// [first, first + count): AABB center plus the largest distance-to-center plus
        /// child radius. Cheap and robust, not a minimum enclosing circle.
        /// </summary>
        public static BvhCircle BuildParentCircle(NativeList<BvhCircle> circles, int first, int count)
        {
            BvhCircle head = circles[first];
            float2 min = head.Center - head.Radius;
            float2 max = head.Center + head.Radius;

            for (int lane = 1; lane < count; lane++)
            {
                BvhCircle circle = circles[first + lane];
                min = math.min(min, circle.Center - circle.Radius);
                max = math.max(max, circle.Center + circle.Radius);
            }

            float2 center = (min + max) * 0.5f;
            float radius = 0f;
            for (int lane = 0; lane < count; lane++)
            {
                BvhCircle circle = circles[first + lane];
                radius = math.max(radius, math.distance(center, circle.Center) + circle.Radius);
            }

            return new BvhCircle(center, radius);
        }

        private static void AllocateNodes(ref BvhTree tree, int nodeCount)
        {
            EnsureCapacity(ref tree.NodeBounds, nodeCount * BvhLayout.BoundsVectorsPerNode);
            EnsureCapacity(ref tree.ChildReferences, nodeCount * BvhConfig.ChildCount);
            EnsureCapacity(ref tree.ChildKinds, nodeCount * BvhConfig.ChildCount);
            EnsureCapacity(ref tree.NodeActiveMasks, nodeCount);

            // Every lane of every node created below is written explicitly, including the
            // unused ones, so uninitialized resize can never leak a previous build's bounds.
            tree.NodeBounds.ResizeUninitialized(nodeCount * BvhLayout.BoundsVectorsPerNode);
            tree.ChildReferences.ResizeUninitialized(nodeCount * BvhConfig.ChildCount);
            tree.ChildKinds.ResizeUninitialized(nodeCount * BvhConfig.ChildCount);
            tree.NodeActiveMasks.ResizeUninitialized(nodeCount);
        }

        private static void AllocateLevels(ref BvhBuildScratch scratch, int objectCount, int nodeCount)
        {
            EnsureCapacity(ref scratch.MortonEntries, objectCount);
            EnsureCapacity(ref scratch.LevelCircles, objectCount + nodeCount);
            EnsureCapacity(ref scratch.LevelReferences, objectCount + nodeCount);
        }

        private static void BuildMortonOrder(
            ref BvhBuildScratch scratch,
            NativeArray<BvhCircle> objects,
            int objectCount)
        {
            float2 min = objects[0].Center;
            float2 max = min;
            for (int i = 1; i < objectCount; i++)
            {
                float2 center = objects[i].Center;
                min = math.min(min, center);
                max = math.max(max, center);
            }

            // A zero-extent axis (all objects share a coordinate) collapses to key 0 on that
            // axis; the object-index tiebreaker keeps the resulting order deterministic.
            float2 extent = max - min;
            float2 scale = math.select(MortonMaxCoordinate / extent, float2.zero, extent <= 0f);

            scratch.MortonEntries.ResizeUninitialized(objectCount);
            for (int i = 0; i < objectCount; i++)
            {
                float2 normalized = math.clamp((objects[i].Center - min) * scale, 0f, MortonMaxCoordinate);
                uint key = Morton2D((uint)normalized.x, (uint)normalized.y);
                scratch.MortonEntries[i] = new BvhMortonEntry(key, i);
            }

            scratch.MortonEntries.Sort();

            for (int i = 0; i < objectCount; i++)
            {
                int objectIndex = scratch.MortonEntries[i].ObjectIndex;
                scratch.LevelCircles.Add(objects[objectIndex]);
                scratch.LevelReferences.Add(objectIndex);
            }
        }

        private static void GroupBottomUp(ref BvhTree tree, ref BvhBuildScratch scratch, int objectCount)
        {
            int levelStart = 0;
            int levelCount = objectCount;
            int levelIndex = 0;

            do
            {
                BvhChildKind kind = levelIndex == 0 ? BvhChildKind.Object : BvhChildKind.Node;
                int groupCount = (levelCount + BvhConfig.ChildCount - 1) / BvhConfig.ChildCount;
                int nextStart = scratch.LevelCircles.Length;
                int levelEnd = levelStart + levelCount;

                for (int group = 0; group < groupCount; group++)
                {
                    int first = levelStart + (group * BvhConfig.ChildCount);
                    int laneCount = math.min(BvhConfig.ChildCount, levelEnd - first);
                    int nodeIndex = tree.NodeCount;
                    tree.NodeCount++;

                    WriteNode(ref tree, ref scratch, nodeIndex, first, laneCount, kind);
                    scratch.LevelCircles.Add(BuildParentCircle(scratch.LevelCircles, first, laneCount));
                    scratch.LevelReferences.Add(nodeIndex);
                }

                levelStart = nextStart;
                levelCount = groupCount;
                levelIndex++;
                tree.Depth++;
            }
            while (levelCount > 1);

            tree.RootNodeIndex = tree.NodeCount - 1;
        }

        private static void WriteNode(
            ref BvhTree tree,
            ref BvhBuildScratch scratch,
            int nodeIndex,
            int first,
            int laneCount,
            BvhChildKind kind)
        {
            for (int block = 0; block < BvhLayout.VectorsPerChannel; block++)
            {
                float4 centerX = float4.zero;
                float4 centerY = float4.zero;
                float4 radius = float4.zero;

                for (int component = 0; component < 4; component++)
                {
                    int lane = (block * 4) + component;
                    if (lane >= laneCount)
                    {
                        continue;
                    }

                    BvhCircle circle = scratch.LevelCircles[first + lane];
                    centerX[component] = circle.Center.x;
                    centerY[component] = circle.Center.y;
                    radius[component] = circle.Radius;
                }

                tree.NodeBounds[BvhLayout.CenterXVector(nodeIndex, block)] = centerX;
                tree.NodeBounds[BvhLayout.CenterYVector(nodeIndex, block)] = centerY;
                tree.NodeBounds[BvhLayout.RadiusVector(nodeIndex, block)] = radius;
            }

            for (int lane = 0; lane < BvhConfig.ChildCount; lane++)
            {
                int metaIndex = BvhLayout.MetaIndex(nodeIndex, lane);
                bool active = lane < laneCount;
                tree.ChildReferences[metaIndex] = active ? scratch.LevelReferences[first + lane] : -1;
                tree.ChildKinds[metaIndex] = (byte)(active ? kind : BvhChildKind.Unused);
            }

            tree.NodeActiveMasks[nodeIndex] = (uint)((1 << laneCount) - 1);
        }

        private static uint Morton2D(uint x, uint y) => Part1By1(x) | (Part1By1(y) << 1);

        private static uint Part1By1(uint value)
        {
            value &= 0x0000ffffu;
            value = (value ^ (value << 8)) & 0x00ff00ffu;
            value = (value ^ (value << 4)) & 0x0f0f0f0fu;
            value = (value ^ (value << 2)) & 0x33333333u;
            value = (value ^ (value << 1)) & 0x55555555u;
            return value;
        }

        private static void EnsureCapacity<T>(ref NativeList<T> list, int required)
            where T : unmanaged
        {
            if (list.Capacity >= required)
            {
                return;
            }

            list.Capacity = math.max(required, list.Capacity * 2);
        }
    }
}
