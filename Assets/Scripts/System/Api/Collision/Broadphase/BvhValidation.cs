using System;
using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Broadphase
{
    /// <summary>
    /// Development-time validation for the collision BVH: configured width, node/root
    /// bookkeeping, active-lane masks, parent-circle containment, object coverage and
    /// traversal-stack sufficiency.
    ///
    /// These checks are expensive and are meant for tests and development builds, not for
    /// every simulation update.
    /// </summary>
    public static class BvhValidation
    {
        /// <summary>
        /// Not a constant on purpose: keeping it a property stops the compiler from folding
        /// the width guard away, so an unsupported ChildCount still fails at runtime.
        /// </summary>
        public static bool IsSupportedChildCount =>
            BvhConfig.ChildCount == 4 || BvhConfig.ChildCount == 8;

        /// <summary>
        /// Fails loudly for an unsupported compile-time width. Call this once from the
        /// owning system's creation path so a bad <see cref="BvhConfig.ChildCount"/> can
        /// never reach a query.
        /// </summary>
        public static void ValidateConfiguration()
        {
            if (IsSupportedChildCount)
            {
                return;
            }

            throw new InvalidOperationException(
                "BvhConfig.ChildCount must be 4 or 8. Configured value: " + BvhConfig.ChildCount + ".");
        }

        /// <summary>
        /// Worst-case traversal stack occupancy for a tree of <paramref name="depth"/> node
        /// levels: the root push plus one full sibling set per descended level.
        /// </summary>
        public static int RequiredTraversalStackDepth(int depth)
        {
            if (depth <= 0)
            {
                return 0;
            }

            return 1 + ((depth - 1) * (BvhConfig.ChildCount - 1));
        }

        public static bool ValidateTraversalStackCapacity(int depth, int stackCapacity, out FixedString512Bytes failure)
        {
            failure = default;
            int required = RequiredTraversalStackDepth(depth);
            if (required <= stackCapacity)
            {
                return true;
            }

            return Fail("traversal stack too small for built depth, required ", required, out failure);
        }

        /// <summary>True when <paramref name="outer"/> fully contains <paramref name="inner"/>.</summary>
        public static bool Contains(BvhCircle outer, BvhCircle inner, float epsilon)
        {
            return math.distance(outer.Center, inner.Center) + inner.Radius <= outer.Radius + epsilon;
        }

        /// <summary>
        /// Full structural and geometric validation of a built tree against the objects it
        /// was built from. Returns false and a description on the first violation.
        /// </summary>
        public static bool ValidateTree(
            ref BvhTree tree,
            NativeArray<BvhCircle> objects,
            int objectCount,
            float epsilon,
            out FixedString512Bytes failure)
        {
            failure = default;

            if (!IsSupportedChildCount)
            {
                return Fail("unsupported BvhConfig.ChildCount ", BvhConfig.ChildCount, out failure);
            }

            if (tree.ObjectCount != objectCount)
            {
                return Fail("tree object count mismatch ", tree.ObjectCount, out failure);
            }

            if (objectCount == 0)
            {
                if (tree.NodeCount != 0)
                {
                    return Fail("empty tree has nodes ", tree.NodeCount, out failure);
                }

                if (tree.RootNodeIndex != -1)
                {
                    return Fail("empty tree root is not -1 but ", tree.RootNodeIndex, out failure);
                }

                if (tree.Depth != 0)
                {
                    return Fail("empty tree depth is not 0 but ", tree.Depth, out failure);
                }

                return true;
            }

            if (tree.NodeCount != BvhBuilder.ComputeNodeCount(objectCount))
            {
                return Fail("unexpected node count ", tree.NodeCount, out failure);
            }

            if (tree.Depth != BvhBuilder.ComputeDepth(objectCount))
            {
                return Fail("unexpected depth ", tree.Depth, out failure);
            }

            if (tree.RootNodeIndex != tree.NodeCount - 1)
            {
                return Fail("root is not the last built node but ", tree.RootNodeIndex, out failure);
            }

            if (tree.NodeBounds.Length != tree.NodeCount * BvhLayout.BoundsVectorsPerNode)
            {
                return Fail("node bounds length does not match configured width ", tree.NodeBounds.Length, out failure);
            }

            if (tree.ChildReferences.Length != tree.NodeCount * BvhConfig.ChildCount
                || tree.ChildKinds.Length != tree.NodeCount * BvhConfig.ChildCount
                || tree.NodeActiveMasks.Length != tree.NodeCount)
            {
                return Fail("node metadata length does not match node count ", tree.NodeCount, out failure);
            }

            if (!ValidateTraversalStackCapacity(tree.Depth, BvhTraversalWorkspace.StackCapacity, out failure))
            {
                return false;
            }

            NativeArray<byte> objectSeen = new(objectCount, Allocator.Temp);
            NativeArray<byte> nodeSeen = new(tree.NodeCount, Allocator.Temp);
            bool valid = ValidateNodes(ref tree, objects, objectCount, epsilon, objectSeen, nodeSeen, out failure);
            if (valid)
            {
                valid = ValidateCoverage(ref tree, objectSeen, nodeSeen, out failure);
            }

            objectSeen.Dispose();
            nodeSeen.Dispose();
            return valid;
        }

        private static bool ValidateNodes(
            ref BvhTree tree,
            NativeArray<BvhCircle> objects,
            int objectCount,
            float epsilon,
            NativeArray<byte> objectSeen,
            NativeArray<byte> nodeSeen,
            out FixedString512Bytes failure)
        {
            failure = default;

            for (int nodeIndex = 0; nodeIndex < tree.NodeCount; nodeIndex++)
            {
                uint activeMask = tree.ActiveMask(nodeIndex);
                if (activeMask == 0 || activeMask > BvhLayout.FullActiveMask)
                {
                    return Fail("active mask out of range at node ", nodeIndex, out failure);
                }

                // Lanes always fill from lane 0 upwards, so the mask must be a low run.
                if ((activeMask & (activeMask + 1)) != 0)
                {
                    return Fail("active mask is not a contiguous low run at node ", nodeIndex, out failure);
                }

                for (int lane = 0; lane < BvhConfig.ChildCount; lane++)
                {
                    bool active = ((activeMask >> lane) & 1u) != 0u;
                    BvhChildKind kind = tree.LaneKind(nodeIndex, lane);
                    int reference = tree.LaneReference(nodeIndex, lane);

                    if (!active)
                    {
                        if (kind != BvhChildKind.Unused || reference != -1)
                        {
                            return Fail("inactive lane is not unused at node ", nodeIndex, out failure);
                        }

                        continue;
                    }

                    BvhCircle laneCircle = tree.LaneCircle(nodeIndex, lane);
                    if (laneCircle.Radius < 0f)
                    {
                        return Fail("negative lane radius at node ", nodeIndex, out failure);
                    }

                    if (kind == BvhChildKind.Object)
                    {
                        if (reference < 0 || reference >= objectCount)
                        {
                            return Fail("object reference out of range ", reference, out failure);
                        }

                        if (objectSeen[reference] != 0)
                        {
                            return Fail("object referenced more than once ", reference, out failure);
                        }

                        objectSeen[reference] = 1;
                        if (!Contains(laneCircle, objects[reference], epsilon))
                        {
                            failure = default;
                            failure.Append("parent node ");
                            failure.Append(nodeIndex);
                            failure.Append(" lane ");
                            failure.Append(lane);
                            failure.Append(" does not contain object reference ");
                            failure.Append(reference);
                            return false;
                        }

                        continue;
                    }

                    if (kind != BvhChildKind.Node)
                    {
                        return Fail("active lane has unused kind at node ", nodeIndex, out failure);
                    }

                    if (reference < 0 || reference >= nodeIndex)
                    {
                        return Fail("node reference is not a previously built node ", reference, out failure);
                    }

                    if (nodeSeen[reference] != 0)
                    {
                        return Fail("node referenced more than once ", reference, out failure);
                    }

                    nodeSeen[reference] = 1;
                    if (!ValidateChildNodeContainment(
                            ref tree,
                            laneCircle,
                            nodeIndex,
                            lane,
                            reference,
                            epsilon,
                            out failure))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidateChildNodeContainment(
            ref BvhTree tree,
            BvhCircle laneCircle,
            int parentNodeIndex,
            int parentLane,
            int childNodeIndex,
            float epsilon,
            out FixedString512Bytes failure)
        {
            failure = default;
            uint childMask = tree.ActiveMask(childNodeIndex);

            // Containment is transitive, so checking one level at a time proves the root
            // circle contains every circle in its subtree.
            for (int childLane = 0; childLane < BvhConfig.ChildCount; childLane++)
            {
                if (((childMask >> childLane) & 1u) == 0u)
                {
                    continue;
                }

                if (!Contains(laneCircle, tree.LaneCircle(childNodeIndex, childLane), epsilon))
                {
                    failure = default;
                    failure.Append("parent node ");
                    failure.Append(parentNodeIndex);
                    failure.Append(" lane ");
                    failure.Append(parentLane);
                    failure.Append(" does not contain child node ");
                    failure.Append(childNodeIndex);
                    failure.Append(" lane ");
                    failure.Append(childLane);
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateCoverage(
            ref BvhTree tree,
            NativeArray<byte> objectSeen,
            NativeArray<byte> nodeSeen,
            out FixedString512Bytes failure)
        {
            failure = default;

            for (int i = 0; i < objectSeen.Length; i++)
            {
                if (objectSeen[i] == 0)
                {
                    return Fail("object is not reachable from the tree ", i, out failure);
                }
            }

            for (int i = 0; i < nodeSeen.Length; i++)
            {
                byte expected = (byte)(i != tree.RootNodeIndex ? 1 : 0);
                if (nodeSeen[i] != expected)
                {
                    return Fail("node parent linkage is wrong at node ", i, out failure);
                }
            }

            return true;
        }

        private static bool Fail(FixedString128Bytes reason, int value, out FixedString512Bytes failure)
        {
            failure = default;
            failure.Append(reason);
            failure.Append(value);
            return false;
        }
    }
}
