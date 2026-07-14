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
    // ECS Lifecycle: singleton VFX dispatch queue; created by CombatAoeVfxDispatchSystem on
    // create, drained every presentation update, disposed by CombatAoeVfxDispatchSystem on destroy.
    public struct CombatAoeVfxDispatchSingleton : IComponentData
    {
        public NativeQueue<AoeVfxSpawnRequest> PendingAoeSpawns;
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
                PendingAoeSpawns = new NativeQueue<AoeVfxSpawnRequest>(Allocator.Persistent)
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

            CombatVfxRoot root = CombatVfxRoot.Instance;
            if (root == null)
            {
                singleton.PendingAoeSpawns.Clear();
                return;
            }

            LastVfxEventCount = root.DrainAndDispatch(ref singleton.PendingAoeSpawns);

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.VfxEventsCreated += LastVfxEventCount;
            }
        }
    }
}
