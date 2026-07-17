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
    // SortedPositions/SortedAreaSizes/BucketOffsets are grow-only scratch reused every frame by
    // BucketAoeVfxSpawnsJob - never reallocated just to shrink, mirroring
    // AoeVfxTypeResources.EnsureBufferCapacity's GraphicsBuffer growth pattern.
    public struct CombatAoeVfxDispatchSingleton : IComponentData
    {
        public NativeQueue<AoeVfxSpawnRequest> PendingAoeSpawns;
        public NativeList<float2> SortedPositions;
        public NativeList<float> SortedAreaSizes;
        public NativeList<int> BucketOffsets;
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
                PendingAoeSpawns = new NativeQueue<AoeVfxSpawnRequest>(Allocator.Persistent),
                SortedPositions = new NativeList<float2>(Allocator.Persistent),
                SortedAreaSizes = new NativeList<float>(Allocator.Persistent),
                BucketOffsets = new NativeList<int>(Allocator.Persistent)
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
            if (singleton.PendingAoeSpawns.IsCreated)
            {
                singleton.PendingAoeSpawns.Dispose();
            }
            if (singleton.SortedPositions.IsCreated)
            {
                singleton.SortedPositions.Dispose();
            }
            if (singleton.SortedAreaSizes.IsCreated)
            {
                singleton.SortedAreaSizes.Dispose();
            }
            if (singleton.BucketOffsets.IsCreated)
            {
                singleton.BucketOffsets.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<CombatAoeVfxDispatchSingleton> vfx = SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            ref CombatAoeVfxDispatchSingleton singleton = ref vfx.ValueRW;
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;
            LastVfxEventCount = 0;

            if (singleton.PendingAoeSpawns.Count == 0)
            {
                return;
            }

            // todo: this is not the correct way to reach the game obj.
            // use singleton entitiy with game obj binding.
            CombatVfxRoot root = CombatVfxRoot.Instance;
            if (root == null)
            {
                singleton.PendingAoeSpawns.Clear();
                return;
            }

            new BucketAoeVfxSpawnsJob
            {
                Pending = singleton.PendingAoeSpawns,
                BucketCount = root.RegisteredVfxCount,
                SortedPositions = singleton.SortedPositions,
                SortedAreaSizes = singleton.SortedAreaSizes,
                BucketOffsets = singleton.BucketOffsets
            }.Run();

            LastVfxEventCount = root.DrainAndDispatch(
                singleton.SortedPositions.AsArray(),
                singleton.SortedAreaSizes.AsArray(),
                singleton.BucketOffsets.AsArray());

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.VfxEventsCreated += LastVfxEventCount;
            }
        }

        // Buckets the shared queue by VfxId via a two-pass counting sort so DrainAndDispatch can
        // read each VFX type's events as one contiguous slice instead of dequeuing item by item.
        [BurstCompile]
        private struct BucketAoeVfxSpawnsJob : IJob
        {
            public NativeQueue<AoeVfxSpawnRequest> Pending;
            public int BucketCount;
            public NativeList<float2> SortedPositions;
            public NativeList<float> SortedAreaSizes;
            public NativeList<int> BucketOffsets;

            public void Execute()
            {
                int pendingCount = Pending.Count;
                NativeArray<AoeVfxSpawnRequest> items = Pending.ToArray(Allocator.Temp);
                Pending.Clear();

                var counts = new NativeArray<int>(BucketCount + 1, Allocator.Temp);
                for (int i = 0; i < items.Length; i++)
                {
                    int id = items[i].VfxId;
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
                    AoeVfxSpawnRequest item = items[i];
                    int id = item.VfxId;
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
    }
}
