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
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Vfx
{
    // ECS Lifecycle: singleton VFX dispatch queue; created by CombatVfxDispatchSystem on
    // create, drained every presentation update, disposed by CombatVfxDispatchSystem on destroy.
    public struct CombatVfxDispatchSingleton : IComponentData
    {
        public NativeQueue<VfxPendingSpawn> PendingSpawns;
        public JobHandle ProducerHandle;
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatVfxDispatchSystem : SystemBase
    {
        internal int LastVfxEventCount;
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(CombatVfxDispatchSingleton));
            EntityManager.SetComponentData(singletonEntity, new CombatVfxDispatchSingleton
            {
                PendingSpawns = new NativeQueue<VfxPendingSpawn>(Allocator.Persistent)
            });
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<CombatVfxDispatchSingleton>(singletonEntity))
            {
                return;
            }

            CombatVfxDispatchSingleton singleton =
                EntityManager.GetComponentData<CombatVfxDispatchSingleton>(singletonEntity);
            singleton.ProducerHandle.Complete();
            if (singleton.PendingSpawns.IsCreated)
            {
                singleton.PendingSpawns.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<CombatVfxDispatchSingleton> vfx = SystemAPI.GetSingletonRW<CombatVfxDispatchSingleton>();
            ref CombatVfxDispatchSingleton singleton = ref vfx.ValueRW;
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;
            LastVfxEventCount = 0;

            if (singleton.PendingSpawns.Count == 0)
            {
                return;
            }

            CombatVfxRoot root = CombatVfxRoot.Instance;
            if (root == null)
            {
                singleton.PendingSpawns.Clear();
                return;
            }

            LastVfxEventCount = root.DrainAndDispatch(ref singleton.PendingSpawns);

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.VfxEventsCreated += LastVfxEventCount;
            }
        }
    }
}
