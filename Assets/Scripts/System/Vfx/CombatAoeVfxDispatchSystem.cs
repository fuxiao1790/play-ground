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
        public NativeQueue<VfxSpawnRequest> PendingBasicSpawns;
        public NativeList<float2> BasicSortedPositions;
        public NativeList<float> BasicSortedAreaSizes;
        public NativeList<int> BasicBucketOffsets;
        public NativeQueue<TimedVfxSpawnRequest> PendingTimedSpawns;
        public NativeList<float2> TimedSortedPositions;
        public NativeList<float> TimedSortedAreaSizes;
        public NativeList<float> TimedSortedDurations;
        public NativeList<float> TimedSortedTickIntervals;
        public NativeList<int> TimedBucketOffsets;
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
                PendingBasicSpawns = new NativeQueue<VfxSpawnRequest>(Allocator.Persistent),
                BasicSortedPositions = new NativeList<float2>(Allocator.Persistent),
                BasicSortedAreaSizes = new NativeList<float>(Allocator.Persistent),
                BasicBucketOffsets = new NativeList<int>(Allocator.Persistent),
                PendingTimedSpawns = new NativeQueue<TimedVfxSpawnRequest>(Allocator.Persistent),
                TimedSortedPositions = new NativeList<float2>(Allocator.Persistent),
                TimedSortedAreaSizes = new NativeList<float>(Allocator.Persistent),
                TimedSortedDurations = new NativeList<float>(Allocator.Persistent),
                TimedSortedTickIntervals = new NativeList<float>(Allocator.Persistent),
                TimedBucketOffsets = new NativeList<int>(Allocator.Persistent)
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
            if (singleton.PendingBasicSpawns.IsCreated)
            {
                singleton.PendingBasicSpawns.Dispose();
            }
            if (singleton.BasicSortedPositions.IsCreated)
            {
                singleton.BasicSortedPositions.Dispose();
            }
            if (singleton.BasicSortedAreaSizes.IsCreated)
            {
                singleton.BasicSortedAreaSizes.Dispose();
            }
            if (singleton.BasicBucketOffsets.IsCreated)
            {
                singleton.BasicBucketOffsets.Dispose();
            }
            if (singleton.PendingTimedSpawns.IsCreated)
            {
                singleton.PendingTimedSpawns.Dispose();
            }
            if (singleton.TimedSortedPositions.IsCreated)
            {
                singleton.TimedSortedPositions.Dispose();
            }
            if (singleton.TimedSortedAreaSizes.IsCreated)
            {
                singleton.TimedSortedAreaSizes.Dispose();
            }
            if (singleton.TimedSortedDurations.IsCreated)
            {
                singleton.TimedSortedDurations.Dispose();
            }
            if (singleton.TimedSortedTickIntervals.IsCreated)
            {
                singleton.TimedSortedTickIntervals.Dispose();
            }
            if (singleton.TimedBucketOffsets.IsCreated)
            {
                singleton.TimedBucketOffsets.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<CombatAoeVfxDispatchSingleton> vfx = SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            ref CombatAoeVfxDispatchSingleton singleton = ref vfx.ValueRW;
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;
            LastVfxEventCount = 0;

            if (singleton.PendingBasicSpawns.Count == 0 && singleton.PendingTimedSpawns.Count == 0)
            {
                return;
            }

            // todo: this is not the correct way to reach the game obj.
            // use singleton entitiy with game obj binding.
            CombatVfxRoot root = CombatVfxRoot.Instance;
            if (root == null)
            {
                singleton.PendingBasicSpawns.Clear();
                singleton.PendingTimedSpawns.Clear();
                return;
            }

            int dispatchedCount = 0;
            if (singleton.PendingBasicSpawns.Count > 0)
            {
                new BucketBasicVfxSpawnsJob
                {
                    Pending = singleton.PendingBasicSpawns,
                    BucketCount = root.RegisteredCountFor(VfxDataShape.Basic),
                    SortedPositions = singleton.BasicSortedPositions,
                    SortedAreaSizes = singleton.BasicSortedAreaSizes,
                    BucketOffsets = singleton.BasicBucketOffsets
                }.Run();

                dispatchedCount += root.DrainAndDispatchBasic(
                    singleton.BasicSortedPositions.AsArray(),
                    singleton.BasicSortedAreaSizes.AsArray(),
                    singleton.BasicBucketOffsets.AsArray());
            }

            if (singleton.PendingTimedSpawns.Count > 0)
            {
                new BucketTimedVfxSpawnsJob
                {
                    Pending = singleton.PendingTimedSpawns,
                    BucketCount = root.RegisteredCountFor(VfxDataShape.Timed),
                    SortedPositions = singleton.TimedSortedPositions,
                    SortedAreaSizes = singleton.TimedSortedAreaSizes,
                    SortedDurations = singleton.TimedSortedDurations,
                    SortedTickIntervals = singleton.TimedSortedTickIntervals,
                    BucketOffsets = singleton.TimedBucketOffsets
                }.Run();

                dispatchedCount += root.DrainAndDispatchTimed(
                    singleton.TimedSortedPositions.AsArray(),
                    singleton.TimedSortedAreaSizes.AsArray(),
                    singleton.TimedSortedDurations.AsArray(),
                    singleton.TimedSortedTickIntervals.AsArray(),
                    singleton.TimedBucketOffsets.AsArray());
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
        private struct BucketBasicVfxSpawnsJob : IJob
        {
            public NativeQueue<VfxSpawnRequest> Pending;
            public int BucketCount;
            public NativeList<float2> SortedPositions;
            public NativeList<float> SortedAreaSizes;
            public NativeList<int> BucketOffsets;

            public void Execute()
            {
                int pendingCount = Pending.Count;
                NativeArray<VfxSpawnRequest> items = Pending.ToArray(Allocator.Temp);
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
                    VfxSpawnRequest item = items[i];
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
        private struct BucketTimedVfxSpawnsJob : IJob
        {
            public NativeQueue<TimedVfxSpawnRequest> Pending;
            public int BucketCount;
            public NativeList<float2> SortedPositions;
            public NativeList<float> SortedAreaSizes;
            public NativeList<float> SortedDurations;
            public NativeList<float> SortedTickIntervals;
            public NativeList<int> BucketOffsets;

            public void Execute()
            {
                int pendingCount = Pending.Count;
                NativeArray<TimedVfxSpawnRequest> items = Pending.ToArray(Allocator.Temp);
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
                    TimedVfxSpawnRequest item = items[i];
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
    }
}
