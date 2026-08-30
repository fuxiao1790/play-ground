using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using Unity.Collections;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace PlayGround.Tests.EditMode
{
    public sealed class BvhBroadphaseEditModeTests
    {
        private BvhTree tree;
        private BvhBuildScratch scratch;
        private NativeArray<BvhCircle> objects;

        [SetUp]
        public void SetUp()
        {
            tree = BvhTree.Create(16, Allocator.Persistent);
            scratch = BvhBuildScratch.Create(64, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            if (objects.IsCreated)
            {
                objects.Dispose();
            }

            scratch.Dispose();
            tree.Dispose();
        }

        [Test]
        public void Configuration_ChildCountDrivesSupportedPhysicalNodeLayout()
        {
            Assert.That(BvhValidation.IsSupportedChildCount, Is.True);
            Assert.That(BvhConfig.ChildCount, Is.EqualTo(4).Or.EqualTo(8));
            Assert.DoesNotThrow(BvhValidation.ValidateConfiguration);

            Assert.That(BvhLayout.VectorsPerChannel, Is.EqualTo(BvhConfig.ChildCount / 4));
            Assert.That(BvhLayout.BoundsVectorsPerNode, Is.EqualTo(3 * (BvhConfig.ChildCount / 4)));
            Assert.That(BvhLayout.NodeBoundsSizeInBytes, Is.EqualTo(BvhConfig.ChildCount == 8 ? 96 : 48));
            Assert.That(BvhLayout.FullActiveMask, Is.EqualTo((1 << BvhConfig.ChildCount) - 1));

            NativeArray<BvhCircle> source = AllocateObjects(BvhConfig.ChildCount * 3);
            FillGrid(source, source.Length, 1.5f, 0.25f);
            Build(source, source.Length);

            Assert.That(tree.NodeBounds.Length, Is.EqualTo(tree.NodeCount * BvhLayout.BoundsVectorsPerNode));
            Assert.That(tree.ChildReferences.Length, Is.EqualTo(tree.NodeCount * BvhConfig.ChildCount));
            Assert.That(tree.ChildKinds.Length, Is.EqualTo(tree.NodeCount * BvhConfig.ChildCount));
        }

        [Test]
        public void Build_EmptySceneProducesEmptyTreeAndNoCandidates()
        {
            NativeArray<BvhCircle> source = AllocateObjects(0);
            Build(source, 0);

            Assert.That(tree.IsEmpty, Is.True);
            Assert.That(tree.RootNodeIndex, Is.EqualTo(-1));
            Assert.That(tree.NodeCount, Is.Zero);
            Assert.That(tree.Depth, Is.Zero);
            Assert.That(Query(float2.zero, 1000f), Is.Empty);
            AssertValid(source, 0);
        }

        [Test]
        public void Build_SingleObjectProducesRootNodeWithOneActiveLane()
        {
            NativeArray<BvhCircle> source = AllocateObjects(1);
            source[0] = new BvhCircle(new float2(3f, -2f), 0.5f);
            Build(source, 1);

            Assert.That(tree.NodeCount, Is.EqualTo(1));
            Assert.That(tree.RootNodeIndex, Is.EqualTo(0));
            Assert.That(tree.Depth, Is.EqualTo(1));
            Assert.That(tree.ActiveMask(0), Is.EqualTo(1u));
            Assert.That(tree.LaneKind(0, 0), Is.EqualTo(BvhChildKind.Object));
            Assert.That(tree.LaneReference(0, 0), Is.Zero);
            AssertUnusedLanes(0, 1);
            AssertValid(source, 1);

            Assert.That(Query(new float2(3f, -2f), 0.1f), Is.EquivalentTo(new[] { 0 }));
            Assert.That(Query(new float2(500f, 500f), 1f), Is.Empty);
        }

        [Test]
        public void Build_PartialRootMasksUnusedLanesAndMarksThemUnused()
        {
            int count = BvhConfig.ChildCount - 1;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillGrid(source, count, 2f, 0.3f);
            Build(source, count);

            Assert.That(tree.NodeCount, Is.EqualTo(1));
            Assert.That(tree.Depth, Is.EqualTo(1));
            Assert.That(tree.ActiveMask(0), Is.EqualTo((uint)((1 << count) - 1)));
            AssertUnusedLanes(0, count);
            AssertValid(source, count);
        }

        [Test]
        public void Build_ExactMultipleFillsEveryLaneAndOneMoreAddsALevel()
        {
            int count = BvhConfig.ChildCount;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillGrid(source, count, 2f, 0.3f);
            Build(source, count);

            Assert.That(tree.NodeCount, Is.EqualTo(1));
            Assert.That(tree.Depth, Is.EqualTo(1));
            Assert.That(tree.ActiveMask(0), Is.EqualTo((uint)BvhLayout.FullActiveMask));
            AssertValid(source, count);

            int wider = BvhConfig.ChildCount + 1;
            NativeArray<BvhCircle> widerSource = AllocateObjects(wider);
            FillGrid(widerSource, wider, 2f, 0.3f);
            Build(widerSource, wider);

            Assert.That(tree.Depth, Is.EqualTo(2));
            Assert.That(tree.NodeCount, Is.EqualTo(3));
            Assert.That(tree.RootNodeIndex, Is.EqualTo(2));
            Assert.That(tree.LaneKind(tree.RootNodeIndex, 0), Is.EqualTo(BvhChildKind.Node));
            AssertValid(widerSource, wider);
        }

        [Test]
        public void Build_MultiLevelSceneValidatesContainmentAndMasks()
        {
            const int count = 500;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillGrid(source, count, 1.25f, 0.4f);
            Build(source, count);

            Assert.That(tree.Depth, Is.GreaterThan(2));
            Assert.That(tree.NodeCount, Is.EqualTo(BvhBuilder.ComputeNodeCount(count)));
            AssertValid(source, count);
        }

        [Test]
        public void Build_IsDeterministicAcrossRepeatedBuilds()
        {
            const int count = 257;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 4242u, 30f, 0.1f, 1.5f);

            Build(source, count);
            int nodeCount = tree.NodeCount;
            int rootNodeIndex = tree.RootNodeIndex;
            int depth = tree.Depth;
            float4[] bounds = tree.NodeBounds.AsArray().ToArray();
            int[] references = tree.ChildReferences.AsArray().ToArray();
            byte[] kinds = tree.ChildKinds.AsArray().ToArray();
            uint[] masks = tree.NodeActiveMasks.AsArray().ToArray();

            Build(source, count);

            Assert.That(tree.NodeCount, Is.EqualTo(nodeCount));
            Assert.That(tree.RootNodeIndex, Is.EqualTo(rootNodeIndex));
            Assert.That(tree.Depth, Is.EqualTo(depth));
            Assert.That(tree.NodeBounds.AsArray().ToArray(), Is.EqualTo(bounds));
            Assert.That(tree.ChildReferences.AsArray().ToArray(), Is.EqualTo(references));
            Assert.That(tree.ChildKinds.AsArray().ToArray(), Is.EqualTo(kinds));
            Assert.That(tree.NodeActiveMasks.AsArray().ToArray(), Is.EqualTo(masks));
        }

        [Test]
        public void Build_EqualPositionsAndZeroExtentSceneBuildsDeterministically()
        {
            const int count = 40;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            for (int i = 0; i < count; i++)
            {
                source[i] = new BvhCircle(new float2(-7.5f, 12.25f), 0f);
            }

            Build(source, count);
            int[] firstReferences = tree.ChildReferences.AsArray().ToArray();
            AssertValid(source, count);

            Build(source, count);
            Assert.That(tree.ChildReferences.AsArray().ToArray(), Is.EqualTo(firstReferences));

            List<int> candidates = Query(new float2(-7.5f, 12.25f), 0.01f);
            Assert.That(candidates.Count, Is.EqualTo(count));
        }

        [Test]
        public void Build_NegativeCoordinateSceneValidatesAndReturnsOverlaps()
        {
            const int count = 120;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 99u, 25f, 0.2f, 1f);
            for (int i = 0; i < count; i++)
            {
                BvhCircle circle = source[i];
                source[i] = new BvhCircle(circle.Center - new float2(400f, 250f), circle.Radius);
            }

            Build(source, count);
            AssertValid(source, count);
            AssertNoFalseNegatives(source, count, new float2(-400f, -250f), 6f);
        }

        [Test]
        public void Validation_ObjectContainmentFailureNamesParentLaneAndReference()
        {
            NativeArray<BvhCircle> source = AllocateObjects(1);
            source[0] = new BvhCircle(new float2(2f, -3f), 0.5f);
            Build(source, 1);

            float4 centerX = tree.NodeBounds[BvhLayout.CenterXVector(0, 0)];
            centerX[0] += 1000f;
            tree.NodeBounds[BvhLayout.CenterXVector(0, 0)] = centerX;

            bool valid = BvhValidation.ValidateTree(
                ref tree, source, 1, BvhLayout.ContainmentEpsilon, out FixedString512Bytes failure);

            Assert.That(valid, Is.False);
            Assert.That(
                failure.ToString(),
                Is.EqualTo("parent node 0 lane 0 does not contain object reference 0"));
        }

        [Test]
        public void Validation_NodeContainmentFailureNamesParentLaneChildReferenceAndLane()
        {
            int count = BvhConfig.ChildCount + 1;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillGrid(source, count, 2f, 0.25f);
            Build(source, count);

            int parentNode = tree.RootNodeIndex;
            const int parentLane = 0;
            int childNode = tree.LaneReference(parentNode, parentLane);
            float4 centerX = tree.NodeBounds[BvhLayout.CenterXVector(parentNode, 0)];
            centerX[parentLane] += 1000f;
            tree.NodeBounds[BvhLayout.CenterXVector(parentNode, 0)] = centerX;

            bool valid = BvhValidation.ValidateTree(
                ref tree, source, count, BvhLayout.ContainmentEpsilon, out FixedString512Bytes failure);

            Assert.That(valid, Is.False);
            Assert.That(
                failure.ToString(),
                Is.EqualTo(
                    "parent node " + parentNode
                    + " lane 0 does not contain child node " + childNode
                    + " lane 0"));
        }

        [Test]
        public void Bvh4Kernel_MatchesScalarReferenceMask()
        {
            float4 centerX = new(0f, 5f, -3f, 20f);
            float4 centerY = new(0f, 0.5f, -4f, -20f);
            float4 radius = new(1f, 0.25f, 2f, 0.5f);
            float2 queryCenter = new(0.75f, 0.25f);
            const float queryRadius = 1.5f;

            int mask = BvhNodeTest.Bvh4OverlapMask(centerX, centerY, radius, queryCenter, queryRadius);
            int expected = 0;
            for (int lane = 0; lane < 4; lane++)
            {
                if (ScalarOverlap(centerX[lane], centerY[lane], radius[lane], queryCenter, queryRadius))
                {
                    expected |= 1 << lane;
                }
            }

            Assert.That(mask, Is.EqualTo(expected));
            Assert.That(mask & ~0xF, Is.Zero);
        }

        [Test]
        public void Bvh8CompilerSelectedKernel_MatchesScalarReferenceMask()
        {
            Random random = new(20250829u);

            for (int iteration = 0; iteration < 200; iteration++)
            {
                float4 centerXLow = random.NextFloat4(-20f, 20f);
                float4 centerXHigh = random.NextFloat4(-20f, 20f);
                float4 centerYLow = random.NextFloat4(-20f, 20f);
                float4 centerYHigh = random.NextFloat4(-20f, 20f);
                float4 radiusLow = random.NextFloat4(0f, 4f);
                float4 radiusHigh = random.NextFloat4(0f, 4f);
                float2 queryCenter = random.NextFloat2(-20f, 20f);
                float queryRadius = random.NextFloat(0f, 4f);

                int expected = 0;
                for (int lane = 0; lane < 4; lane++)
                {
                    if (ScalarOverlap(centerXLow[lane], centerYLow[lane], radiusLow[lane], queryCenter, queryRadius))
                    {
                        expected |= 1 << lane;
                    }

                    if (ScalarOverlap(centerXHigh[lane], centerYHigh[lane], radiusHigh[lane], queryCenter, queryRadius))
                    {
                        expected |= 1 << (lane + 4);
                    }
                }

                int mask = BvhNodeTest.Bvh8OverlapMask(
                    centerXLow, centerXHigh, centerYLow, centerYHigh, radiusLow, radiusHigh, queryCenter, queryRadius);

                Assert.That(mask, Is.EqualTo(expected), "mask mismatch at iteration " + iteration);
                Assert.That(mask & ~0xFF, Is.Zero);
            }
        }

        [Test]
        public void NodeOverlapMask_MatchesScalarReferenceAndMasksUnusedLanes()
        {
            const int count = 130;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 771u, 20f, 0.1f, 1f);
            Build(source, count);

            Random random = new(4004u);
            for (int iteration = 0; iteration < 64; iteration++)
            {
                float2 queryCenter = random.NextFloat2(-25f, 25f);
                float queryRadius = random.NextFloat(0f, 3f);

                for (int nodeIndex = 0; nodeIndex < tree.NodeCount; nodeIndex++)
                {
                    int expected = 0;
                    for (int lane = 0; lane < BvhConfig.ChildCount; lane++)
                    {
                        BvhCircle circle = tree.LaneCircle(nodeIndex, lane);
                        if (ScalarOverlap(circle.Center.x, circle.Center.y, circle.Radius, queryCenter, queryRadius))
                        {
                            expected |= 1 << lane;
                        }
                    }

                    int mask = BvhNodeTest.OverlapMask(ref tree, nodeIndex, queryCenter, queryRadius);
                    Assert.That(mask, Is.EqualTo(expected), "node " + nodeIndex);

                    int activeMask = (int)tree.ActiveMask(nodeIndex);
                    Assert.That(activeMask & ~BvhLayout.FullActiveMask, Is.Zero);

                    // Every lane traversal would actually visit must carry a real child kind.
                    int visitMask = mask & activeMask;
                    for (int lane = 0; lane < BvhConfig.ChildCount; lane++)
                    {
                        if (((visitMask >> lane) & 1) == 0)
                        {
                            continue;
                        }

                        Assert.That(tree.LaneKind(nodeIndex, lane), Is.Not.EqualTo(BvhChildKind.Unused));
                    }
                }
            }
        }

        [Test]
        public void Traversal_ReturnsEveryBruteForceOverlapForRandomScenes()
        {
            int[] counts = { 1, 3, 8, 9, 17, 64, 129, 257 };
            uint[] seeds = { 11u, 977u, 65537u };

            for (int countIndex = 0; countIndex < counts.Length; countIndex++)
            {
                for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
                {
                    int count = counts[countIndex];
                    NativeArray<BvhCircle> source = AllocateObjects(count);
                    FillRandom(source, count, seeds[seedIndex], 40f, 0.05f, 2f);
                    Build(source, count);
                    AssertValid(source, count);
                    AssertNoFalseNegatives(source, count, float2.zero, 45f);
                }
            }
        }

        [Test]
        public void Traversal_SparseDistributionHasNoFalseNegatives()
        {
            const int count = 192;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 7001u, 2000f, 0.02f, 0.5f);

            Build(source, count);
            AssertValid(source, count);
            AssertNoFalseNegatives(source, count, float2.zero, 2100f, 7002u);
        }

        [Test]
        public void Traversal_ClusteredDistributionHasNoFalseNegatives()
        {
            const int count = 256;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            Random random = new(7101u);
            float2[] clusterCenters =
            {
                new(-80f, -60f),
                new(75f, -55f),
                new(-65f, 90f),
                new(95f, 85f)
            };

            for (int i = 0; i < count; i++)
            {
                float2 center = clusterCenters[i & 3] + random.NextFloat2(-3f, 3f);
                source[i] = new BvhCircle(center, random.NextFloat(0.05f, 2f));
            }

            Build(source, count);
            AssertValid(source, count);
            AssertNoFalseNegatives(source, count, float2.zero, 110f, 7102u);
        }

        [Test]
        public void Traversal_IdenticalCenterDistributionHasNoFalseNegatives()
        {
            const int count = 160;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            Random random = new(7201u);
            float2 center = new(-17.25f, 33.5f);
            for (int i = 0; i < count; i++)
            {
                source[i] = new BvhCircle(center, random.NextFloat(0f, 8f));
            }

            Build(source, count);
            AssertValid(source, count);
            AssertNoFalseNegatives(source, count, center, 12f, 7202u);
        }

        [Test]
        public void Traversal_OversizedCircleDistributionHasNoFalseNegatives()
        {
            const int count = 144;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 7301u, 100f, 20f, 180f);

            Build(source, count);
            AssertValid(source, count);
            AssertNoFalseNegatives(source, count, float2.zero, 250f, 7302u);
        }

        [Test]
        public void Traversal_LongThinDistributionHasNoFalseNegatives()
        {
            const int count = 320;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            Random random = new(7401u);
            for (int i = 0; i < count; i++)
            {
                source[i] = new BvhCircle(
                    new float2(random.NextFloat(-2000f, 2000f), random.NextFloat(-0.1f, 0.1f)),
                    random.NextFloat(0.01f, 1f));
            }

            Build(source, count);
            AssertValid(source, count);
            AssertNoFalseNegatives(source, count, float2.zero, 2100f, 7402u);
        }

        [Test]
        public void Traversal_MovingTargetFramesHaveNoFalseNegatives()
        {
            const int count = 180;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 7501u, 120f, 0.05f, 3f);
            Random random = new(7502u);
            float2[] velocities = new float2[count];
            for (int i = 0; i < count; i++)
            {
                velocities[i] = random.NextFloat2(-4f, 4f);
            }

            for (int frame = 0; frame < 8; frame++)
            {
                for (int i = 0; i < count; i++)
                {
                    BvhCircle circle = source[i];
                    source[i] = new BvhCircle(circle.Center + velocities[i], circle.Radius);
                }

                Build(source, count);
                AssertValid(source, count);
                AssertNoFalseNegatives(source, count, float2.zero, 170f, (uint)(7600 + frame));
            }
        }

        [Test]
        public void Traversal_StreamsMoreCandidatesThanTheFixedStackCanHold()
        {
            int count = BvhTraversalWorkspace.StackCapacity * 3;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            for (int i = 0; i < count; i++)
            {
                source[i] = new BvhCircle(new float2(i * 0.001f, 0f), 0.05f);
            }

            Build(source, count);

            List<int> candidates = Query(new float2(count * 0.0005f, 0f), 10f);
            Assert.That(count, Is.GreaterThan(127));
            Assert.That(candidates.Count, Is.EqualTo(count));
            Assert.That(new HashSet<int>(candidates).Count, Is.EqualTo(count));
        }

        [Test]
        public void Traversal_ReusedWorkspaceMatchesFreshWorkspacePerQuery()
        {
            const int count = 300;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 8123u, 30f, 0.1f, 1.5f);
            Build(source, count);

            BvhTraversalWorkspace reused = BvhTraversalWorkspace.Create(tree);
            Random random = new(555u);

            for (int iteration = 0; iteration < 50; iteration++)
            {
                float2 queryCenter = random.NextFloat2(-35f, 35f);
                float queryRadius = random.NextFloat(0.1f, 4f);

                reused.Reset(queryCenter, queryRadius);
                List<int> streamed = new();
                while (reused.TryMoveNext(out int objectIndex))
                {
                    streamed.Add(objectIndex);
                }

                Assert.That(streamed, Is.EqualTo(Query(queryCenter, queryRadius)));
            }

            Assert.That(reused.MaxStackDepth, Is.GreaterThan(0));
            Assert.That(reused.MaxStackDepth, Is.LessThanOrEqualTo(BvhTraversalWorkspace.StackCapacity));
        }

        [Test]
        public void Traversal_FixedStackCapacityIsSufficientForBuiltDepth()
        {
            const int count = 4096;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            FillRandom(source, count, 31337u, 120f, 0.05f, 0.5f);
            Build(source, count);

            Assert.That(
                BvhValidation.ValidateTraversalStackCapacity(
                    tree.Depth, BvhTraversalWorkspace.StackCapacity, out FixedString512Bytes failure),
                Is.True,
                failure.ToString());
            Assert.That(
                BvhValidation.RequiredTraversalStackDepth(tree.Depth),
                Is.LessThanOrEqualTo(BvhTraversalWorkspace.StackCapacity));

            BvhTraversalWorkspace workspace = BvhTraversalWorkspace.Create(tree);
            workspace.Reset(float2.zero, 500f);
            int visited = 0;
            while (workspace.TryMoveNext(out _))
            {
                visited++;
            }

            Assert.That(visited, Is.EqualTo(count));
            Assert.That(workspace.MaxStackDepth, Is.LessThanOrEqualTo(BvhTraversalWorkspace.StackCapacity));
        }

        [Test]
        public void ShapeBoundingCircles_NeverPruneExactRectangleOrCapsuleHits()
        {
            const int count = 200;
            NativeArray<BvhCircle> source = AllocateObjects(count);
            Random random = new(606060u);

            float2[] positions = new float2[count];
            float[] radii = new float[count];
            float2[] halfExtents = new float2[count];
            float[] rotations = new float[count];
            CombatShapeType[] shapes = new CombatShapeType[count];

            for (int i = 0; i < count; i++)
            {
                positions[i] = random.NextFloat2(-20f, 20f);
                radii[i] = random.NextFloat(0.1f, 0.6f);
                halfExtents[i] = random.NextFloat2(0.1f, 1.2f);
                rotations[i] = random.NextFloat(0f, 6.2831855f);
                shapes[i] = (CombatShapeType)random.NextInt(0, 3);
                source[i] = BvhCircle.FromShape(positions[i], radii[i], halfExtents[i], shapes[i]);
            }

            Build(source, count);
            AssertValid(source, count);

            for (int iteration = 0; iteration < 120; iteration++)
            {
                float2 sourcePosition = random.NextFloat2(-22f, 22f);
                float sourceRadius = random.NextFloat(0.1f, 0.8f);
                float2 sourceHalfExtents = random.NextFloat2(0.1f, 1.5f);
                float sourceRotation = random.NextFloat(0f, 6.2831855f);
                CombatShapeType sourceShape = (CombatShapeType)random.NextInt(0, 3);

                BvhCircle queryCircle = BvhCircle.FromShape(
                    sourcePosition, sourceRadius, sourceHalfExtents, sourceShape);
                HashSet<int> candidates = new(Query(queryCircle.Center, queryCircle.Radius));

                for (int i = 0; i < count; i++)
                {
                    bool exactHit = CombatCollisionMath.Hit(
                        sourcePosition, sourceRadius, sourceHalfExtents, sourceRotation, sourceShape,
                        positions[i], radii[i], halfExtents[i], rotations[i], shapes[i]);

                    if (exactHit)
                    {
                        Assert.That(
                            candidates.Contains(i),
                            Is.True,
                            "broadphase pruned exact hit on object " + i + " at iteration " + iteration);
                    }
                }
            }
        }

        private NativeArray<BvhCircle> AllocateObjects(int count)
        {
            if (objects.IsCreated)
            {
                objects.Dispose();
            }

            // Always allocate at least one element so an empty scene still hands the builder
            // a created array; the build honours the separate count argument.
            objects = new NativeArray<BvhCircle>(math.max(1, count), Allocator.Persistent);
            return objects;
        }

        private void Build(NativeArray<BvhCircle> source, int count)
        {
            BvhBuilder.Build(ref tree, ref scratch, source, count);
        }

        private List<int> Query(float2 center, float radius)
        {
            BvhTraversalWorkspace workspace = BvhTraversalWorkspace.Create(tree);
            workspace.Reset(center, radius);

            List<int> candidates = new();
            while (workspace.TryMoveNext(out int objectIndex))
            {
                candidates.Add(objectIndex);
            }

            return candidates;
        }

        private void AssertValid(NativeArray<BvhCircle> source, int count)
        {
            bool valid = BvhValidation.ValidateTree(
                ref tree, source, count, BvhLayout.ContainmentEpsilon, out FixedString512Bytes failure);
            Assert.That(valid, Is.True, failure.ToString());
        }

        private void AssertUnusedLanes(int nodeIndex, int activeLaneCount)
        {
            for (int lane = activeLaneCount; lane < BvhConfig.ChildCount; lane++)
            {
                Assert.That(tree.LaneKind(nodeIndex, lane), Is.EqualTo(BvhChildKind.Unused));
                Assert.That(tree.LaneReference(nodeIndex, lane), Is.EqualTo(-1));
                Assert.That((tree.ActiveMask(nodeIndex) >> lane) & 1u, Is.Zero);
            }
        }

        private void AssertNoFalseNegatives(
            NativeArray<BvhCircle> source,
            int count,
            float2 queryOrigin,
            float querySpread,
            uint seed = 1234567u)
        {
            Random random = new(seed);

            for (int iteration = 0; iteration < 40; iteration++)
            {
                float queryRadius = random.NextFloat(0f, 3f);
                float2 queryCenter;
                if (count > 0 && (iteration & 1) == 0)
                {
                    BvhCircle anchor = source[random.NextInt(0, count)];
                    float2 direction = math.normalizesafe(
                        random.NextFloat2(-1f, 1f),
                        new float2(1f, 0f));
                    float distance = random.NextFloat(0f, anchor.Radius + queryRadius);
                    queryCenter = anchor.Center + (direction * distance);
                }
                else
                {
                    queryCenter = queryOrigin + random.NextFloat2(-querySpread, querySpread);
                }

                HashSet<int> candidates = new(Query(queryCenter, queryRadius));

                for (int i = 0; i < count; i++)
                {
                    BvhCircle circle = source[i];
                    float combined = circle.Radius + queryRadius;
                    if (math.distancesq(circle.Center, queryCenter) > combined * combined)
                    {
                        continue;
                    }

                    Assert.That(
                        candidates.Contains(i),
                        Is.True,
                        "broadphase missed object " + i + " at iteration " + iteration);
                }
            }
        }

        private static bool ScalarOverlap(
            float childCenterX,
            float childCenterY,
            float childRadius,
            float2 queryCenter,
            float queryRadius)
        {
            float deltaX = childCenterX - queryCenter.x;
            float deltaY = childCenterY - queryCenter.y;
            float combinedRadius = childRadius + (queryRadius + BvhLayout.OverlapEpsilon);
            return ((deltaX * deltaX) + (deltaY * deltaY)) <= (combinedRadius * combinedRadius);
        }

        private static void FillGrid(NativeArray<BvhCircle> source, int count, float spacing, float radius)
        {
            int columns = (int)math.ceil(math.sqrt((float)count));
            for (int i = 0; i < count; i++)
            {
                float2 center = new((i % columns) * spacing, (i / columns) * spacing);
                source[i] = new BvhCircle(center, radius);
            }
        }

        private static void FillRandom(
            NativeArray<BvhCircle> source,
            int count,
            uint seed,
            float extent,
            float minRadius,
            float maxRadius)
        {
            Random random = new(seed);
            for (int i = 0; i < count; i++)
            {
                source[i] = new BvhCircle(
                    random.NextFloat2(-extent, extent),
                    random.NextFloat(minRadius, maxRadius));
            }
        }
    }
}
