using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Vfx
{
    // ECS Lifecycle: singleton VFX dispatch queue; created by CombatAoeVfxDispatchSystem on
    // create, drained every presentation update, disposed by CombatAoeVfxDispatchSystem on destroy.
    // Per-shape sorted lists and bucket offsets are grow-only scratch reused every frame by
    // bucketing jobs - never reallocated just to shrink, mirroring
    // AoeVfxTypeResources.EnsureBufferCapacity's GraphicsBuffer growth pattern.
    public struct CombatAoeVfxDispatchSingleton : IComponentData
    {
        public NativeQueue<CircularVfxSpawnRequest> PendingCircularSpawns;
        public NativeList<float2> CircularSortedPositions;
        public NativeList<float> CircularSortedAreaSizes;
        public NativeList<int> CircularBucketOffsets;
        public NativeQueue<TimedCircularVfxSpawnRequest> PendingTimedCircularSpawns;
        public NativeList<float2> TimedCircularSortedPositions;
        public NativeList<float> TimedCircularSortedAreaSizes;
        public NativeList<float> TimedCircularSortedDurations;
        public NativeList<float> TimedCircularSortedTickIntervals;
        public NativeList<int> TimedCircularBucketOffsets;
        public NativeQueue<LineSegmentVfxSpawn> PendingLineSegmentSpawns;
        public NativeList<float2> LineSegmentSortedStartPositions;
        public NativeList<float2> LineSegmentSortedEndPositions;
        public NativeList<float> LineSegmentSortedWidths;
        public NativeList<int> LineSegmentBucketOffsets;
        public JobHandle ProducerHandle;
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatAoeVfxDispatchSystem : SystemBase
    {
        internal int LastVfxEventCount;
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(CombatAoeVfxDispatchSingleton));
            EntityManager.SetComponentData(singletonEntity, new CombatAoeVfxDispatchSingleton
            {
                PendingCircularSpawns = new NativeQueue<CircularVfxSpawnRequest>(Allocator.Persistent),
                CircularSortedPositions = new NativeList<float2>(Allocator.Persistent),
                CircularSortedAreaSizes = new NativeList<float>(Allocator.Persistent),
                CircularBucketOffsets = new NativeList<int>(Allocator.Persistent),
                PendingTimedCircularSpawns = new NativeQueue<TimedCircularVfxSpawnRequest>(Allocator.Persistent),
                TimedCircularSortedPositions = new NativeList<float2>(Allocator.Persistent),
                TimedCircularSortedAreaSizes = new NativeList<float>(Allocator.Persistent),
                TimedCircularSortedDurations = new NativeList<float>(Allocator.Persistent),
                TimedCircularSortedTickIntervals = new NativeList<float>(Allocator.Persistent),
                TimedCircularBucketOffsets = new NativeList<int>(Allocator.Persistent),
                PendingLineSegmentSpawns = new NativeQueue<LineSegmentVfxSpawn>(Allocator.Persistent),
                LineSegmentSortedStartPositions = new NativeList<float2>(Allocator.Persistent),
                LineSegmentSortedEndPositions = new NativeList<float2>(Allocator.Persistent),
                LineSegmentSortedWidths = new NativeList<float>(Allocator.Persistent),
                LineSegmentBucketOffsets = new NativeList<int>(Allocator.Persistent)
            });
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<CombatAoeVfxDispatchSingleton>(singletonEntity))
            {
                return;
            }

            CombatAoeVfxDispatchSingleton singleton =
                EntityManager.GetComponentData<CombatAoeVfxDispatchSingleton>(singletonEntity);
            singleton.ProducerHandle.Complete();
            if (singleton.PendingCircularSpawns.IsCreated)
            {
                singleton.PendingCircularSpawns.Dispose();
            }
            if (singleton.CircularSortedPositions.IsCreated)
            {
                singleton.CircularSortedPositions.Dispose();
            }
            if (singleton.CircularSortedAreaSizes.IsCreated)
            {
                singleton.CircularSortedAreaSizes.Dispose();
            }
            if (singleton.CircularBucketOffsets.IsCreated)
            {
                singleton.CircularBucketOffsets.Dispose();
            }
            if (singleton.PendingTimedCircularSpawns.IsCreated)
            {
                singleton.PendingTimedCircularSpawns.Dispose();
            }
            if (singleton.TimedCircularSortedPositions.IsCreated)
            {
                singleton.TimedCircularSortedPositions.Dispose();
            }
            if (singleton.TimedCircularSortedAreaSizes.IsCreated)
            {
                singleton.TimedCircularSortedAreaSizes.Dispose();
            }
            if (singleton.TimedCircularSortedDurations.IsCreated)
            {
                singleton.TimedCircularSortedDurations.Dispose();
            }
            if (singleton.TimedCircularSortedTickIntervals.IsCreated)
            {
                singleton.TimedCircularSortedTickIntervals.Dispose();
            }
            if (singleton.TimedCircularBucketOffsets.IsCreated)
            {
                singleton.TimedCircularBucketOffsets.Dispose();
            }
            if (singleton.PendingLineSegmentSpawns.IsCreated)
            {
                singleton.PendingLineSegmentSpawns.Dispose();
            }
            if (singleton.LineSegmentSortedStartPositions.IsCreated)
            {
                singleton.LineSegmentSortedStartPositions.Dispose();
            }
            if (singleton.LineSegmentSortedEndPositions.IsCreated)
            {
                singleton.LineSegmentSortedEndPositions.Dispose();
            }
            if (singleton.LineSegmentSortedWidths.IsCreated)
            {
                singleton.LineSegmentSortedWidths.Dispose();
            }
            if (singleton.LineSegmentBucketOffsets.IsCreated)
            {
                singleton.LineSegmentBucketOffsets.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<CombatAoeVfxDispatchSingleton> vfx = SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            ref CombatAoeVfxDispatchSingleton singleton = ref vfx.ValueRW;
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;
            LastVfxEventCount = 0;

            if (singleton.PendingCircularSpawns.Count == 0
                && singleton.PendingTimedCircularSpawns.Count == 0
                && singleton.PendingLineSegmentSpawns.Count == 0)
            {
                return;
            }

            // todo: this is not the correct way to reach the game obj.
            // use singleton entitiy with game obj binding.
            CombatVfxRoot root = CombatVfxRoot.Instance;
            if (root == null)
            {
                singleton.PendingCircularSpawns.Clear();
                singleton.PendingTimedCircularSpawns.Clear();
                singleton.PendingLineSegmentSpawns.Clear();
                return;
            }

            int dispatchedCount = 0;
            if (singleton.PendingCircularSpawns.Count > 0)
            {
                new BucketCircularVfxSpawnsJob
                {
                    Pending = singleton.PendingCircularSpawns,
                    BucketCount = root.RegisteredCountFor(VfxDataShape.Circular),
                    SortedPositions = singleton.CircularSortedPositions,
                    SortedAreaSizes = singleton.CircularSortedAreaSizes,
                    BucketOffsets = singleton.CircularBucketOffsets
                }.Run();

                dispatchedCount += root.DrainAndDispatchCircular(
                    singleton.CircularSortedPositions.AsArray(),
                    singleton.CircularSortedAreaSizes.AsArray(),
                    singleton.CircularBucketOffsets.AsArray());
            }

            if (singleton.PendingTimedCircularSpawns.Count > 0)
            {
                new BucketTimedCircularVfxSpawnsJob
                {
                    Pending = singleton.PendingTimedCircularSpawns,
                    BucketCount = root.RegisteredCountFor(VfxDataShape.TimedCircular),
                    SortedPositions = singleton.TimedCircularSortedPositions,
                    SortedAreaSizes = singleton.TimedCircularSortedAreaSizes,
                    SortedDurations = singleton.TimedCircularSortedDurations,
                    SortedTickIntervals = singleton.TimedCircularSortedTickIntervals,
                    BucketOffsets = singleton.TimedCircularBucketOffsets
                }.Run();

                dispatchedCount += root.DrainAndDispatchTimedCircular(
                    singleton.TimedCircularSortedPositions.AsArray(),
                    singleton.TimedCircularSortedAreaSizes.AsArray(),
                    singleton.TimedCircularSortedDurations.AsArray(),
                    singleton.TimedCircularSortedTickIntervals.AsArray(),
                    singleton.TimedCircularBucketOffsets.AsArray());
            }

            if (singleton.PendingLineSegmentSpawns.Count > 0)
            {
                new BucketLineSegmentVfxSpawnsJob
                {
                    Pending = singleton.PendingLineSegmentSpawns,
                    BucketCount = root.RegisteredCountFor(VfxDataShape.LineSegment),
                    SortedStartPositions = singleton.LineSegmentSortedStartPositions,
                    SortedEndPositions = singleton.LineSegmentSortedEndPositions,
                    SortedWidths = singleton.LineSegmentSortedWidths,
                    BucketOffsets = singleton.LineSegmentBucketOffsets
                }.Run();

                dispatchedCount += root.DrainAndDispatchLineSegment(
                    singleton.LineSegmentSortedStartPositions.AsArray(),
                    singleton.LineSegmentSortedEndPositions.AsArray(),
                    singleton.LineSegmentSortedWidths.AsArray(),
                    singleton.LineSegmentBucketOffsets.AsArray());
            }

            LastVfxEventCount = dispatchedCount;

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.VfxEventsCreated += LastVfxEventCount;
            }
        }

        // Buckets the shared queue by decoded local VfxId via a two-pass counting sort so the
        // root can read each graph's events as one contiguous slice.
        [BurstCompile]
        private struct BucketCircularVfxSpawnsJob : IJob
        {
            public NativeQueue<CircularVfxSpawnRequest> Pending;
            public int BucketCount;
            public NativeList<float2> SortedPositions;
            public NativeList<float> SortedAreaSizes;
            public NativeList<int> BucketOffsets;

            public void Execute()
            {
                int pendingCount = Pending.Count;
                NativeArray<CircularVfxSpawnRequest> items = Pending.ToArray(Allocator.Temp);
                Pending.Clear();

                var counts = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int i = 0; i < items.Length; i++)
                {
                    int id = VfxDataShapeTable.DecodeLocalIndex(items[i].VfxId);
                    if (id >= 1 && id <= BucketCount)
                    {
                        counts[id]++;
                    }
                }

                BucketOffsets.ResizeUninitialized(BucketCount + 2);
                int running = 0;
                for (int id = 1; id <= BucketCount; id++)
                {
                    BucketOffsets[id] = running;
                    running += counts[id];
                }
                BucketOffsets[BucketCount + 1] = running;

                var cursor = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int id = 1; id <= BucketCount; id++)
                {
                    cursor[id] = BucketOffsets[id];
                }

                SortedPositions.ResizeUninitialized(pendingCount);
                SortedAreaSizes.ResizeUninitialized(pendingCount);

                for (int i = 0; i < items.Length; i++)
                {
                    CircularVfxSpawnRequest item = items[i];
                    int id = VfxDataShapeTable.DecodeLocalIndex(item.VfxId);
                    if (id < 1 || id > BucketCount)
                    {
                        continue;
                    }

                    int dest = cursor[id];
                    cursor[id] = dest + 1;
                    SortedPositions[dest] = item.Position;
                    SortedAreaSizes[dest] = math.max(0.01f, item.AreaSize);
                }

                SortedPositions.Length = running;
                SortedAreaSizes.Length = running;

                items.Dispose();
                counts.Dispose();
                cursor.Dispose();
            }
        }

        [BurstCompile]
        private struct BucketTimedCircularVfxSpawnsJob : IJob
        {
            public NativeQueue<TimedCircularVfxSpawnRequest> Pending;
            public int BucketCount;
            public NativeList<float2> SortedPositions;
            public NativeList<float> SortedAreaSizes;
            public NativeList<float> SortedDurations;
            public NativeList<float> SortedTickIntervals;
            public NativeList<int> BucketOffsets;

            public void Execute()
            {
                int pendingCount = Pending.Count;
                NativeArray<TimedCircularVfxSpawnRequest> items = Pending.ToArray(Allocator.Temp);
                Pending.Clear();

                var counts = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int i = 0; i < items.Length; i++)
                {
                    int id = VfxDataShapeTable.DecodeLocalIndex(items[i].VfxId);
                    if (id >= 1 && id <= BucketCount)
                    {
                        counts[id]++;
                    }
                }

                BucketOffsets.ResizeUninitialized(BucketCount + 2);
                int running = 0;
                for (int id = 1; id <= BucketCount; id++)
                {
                    BucketOffsets[id] = running;
                    running += counts[id];
                }
                BucketOffsets[BucketCount + 1] = running;

                var cursor = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int id = 1; id <= BucketCount; id++)
                {
                    cursor[id] = BucketOffsets[id];
                }

                SortedPositions.ResizeUninitialized(pendingCount);
                SortedAreaSizes.ResizeUninitialized(pendingCount);
                SortedDurations.ResizeUninitialized(pendingCount);
                SortedTickIntervals.ResizeUninitialized(pendingCount);

                for (int i = 0; i < items.Length; i++)
                {
                    TimedCircularVfxSpawnRequest item = items[i];
                    int id = VfxDataShapeTable.DecodeLocalIndex(item.VfxId);
                    if (id < 1 || id > BucketCount)
                    {
                        continue;
                    }

                    int dest = cursor[id];
                    cursor[id] = dest + 1;
                    SortedPositions[dest] = item.Position;
                    SortedAreaSizes[dest] = math.max(0.01f, item.AreaSize);
                    SortedDurations[dest] = item.Duration;
                    SortedTickIntervals[dest] = item.TickInterval;
                }

                SortedPositions.Length = running;
                SortedAreaSizes.Length = running;
                SortedDurations.Length = running;
                SortedTickIntervals.Length = running;

                items.Dispose();
                counts.Dispose();
                cursor.Dispose();
            }
        }

        [BurstCompile]
        private struct BucketLineSegmentVfxSpawnsJob : IJob
        {
            public NativeQueue<LineSegmentVfxSpawn> Pending;
            public int BucketCount;
            public NativeList<float2> SortedStartPositions;
            public NativeList<float2> SortedEndPositions;
            public NativeList<float> SortedWidths;
            public NativeList<int> BucketOffsets;

            public void Execute()
            {
                int pendingCount = Pending.Count;
                NativeArray<LineSegmentVfxSpawn> items = Pending.ToArray(Allocator.Temp);
                Pending.Clear();

                var counts = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int i = 0; i < items.Length; i++)
                {
                    int id = VfxDataShapeTable.DecodeLocalIndex(items[i].VfxId);
                    if (id >= 1 && id <= BucketCount)
                    {
                        counts[id]++;
                    }
                }

                BucketOffsets.ResizeUninitialized(BucketCount + 2);
                int running = 0;
                for (int id = 1; id <= BucketCount; id++)
                {
                    BucketOffsets[id] = running;
                    running += counts[id];
                }
                BucketOffsets[BucketCount + 1] = running;

                var cursor = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int id = 1; id <= BucketCount; id++)
                {
                    cursor[id] = BucketOffsets[id];
                }

                SortedStartPositions.ResizeUninitialized(pendingCount);
                SortedEndPositions.ResizeUninitialized(pendingCount);
                SortedWidths.ResizeUninitialized(pendingCount);

                for (int i = 0; i < items.Length; i++)
                {
                    LineSegmentVfxSpawn item = items[i];
                    int id = VfxDataShapeTable.DecodeLocalIndex(item.VfxId);
                    if (id < 1 || id > BucketCount)
                    {
                        continue;
                    }

                    int dest = cursor[id];
                    cursor[id] = dest + 1;
                    SortedStartPositions[dest] = item.StartPosition;
                    SortedEndPositions[dest] = item.EndPosition;
                    SortedWidths[dest] = math.max(0.01f, item.Width);
                }

                SortedStartPositions.Length = running;
                SortedEndPositions.Length = running;
                SortedWidths.Length = running;

                items.Dispose();
                counts.Dispose();
                cursor.Dispose();
            }
        }
    }
}
