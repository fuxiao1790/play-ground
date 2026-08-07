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
    // Zeroes the per-frame accumulators in the shared stats blackboard at the start of every
    // frame, before any producer writes to it. Producers accumulate their per-frame
    // contributions into CombatStatsSingleton during simulation/presentation;
    // CombatStatsGatherSystem reads the built-up snapshot at the end of the frame. Running here
    // (InitializationSystemGroup) guarantees the reset happens before those producers each frame.
    //
    // ActiveProjectiles/ActiveAoes/ActiveTargeted are deliberately preserved: they are level stats, not
    // accumulators. Gather overwrites them each Presentation, and CombatPoolCleanupSystem reads
    // them mid-frame (LateSimulation) as last frame's values for its calm-down gate — zeroing
    // them here would hand that gate a phantom activeLoad of 0 every frame.
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class CombatStatsResetSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW = new CombatStatsSingleton
                {
                    ActiveProjectiles = stats.ValueRO.ActiveProjectiles,
                    ActiveAoes = stats.ValueRO.ActiveAoes,
                    ActiveTargeted = stats.ValueRO.ActiveTargeted,
                    TargetedLinkCounts = stats.ValueRO.TargetedLinkCounts,
                    TargetedLinkProducerHandle = stats.ValueRO.TargetedLinkProducerHandle
                };
            }
        }
    }
}
