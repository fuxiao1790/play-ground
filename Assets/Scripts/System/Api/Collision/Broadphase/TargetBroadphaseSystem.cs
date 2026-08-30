using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Combat.Collision.Broadphase
{
    // ECS Lifecycle: singleton target broadphase data (retained spatial hashes plus the discrete
    // projectile BVH); created by TargetBroadphaseSystem on create, rebuilt every simulation
    // update from one target snapshot, and disposed by TargetBroadphaseSystem on destroy.
    public struct TargetBroadphaseSingleton : IComponentData
    {
        public NativeParallelMultiHashMap<long, int> ProjectileCollisionCells;
        public NativeParallelMultiHashMap<long, int> TrackingCells;
        public NativeParallelHashMap<long, int> TrackingIndicesById;
        public NativeParallelMultiHashMap<long, int> AoeOccupiedCells;
        public NativeReference<float> MaxTargetRadius;
        public NativeList<Entity> TargetEntities;
        public NativeList<TargetPosition> TargetPositions;
        public NativeList<TargetCollisionShape> TargetShapes;
        public NativeList<TargetFaction> TargetFactions;

        /// <summary>
        /// Bounding circle of every snapshot target, same index order as the snapshot lists.
        /// Derived cache (center + radius only) the BVH build consumes; not a second
        /// entity/shape/faction snapshot.
        /// </summary>
        public NativeList<BvhCircle> TargetCircles;

        /// <summary>Discrete projectile collision BVH; leaves reference snapshot indices.</summary>
        public BvhTree DiscreteBvh;

        /// <summary>Reusable Morton/level workspace for the discrete BVH rebuild.</summary>
        public BvhBuildScratch DiscreteBvhScratch;

#if ENABLE_PROFILER
        /// <summary>One worker-local discrete-query aggregate per processed chunk.</summary>
        public NativeQueue<BvhQueryMetrics> DiscreteQueryMetrics;
#endif

        public int TargetCount;
        public JobHandle BuildHandle;
        public JobHandle ConsumerHandle;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileDiscreteCollisionSystem))]
    [UpdateBefore(typeof(LingeringAoeCollisionSystem))]
    [UpdateBefore(typeof(ImpactAoeCollisionSystem))]
    public partial struct TargetBroadphaseSystem : ISystem
    {
        private static readonly ProfilerMarker GatherMarker =
            new("TargetBroadphaseSystem.Gather");
        private static readonly ProfilerMarker ContinuousHashBuildMarker =
            new("TargetBroadphaseSystem.Build.ContinuousHash");
        private static readonly ProfilerMarker TrackingHashBuildMarker =
            new("TargetBroadphaseSystem.Build.TrackingHash");
        private static readonly ProfilerMarker AoeOccupiedHashBuildMarker =
            new("TargetBroadphaseSystem.Build.AoeOccupiedHash");
        private static readonly ProfilerMarker BvhBuildMarker =
            new("TargetBroadphaseSystem.Build.MortonBvh");
#if ENABLE_PROFILER
        // Keep counters in their own type. Burst jobs below reference this system's marker
        // fields, which makes Burst visit TargetBroadphaseSystem's static constructor. A
        // ProfilerCounterValue constructor performs an external native call and is forbidden
        // from a Burst static constructor (BC1091). Nested type has a separate static
        // constructor, reached only by managed OnUpdate after worker handles complete.
        private static class ProfilerCounters
        {
            public static readonly ProfilerCounterValue<int> TargetCount =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.TargetCount", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> NodeCount =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.NodeCount", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> Depth =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.Depth", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> ActiveLaneCount =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.ActiveLanes", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<long> QueryCount =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.QueryCount", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<long> NodesVisited =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.NodesVisited", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<long> ChildCirclesTested =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.ChildCirclesTested", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<long> SurvivingLanes =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.SurvivingLanes", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<long> LeafCandidates =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.LeafCandidates", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<long> ExactTests =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.ExactTests", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<float> AverageNodesVisited =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.AverageNodesVisited", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<float> AverageChildCirclesTested =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.AverageChildCirclesTested", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<float> AverageSurvivingLanes =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.AverageSurvivingLanes", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<float> AverageSurvivingLanesPerNode =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.AverageSurvivingLanesPerNode", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<float> AverageLeafCandidates =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.AverageLeafCandidates", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<float> AverageExactTests =
                new(ProfilerCategory.Scripts, "TargetBroadphase.Bvh.AverageExactTests", ProfilerMarkerDataUnit.Count);
        }
#endif

        private Entity singletonEntity;
        private EntityQuery targetQuery;
        private EntityTypeHandle entityHandle;
        private ComponentTypeHandle<TargetPosition> positionHandle;
        private ComponentTypeHandle<TargetCollisionShape> shapeHandle;
        private ComponentTypeHandle<TargetFaction> factionHandle;

        public void OnCreate(ref SystemState state)
        {
            // Fails loudly here, before any container exists, if the compile-time BVH width is
            // unsupported — an unsupported width can then never reach a build or a query.
            BvhValidation.ValidateConfiguration();

            targetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TargetProxyTag>(),
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadOnly<TargetCollisionShape>(),
                ComponentType.ReadOnly<TargetFaction>());

            entityHandle = state.GetEntityTypeHandle();
            positionHandle = state.GetComponentTypeHandle<TargetPosition>(true);
            shapeHandle = state.GetComponentTypeHandle<TargetCollisionShape>(true);
            factionHandle = state.GetComponentTypeHandle<TargetFaction>(true);

            TargetBroadphaseSingleton singleton = new()
            {
                ProjectileCollisionCells = new NativeParallelMultiHashMap<long, int>(1, Allocator.Persistent),
                TrackingCells = new NativeParallelMultiHashMap<long, int>(1, Allocator.Persistent),
                TrackingIndicesById = new NativeParallelHashMap<long, int>(1, Allocator.Persistent),
                AoeOccupiedCells = new NativeParallelMultiHashMap<long, int>(1, Allocator.Persistent),
                MaxTargetRadius = new NativeReference<float>(Allocator.Persistent),
                TargetEntities = new NativeList<Entity>(1, Allocator.Persistent),
                TargetPositions = new NativeList<TargetPosition>(1, Allocator.Persistent),
                TargetShapes = new NativeList<TargetCollisionShape>(1, Allocator.Persistent),
                TargetFactions = new NativeList<TargetFaction>(1, Allocator.Persistent),
                TargetCircles = new NativeList<BvhCircle>(1, Allocator.Persistent),
                DiscreteBvh = BvhTree.Create(1, Allocator.Persistent),
                DiscreteBvhScratch = BvhBuildScratch.Create(1, Allocator.Persistent),
#if ENABLE_PROFILER
                DiscreteQueryMetrics = new NativeQueue<BvhQueryMetrics>(Allocator.Persistent)
#endif
            };

            singleton.MaxTargetRadius.Value = 0f;
            singletonEntity = state.EntityManager.CreateEntity(typeof(TargetBroadphaseSingleton));
            state.EntityManager.SetComponentData(singletonEntity, singleton);
        }

        public void OnUpdate(ref SystemState state)
        {
            TargetBroadphaseSingleton singleton =
                state.EntityManager.GetComponentData<TargetBroadphaseSingleton>(singletonEntity);

            // Consumers add read jobs to ConsumerHandle after depending on BuildHandle. Completing it
            // here makes the persistent maps safe to clear and rebuild for this frame.
            singleton.ConsumerHandle.Complete();
            singleton.BuildHandle.Complete();
            singleton.ConsumerHandle = default;
            singleton.BuildHandle = default;

#if ENABLE_PROFILER
            PublishProfilerMetrics(ref singleton);
#endif

            int targetCount = targetQuery.CalculateEntityCount();
            singleton.TargetCount = targetCount;

            if (targetCount == 0)
            {
                ClearContainers(ref singleton);
                singleton.TargetEntities.Clear();
                singleton.TargetPositions.Clear();
                singleton.TargetShapes.Clear();
                singleton.TargetFactions.Clear();
                singleton.TargetCircles.Clear();

                // Zero-object build clears node bookkeeping and resets the root to -1 without
                // reading the object array, so no stale tree survives an empty frame. Both
                // handles are already complete above, so this runs safely on the main thread.
                BvhBuilder.Build(ref singleton.DiscreteBvh, ref singleton.DiscreteBvhScratch, default, 0);

                singleton.MaxTargetRadius.Value = 0f;
                singleton.BuildHandle = state.Dependency;
                state.EntityManager.SetComponentData(singletonEntity, singleton);
                return;
            }

            ResizeSnapshotLists(ref singleton, targetCount);

            // One set of array views, taken before anything is scheduled: the gather job writes
            // them and the four build jobs read them back through the same views.
            NativeArray<Entity> targetEntities = singleton.TargetEntities.AsArray();
            NativeArray<TargetPosition> targetPositions = singleton.TargetPositions.AsArray();
            NativeArray<TargetCollisionShape> targetShapes = singleton.TargetShapes.AsArray();
            NativeArray<TargetFaction> targetFactions = singleton.TargetFactions.AsArray();
            NativeArray<BvhCircle> targetCircles = singleton.TargetCircles.AsArray();

            entityHandle.Update(ref state);
            positionHandle.Update(ref state);
            shapeHandle.Update(ref state);
            factionHandle.Update(ref state);

            // Sequential Schedule, never ScheduleParallel: the job carries one running write
            // index across chunks so the snapshot lists and the circle cache stay in lockstep
            // by index, and that index is not race-safe across parallel chunk invocations.
            // Target counts here are small, so sequential scheduling costs nothing.
            JobHandle gatherHandle = new GatherTargetsJob
            {
                EntityHandle = entityHandle,
                PositionHandle = positionHandle,
                ShapeHandle = shapeHandle,
                FactionHandle = factionHandle,
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetShapes = targetShapes,
                TargetFactions = targetFactions,
                TargetCircles = targetCircles,
                WriteIndex = 0
            }.Schedule(targetQuery, state.Dependency);

            ClearContainers(ref singleton);
            EnsureCapacity(ref singleton.ProjectileCollisionCells, targetCount);
            EnsureCapacity(ref singleton.TrackingCells, targetCount);
            EnsureCapacity(ref singleton.TrackingIndicesById, targetCount);

            // The BVH's node/root/depth bookkeeping is a deterministic function of the object
            // count and the configured width, so it is computed here from the same recurrence
            // the builder uses. The build job works on a struct copy: its native lists are
            // shared handles, but these scalars are not, so the published copy sets them itself.
            singleton.DiscreteBvh.ObjectCount = targetCount;
            singleton.DiscreteBvh.NodeCount = BvhBuilder.ComputeNodeCount(targetCount);
            singleton.DiscreteBvh.RootNodeIndex = singleton.DiscreteBvh.NodeCount - 1;
            singleton.DiscreteBvh.Depth = BvhBuilder.ComputeDepth(targetCount);

            JobHandle projectileHandle;
            JobHandle trackingHandle;
            JobHandle aoeHandle;
            JobHandle bvhHandle;

            projectileHandle = new BuildProjectileCollisionHashJob
            {
                TargetPositions = targetPositions,
                TargetShapes = targetShapes,
                ProjectileCollisionCells = singleton.ProjectileCollisionCells,
                MaxTargetRadius = singleton.MaxTargetRadius
            }.Schedule(gatherHandle);

            trackingHandle = new BuildTrackingHashJob
            {
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TrackingCells = singleton.TrackingCells,
                TrackingIndicesById = singleton.TrackingIndicesById
            }.Schedule(gatherHandle);

            aoeHandle = new BuildAoeOccupiedHashJob
            {
                TargetShapes = targetShapes,
                AoeOccupiedCells = singleton.AoeOccupiedCells
            }.Schedule(gatherHandle);

            bvhHandle = new BuildDiscreteBvhJob
            {
                Tree = singleton.DiscreteBvh,
                Scratch = singleton.DiscreteBvhScratch,
                TargetCircles = targetCircles,
                TargetCount = targetCount
            }.Schedule(gatherHandle);

            singleton.BuildHandle = JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(projectileHandle, trackingHandle, aoeHandle),
                bvhHandle);
            state.Dependency = singleton.BuildHandle;
            state.EntityManager.SetComponentData(singletonEntity, singleton);
        }

        public void OnDestroy(ref SystemState state)
        {
            TargetBroadphaseSingleton singleton = default;
            if (singletonEntity != Entity.Null
                && state.EntityManager.Exists(singletonEntity)
                && state.EntityManager.HasComponent<TargetBroadphaseSingleton>(singletonEntity))
            {
                singleton = state.EntityManager.GetComponentData<TargetBroadphaseSingleton>(singletonEntity);
            }

            singleton.BuildHandle.Complete();
            singleton.ConsumerHandle.Complete();
            DisposeIfCreated(ref singleton.ProjectileCollisionCells);
            DisposeIfCreated(ref singleton.TrackingCells);
            DisposeIfCreated(ref singleton.TrackingIndicesById);
            DisposeIfCreated(ref singleton.AoeOccupiedCells);
            DisposeIfCreated(ref singleton.MaxTargetRadius);
            DisposeIfCreated(ref singleton.TargetEntities);
            DisposeIfCreated(ref singleton.TargetPositions);
            DisposeIfCreated(ref singleton.TargetShapes);
            DisposeIfCreated(ref singleton.TargetFactions);
            DisposeIfCreated(ref singleton.TargetCircles);

            if (singleton.DiscreteBvh.IsCreated)
            {
                singleton.DiscreteBvh.Dispose();
            }

            if (singleton.DiscreteBvhScratch.IsCreated)
            {
                singleton.DiscreteBvhScratch.Dispose();
            }

#if ENABLE_PROFILER
            if (singleton.DiscreteQueryMetrics.IsCreated)
            {
                singleton.DiscreteQueryMetrics.Dispose();
            }
#endif
        }

