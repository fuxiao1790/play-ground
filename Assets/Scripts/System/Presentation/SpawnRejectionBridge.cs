using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targets;
using Unity.Entities;

namespace PlayGround.System.Combat.Presentation
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CombatBatchedRenderSystem))]
    public partial class SpawnRejectionBridge : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingletonRW<SpawnRejectedSingleton>(out RefRW<SpawnRejectedSingleton> lane))
            {
                return;
            }

            lane.ValueRW.ProducerHandle.Complete();
            lane.ValueRW.ProducerHandle = default;
            for (int i = 0; i < lane.ValueRO.Events.Length; i++)
            {
                SpawnRejectedEvent rejection = lane.ValueRO.Events[i];
                if (rejection.Caster == Entity.Null
                    || !EntityManager.Exists(rejection.Caster)
                    || !EntityManager.HasComponent<TargetCompanion>(rejection.Caster))
                {
                    continue;
                }

                TargetCompanion companion = EntityManager.GetComponentObject<TargetCompanion>(rejection.Caster);
                companion?.Target?.ReceiveSpawnRejected(rejection.CastToken);
            }

            lane.ValueRW.Events.Clear();
        }
    }
}
