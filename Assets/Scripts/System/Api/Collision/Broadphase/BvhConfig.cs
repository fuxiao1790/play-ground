namespace PlayGround.System.Combat.Collision.Broadphase
{
    /// <summary>
    /// Compile-time branching factor of the collision BVH. Only 4 and 8 are supported.
    /// This constant alone determines children per node, hot bound storage, child
    /// reference/kind storage, active lane mask width, build grouping size, traversal
    /// loop bounds and validation loop bounds. Change it here and recompile to benchmark
    /// BVH4 against BVH8; never hard-code 4 or 8 anywhere else in the tree code.
    /// </summary>
    public static class BvhConfig
    {
        public const int ChildCount = 4;
    }

    public enum BvhChildKind : byte
    {
        Unused = 0,
        Node = 1,
        Object = 2
    }

    /// <summary>
    /// Physical node layout derived from <see cref="BvhConfig.ChildCount"/>.
    ///
    /// One node's hot bounds occupy <see cref="BoundsVectorsPerNode"/> contiguous float4
    /// vectors inside a single flat buffer, ordered as all center X lanes, then all
    /// center Y lanes, then all radius lanes. That keeps each channel's ChildCount floats
    /// contiguous, so the BVH4 node test loads one packed 16-byte vector per channel and
    /// the BVH8 node test loads one packed 32-byte vector per channel, with no gathers.
    ///
    /// Cold metadata (child reference, child kind) is flattened separately and addressed
    /// as nodeIndex * ChildCount + lane so it is only touched after the geometric mask
    /// says a lane is worth reading.
    /// </summary>
    public static class BvhLayout
    {
        /// <summary>float4 vectors needed to hold one channel (X, Y or radius) of one node.</summary>
        public const int VectorsPerChannel = BvhConfig.ChildCount / 4;

        /// <summary>float4 vectors of hot bounds per node (X block(s), then Y block(s), then radius block(s)).</summary>
        public const int BoundsVectorsPerNode = 3 * VectorsPerChannel;

        /// <summary>Physical hot-bounds size of one node: 48 bytes for BVH4, 96 bytes for BVH8.</summary>
        public const int NodeBoundsSizeInBytes = BoundsVectorsPerNode * 16;

        /// <summary>Active-lane mask with every configured lane set.</summary>
        public const int FullActiveMask = (1 << BvhConfig.ChildCount) - 1;

        /// <summary>
        /// Contact margin added to the query radius before the packed compare, so
        /// floating-point error at exact contact can never turn into a false negative.
        /// Kept small so it barely widens the broadphase.
        /// </summary>
        public const float OverlapEpsilon = 1e-4f;

        /// <summary>Slack allowed by containment validation when comparing built parent circles.</summary>
        public const float ContainmentEpsilon = 1e-4f;

        public static int CenterXVector(int nodeIndex, int block) =>
            (nodeIndex * BoundsVectorsPerNode) + block;

        public static int CenterYVector(int nodeIndex, int block) =>
            (nodeIndex * BoundsVectorsPerNode) + VectorsPerChannel + block;

        public static int RadiusVector(int nodeIndex, int block) =>
            (nodeIndex * BoundsVectorsPerNode) + (2 * VectorsPerChannel) + block;

        public static int MetaIndex(int nodeIndex, int lane) =>
            (nodeIndex * BvhConfig.ChildCount) + lane;
    }
}