#if ENABLE_PROFILER
        private static void PublishProfilerMetrics(ref TargetBroadphaseSingleton singleton)
        {
            int activeLaneCount = 0;
            for (int nodeIndex = 0; nodeIndex < singleton.DiscreteBvh.NodeCount; nodeIndex++)
            {
                activeLaneCount += math.countbits(singleton.DiscreteBvh.NodeActiveMasks[nodeIndex]);
            }

            long queryCount = 0;
            long nodesVisited = 0;
            long childCirclesTested = 0;
            long survivingLanes = 0;
            long leafCandidates = 0;
            long exactTests = 0;
            while (singleton.DiscreteQueryMetrics.TryDequeue(out BvhQueryMetrics metrics))
            {
                queryCount += metrics.QueryCount;
                nodesVisited += metrics.NodesVisited;
                childCirclesTested += metrics.ChildCirclesTested;
                survivingLanes += metrics.SurvivingLanes;
                leafCandidates += metrics.LeafCandidates;
                exactTests += metrics.ExactTests;
            }

            ProfilerCounters.TargetCount.Value = singleton.TargetCount;
            ProfilerCounters.NodeCount.Value = singleton.DiscreteBvh.NodeCount;
            ProfilerCounters.Depth.Value = singleton.DiscreteBvh.Depth;
            ProfilerCounters.ActiveLaneCount.Value = activeLaneCount;
            ProfilerCounters.QueryCount.Value = queryCount;
            ProfilerCounters.NodesVisited.Value = nodesVisited;
            ProfilerCounters.ChildCirclesTested.Value = childCirclesTested;
            ProfilerCounters.SurvivingLanes.Value = survivingLanes;
            ProfilerCounters.LeafCandidates.Value = leafCandidates;
            ProfilerCounters.ExactTests.Value = exactTests;

            float inverseQueryCount = queryCount > 0 ? 1f / queryCount : 0f;
            ProfilerCounters.AverageNodesVisited.Value = nodesVisited * inverseQueryCount;
            ProfilerCounters.AverageChildCirclesTested.Value = childCirclesTested * inverseQueryCount;
            ProfilerCounters.AverageSurvivingLanes.Value = survivingLanes * inverseQueryCount;
            ProfilerCounters.AverageSurvivingLanesPerNode.Value =
                nodesVisited > 0 ? (float)survivingLanes / nodesVisited : 0f;
            ProfilerCounters.AverageLeafCandidates.Value = leafCandidates * inverseQueryCount;
            ProfilerCounters.AverageExactTests.Value = exactTests * inverseQueryCount;
        }
