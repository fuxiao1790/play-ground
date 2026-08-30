using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Broadphase
{
    /// <summary>
    /// Width-selected SIMD node overlap kernels.
    ///
    /// One call tests all <see cref="BvhConfig.ChildCount"/> children of a single node
    /// against one query circle and returns a lane bitmask. The complete overlap equation
    /// (subtract, multiply, add, compare) runs packed and ends in exactly one packed mask
    /// extraction. No lane is ever inspected, compared or branched on before the mask
    /// exists; that is what the scalar traversal above these kernels consumes.
    /// </summary>
    public static class BvhNodeTest
    {
        /// <summary>
        /// Geometric overlap mask for one node's children. The returned mask is NOT yet
        /// combined with the node's active-lane mask; the caller does
        /// <c>mask &amp;= activeMask</c> before touching any child metadata.
        /// </summary>
        public static int OverlapMask(ref BvhTree tree, int nodeIndex, float2 queryCenter, float queryRadius)
        {
            // Compile-time width selection. BvhConfig.ChildCount is a constant, so exactly
            // one of these calls survives compilation; the other is intentionally dead code.
#pragma warning disable 162
            if (BvhConfig.ChildCount == 8)
            {
                return WideNodeMask(ref tree, nodeIndex, queryCenter, queryRadius);
            }

            return NarrowNodeMask(ref tree, nodeIndex, queryCenter, queryRadius);
#pragma warning restore 162
        }

        /// <summary>
        /// BVH4 kernel. Four children, one packed float4 equation, one packed mask
        /// extraction through math.bitmask.
        /// </summary>
        public static int Bvh4OverlapMask(
            float4 childCenterX,
            float4 childCenterY,
            float4 childRadius,
            float2 queryCenter,
            float queryRadius)
        {
            float4 deltaX = childCenterX - queryCenter.x;
            float4 deltaY = childCenterY - queryCenter.y;
            float4 combinedRadius = childRadius + (queryRadius + BvhLayout.OverlapEpsilon);
            float4 distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            float4 radiusSquared = combinedRadius * combinedRadius;
            return math.bitmask(distanceSquared <= radiusSquared);
        }

        /// <summary>
        /// BVH8 compiler-selected kernel. Two complete packed float4 equations each produce
        /// one four-bit mask, then combine into one eight-bit mask. Source contains no x86
        /// intrinsics or ISA branch: Burst chooses supported SIMD instructions for target CPU.
        /// There is no eight-lane scalar loop.
        /// </summary>
        public static int Bvh8OverlapMask(
            float4 childCenterXLow,
            float4 childCenterXHigh,
            float4 childCenterYLow,
            float4 childCenterYHigh,
            float4 childRadiusLow,
            float4 childRadiusHigh,
            float2 queryCenter,
            float queryRadius)
        {
            int lowMask = Bvh4OverlapMask(childCenterXLow, childCenterYLow, childRadiusLow, queryCenter, queryRadius);
            int highMask = Bvh4OverlapMask(childCenterXHigh, childCenterYHigh, childRadiusHigh, queryCenter, queryRadius);
            return lowMask | (highMask << 4);
        }

        private static int NarrowNodeMask(ref BvhTree tree, int nodeIndex, float2 queryCenter, float queryRadius)
        {
            return Bvh4OverlapMask(
                tree.NodeBounds[BvhLayout.CenterXVector(nodeIndex, 0)],
                tree.NodeBounds[BvhLayout.CenterYVector(nodeIndex, 0)],
                tree.NodeBounds[BvhLayout.RadiusVector(nodeIndex, 0)],
                queryCenter,
                queryRadius);
        }

        private static int WideNodeMask(ref BvhTree tree, int nodeIndex, float2 queryCenter, float queryRadius)
        {
            float4 childCenterXLow = tree.NodeBounds[BvhLayout.CenterXVector(nodeIndex, 0)];
            float4 childCenterXHigh = tree.NodeBounds[BvhLayout.CenterXVector(nodeIndex, 1)];
            float4 childCenterYLow = tree.NodeBounds[BvhLayout.CenterYVector(nodeIndex, 0)];
            float4 childCenterYHigh = tree.NodeBounds[BvhLayout.CenterYVector(nodeIndex, 1)];
            float4 childRadiusLow = tree.NodeBounds[BvhLayout.RadiusVector(nodeIndex, 0)];
            float4 childRadiusHigh = tree.NodeBounds[BvhLayout.RadiusVector(nodeIndex, 1)];

            return Bvh8OverlapMask(
                childCenterXLow,
                childCenterXHigh,
                childCenterYLow,
                childCenterYHigh,
                childRadiusLow,
                childRadiusHigh,
                queryCenter,
                queryRadius);
        }

    }
}
