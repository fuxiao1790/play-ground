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
using Unity.Entities;

namespace PlayGround.System.Combat.Stats
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
