using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Broadphase
{
#if ENABLE_PROFILER
    /// <summary>
    /// One worker-local aggregate emitted after a discrete projectile chunk finishes.
    /// No shared counter is touched while candidates are traversed or narrowed.
    /// </summary>
    public struct BvhQueryMetrics
    {
        public int QueryCount;
        public int NodesVisited;
        public int ChildCirclesTested;
        public int SurvivingLanes;
        public int LeafCandidates;
        public int ExactTests;

        public bool HasQueries => QueryCount != 0;
    }
#endif

    /// <summary>
    /// Scalar depth-first traversal workspace for one query circle.
    ///
    /// Created once per chunk invocation and reset per query; <see cref="Reset"/> only
    /// touches the query, the current lane mask and the stack's logical length, it never
    /// clears or copies the fixed stack's bytes. Candidates stream out through
    /// <see cref="TryMoveNext"/>, so a query that overlaps more objects than any fixed
    /// collection could hold still reports every one of them.
    ///
    /// Everything here is scalar and runs only after a node kernel has produced its packed
    /// lane mask: set-bit extraction, child metadata lookup, stack push, object yield.
    /// </summary>
    public struct BvhTraversalWorkspace
    {
        private BvhTree tree;
        private FixedList512Bytes<int> stack;
        private float2 queryCenter;
        private float queryRadius;
        private int currentNodeIndex;
        private int currentMask;
        private int maxStackDepth;
#if ENABLE_PROFILER
        private BvhQueryMetrics metrics;
#endif

        public static BvhTraversalWorkspace Create(in BvhTree source)
        {
            return new BvhTraversalWorkspace
            {
                tree = source,
                currentNodeIndex = -1,
                currentMask = 0,
                maxStackDepth = 0
            };
        }

        /// <summary>Node references the fixed traversal stack can hold.</summary>
        public static int StackCapacity => default(FixedList512Bytes<int>).Capacity;

        /// <summary>Deepest stack occupancy reached since this workspace was created.</summary>
        public int MaxStackDepth => maxStackDepth;

#if ENABLE_PROFILER
        /// <summary>Chunk-local totals accumulated across every reset/query.</summary>
        public BvhQueryMetrics Metrics => metrics;

        /// <summary>Records one scalar exact narrowphase attempt.</summary>
        public void RecordExactTest()
        {
            metrics.ExactTests++;
        }
#endif

        /// <summary>
        /// Reseeds the workspace for a new query circle. Logical state only: query, current
        /// mask, current node and the stack's length.
        /// </summary>
        public void Reset(float2 center, float radius)
        {
#if ENABLE_PROFILER
            metrics.QueryCount++;
#endif
            queryCenter = center;
            queryRadius = radius;
            currentNodeIndex = -1;
            currentMask = 0;
            stack.Clear();

            if (tree.RootNodeIndex < 0)
            {
                return;
            }

            stack.Add(tree.RootNodeIndex);
            maxStackDepth = math.max(maxStackDepth, stack.Length);
        }

        /// <summary>
        /// Yields the next candidate object index for the current query, or false when the
        /// traversal is exhausted. Candidates are broadphase candidates only: exact
        /// narrowphase still decides the hit.
        /// </summary>
        public bool TryMoveNext(out int objectIndex)
        {
            while (true)
            {
                while (currentMask != 0)
                {
                    int lane = math.tzcnt(currentMask);
                    currentMask &= currentMask - 1;

                    int metaIndex = BvhLayout.MetaIndex(currentNodeIndex, lane);
                    BvhChildKind kind = (BvhChildKind)tree.ChildKinds[metaIndex];
                    int reference = tree.ChildReferences[metaIndex];

                    if (kind == BvhChildKind.Object)
                    {
#if ENABLE_PROFILER
                        metrics.LeafCandidates++;
#endif
                        objectIndex = reference;
                        return true;
                    }

                    if (kind == BvhChildKind.Node)
                    {
                        stack.Add(reference);
                        maxStackDepth = math.max(maxStackDepth, stack.Length);
                    }
                }

                if (stack.Length == 0)
                {
                    objectIndex = -1;
                    return false;
                }

                int nodeIndex = stack[stack.Length - 1];
                stack.RemoveAtSwapBack(stack.Length - 1);

                int geometricMask = BvhNodeTest.OverlapMask(ref tree, nodeIndex, queryCenter, queryRadius);
#if ENABLE_PROFILER
                metrics.NodesVisited++;
                metrics.ChildCirclesTested += math.countbits(tree.NodeActiveMasks[nodeIndex]);
#endif
                if (geometricMask == 0)
                {
                    // Hot/cold split: a node that fails outright never touches its metadata.
                    continue;
                }

                currentNodeIndex = nodeIndex;
                currentMask = geometricMask & (int)tree.NodeActiveMasks[nodeIndex];
#if ENABLE_PROFILER
                metrics.SurvivingLanes += math.countbits((uint)currentMask);
#endif
            }
        }
    }
}
