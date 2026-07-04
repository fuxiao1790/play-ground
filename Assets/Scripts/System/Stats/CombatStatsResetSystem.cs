using Unity.Entities;

namespace PlayGround.System.Stats
{
    // Zeroes the shared stats blackboard at the start of every frame, before any producer
    // writes to it. Producers accumulate their per-frame contributions into
    // CombatStatsSingleton during simulation/presentation; CombatStatsGatherSystem reads the
    // built-up snapshot at the end of the frame. Running here (InitializationSystemGroup)
    // guarantees the reset happens before those producers each frame.
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class CombatStatsResetSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW = default;
            }
        }
    }
}