#endif

        private static void ResizeSnapshotLists(ref TargetBroadphaseSingleton singleton, int targetCount)
        {
            EnsureCapacity(ref singleton.TargetEntities, targetCount);
            EnsureCapacity(ref singleton.TargetPositions, targetCount);
            EnsureCapacity(ref singleton.TargetShapes, targetCount);
            EnsureCapacity(ref singleton.TargetFactions, targetCount);
            EnsureCapacity(ref singleton.TargetCircles, targetCount);
            singleton.TargetEntities.ResizeUninitialized(targetCount);
            singleton.TargetPositions.ResizeUninitialized(targetCount);
            singleton.TargetShapes.ResizeUninitialized(targetCount);
            singleton.TargetFactions.ResizeUninitialized(targetCount);
            singleton.TargetCircles.ResizeUninitialized(targetCount);
        }

        private static int CalculateAoeCapacity(NativeArray<TargetCollisionShape> targetShapes)
        {
            int capacity = 0;
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape target = targetShapes[i];
                int2 min = CombatSpatialHash.MinCell(target.BoundsMin, CombatSpatialHash.AoeCellSize);
                int2 max = CombatSpatialHash.MaxCell(target.BoundsMax, CombatSpatialHash.AoeCellSize);
                capacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
            }

            return math.max(1, capacity);
        }

        private static void ClearContainers(ref TargetBroadphaseSingleton singleton)
        {
            singleton.ProjectileCollisionCells.Clear();
            singleton.TrackingCells.Clear();
            singleton.TrackingIndicesById.Clear();
            singleton.AoeOccupiedCells.Clear();
        }

        private static void EnsureCapacity<T>(ref NativeList<T> list, int capacity)
            where T : unmanaged
        {
            int required = math.max(1, capacity);
            if (list.Capacity < required)
            {
                list.Capacity = required;
            }
        }

        private static void EnsureCapacity(ref NativeParallelMultiHashMap<long, int> map, int capacity)
        {
            int required = math.max(1, capacity);
            if (map.Capacity < required)
            {
                map.Capacity = required;
            }
        }

        private static void EnsureCapacity(ref NativeParallelHashMap<long, int> map, int capacity)
        {
            int required = math.max(1, capacity);
            if (map.Capacity < required)
            {
                map.Capacity = required;
            }
        }

        private static void DisposeIfCreated(ref NativeParallelMultiHashMap<long, int> map)
        {
            if (map.IsCreated)
            {
                map.Dispose();
            }
        }

        private static void DisposeIfCreated(ref NativeParallelHashMap<long, int> map)
        {
            if (map.IsCreated)
            {
                map.Dispose();
            }
        }

        private static void DisposeIfCreated(ref NativeReference<float> reference)
        {
            if (reference.IsCreated)
            {
                reference.Dispose();
            }
        }

        private static void DisposeIfCreated<T>(ref NativeList<T> list)
            where T : unmanaged
        {
            if (list.IsCreated)
            {
                list.Dispose();
            }
        }

        /// <summary>
        /// One pass over the target query filling the persistent snapshot lists and the derived
        /// circle cache. Must be scheduled sequentially: <see cref="WriteIndex"/> runs across
        /// chunk invocations and is not race-safe under ScheduleParallel.
        /// </summary>
        [BurstCompile]
        private struct GatherTargetsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<TargetPosition> PositionHandle;
            [ReadOnly] public ComponentTypeHandle<TargetCollisionShape> ShapeHandle;
            [ReadOnly] public ComponentTypeHandle<TargetFaction> FactionHandle;

            public NativeArray<Entity> TargetEntities;
            public NativeArray<TargetPosition> TargetPositions;
            public NativeArray<TargetCollisionShape> TargetShapes;
            public NativeArray<TargetFaction> TargetFactions;
            public NativeArray<BvhCircle> TargetCircles;

            // Running snapshot write index, carried across chunk invocations of this single
            // sequential job instance.
            public int WriteIndex;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                using ProfilerMarker.AutoScope marker = GatherMarker.Auto();
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityHandle);
                NativeArray<TargetPosition> positions = chunk.GetNativeArray(ref PositionHandle);
                NativeArray<TargetCollisionShape> shapes = chunk.GetNativeArray(ref ShapeHandle);
                NativeArray<TargetFaction> factions = chunk.GetNativeArray(ref FactionHandle);

                ChunkEntityEnumerator enumerator =
                    new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    TargetPosition position = positions[i];
                    TargetCollisionShape shape = shapes[i];

                    TargetEntities[WriteIndex] = entities[i];
                    TargetPositions[WriteIndex] = position;
                    TargetShapes[WriteIndex] = shape;
                    TargetFactions[WriteIndex] = factions[i];
                    TargetCircles[WriteIndex] = BvhCircle.FromShape(
                        position.Value,
                        shape.Radius,
                        shape.HalfExtents,
                        shape.ShapeType);
                    WriteIndex++;
                }
            }
        }

        [BurstCompile]
        private struct BuildProjectileCollisionHashJob : IJob
        {
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            public NativeParallelMultiHashMap<long, int> ProjectileCollisionCells;
            public NativeReference<float> MaxTargetRadius;

            public void Execute()
            {
                using ProfilerMarker.AutoScope marker = ContinuousHashBuildMarker.Auto();
                float maxTargetRadius = 0f;
                for (int i = 0; i < TargetPositions.Length; i++)
                {
                    int2 cell = CombatSpatialHash.FloorCell(
                        TargetPositions[i].Value,
                        CombatSpatialHash.ProjectileCollisionCellSize);
                    ProjectileCollisionCells.Add(CombatSpatialHash.CellKey(cell.x, cell.y), i);

                    TargetCollisionShape target = TargetShapes[i];
                    float radius = CombatCollisionMath.BoundingRadius(
                        target.Radius,
                        target.HalfExtents,
                        target.ShapeType);
                    maxTargetRadius = math.max(maxTargetRadius, radius);
                }

                MaxTargetRadius.Value = maxTargetRadius;
            }
        }

        [BurstCompile]
        private struct BuildTrackingHashJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            public NativeParallelMultiHashMap<long, int> TrackingCells;
            public NativeParallelHashMap<long, int> TrackingIndicesById;

            public void Execute()
            {
                using ProfilerMarker.AutoScope marker = TrackingHashBuildMarker.Auto();
                for (int i = 0; i < TargetEntities.Length; i++)
                {
                    TrackingIndicesById.TryAdd(
                        TargetIdKey(TargetedAcquisition.TargetKey(TargetEntities[i])), i);
                    int2 cell = CombatSpatialHash.FloorCell(
                        TargetPositions[i].Value,
                        CombatSpatialHash.TrackingCellSize);
                    TrackingCells.Add(CombatSpatialHash.CellKey(cell.x, cell.y), i);
                }
            }
        }

        [BurstCompile]
        private struct BuildAoeOccupiedHashJob : IJob
        {
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            public NativeParallelMultiHashMap<long, int> AoeOccupiedCells;

            public void Execute()
            {
                using ProfilerMarker.AutoScope marker = AoeOccupiedHashBuildMarker.Auto();
                // Same occupied-cell capacity math as before, evaluated here because the shapes it
                // sums over are now filled by the scheduled gather job rather than a main-thread
                // copy. The map is cleared on the main thread before this job is scheduled.
                EnsureCapacity(ref AoeOccupiedCells, CalculateAoeCapacity(TargetShapes));

                for (int i = 0; i < TargetShapes.Length; i++)
                {
                    TargetCollisionShape target = TargetShapes[i];
                    int2 min = CombatSpatialHash.MinCell(target.BoundsMin, CombatSpatialHash.AoeCellSize);
                    int2 max = CombatSpatialHash.MaxCell(target.BoundsMax, CombatSpatialHash.AoeCellSize);
                    for (int y = min.y; y <= max.y; y++)
                    {
                        for (int x = min.x; x <= max.x; x++)
                        {
                            AoeOccupiedCells.Add(CombatSpatialHash.CellKey(x, y), i);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Full deterministic rebuild of the discrete projectile BVH from the snapshot circles.
        /// The tree/scratch structs are copies; their native lists are shared handles, so the
        /// build's buffer contents land in the singleton's containers.
        /// </summary>
        [BurstCompile]
        private struct BuildDiscreteBvhJob : IJob
        {
            public BvhTree Tree;
            public BvhBuildScratch Scratch;
            [ReadOnly] public NativeArray<BvhCircle> TargetCircles;
            public int TargetCount;

            public void Execute()
            {
                using ProfilerMarker.AutoScope marker = BvhBuildMarker.Auto();
                BvhBuilder.Build(ref Tree, ref Scratch, TargetCircles, TargetCount);
            }
        }

        private static long TargetIdKey(int targetId)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)targetId) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
