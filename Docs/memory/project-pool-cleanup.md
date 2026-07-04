# Combat Pool Cleanup

## Context

Projectile and AOE entities use disable-in-place pooling. Expired entities keep
their archetype and reusable component storage, with `Active` disabled, so later
spawns can reuse them without hot-path structural churn.

Without cleanup, the world retains the highest historical projectile/AOE entity
count. Heavy scenes can leave behind large disabled pools, increasing idle scans
and retained chunk memory after combat calms down.

## Decision

`CombatPoolCleanupSystem` trims disabled combat pools at the end of simulation
only when frame headroom exists. It uses:

- smoothed frame-time and current-frame elapsed gates
- per-pool retention and active-to-disabled ratio floors
- per-pool and per-frame delete caps

The trimmer evaluates the current reuse pools on master: projectiles, impact
AOEs, and lingering AOEs. It does not key cleanup by `CombatRenderKindId`
because spawn reuse can now claim any disabled slot in the matching reuse pool
and overwrites render data on reset.

## Follow-up

Profile with the Entities Structural Changes module after stress scenes. Tune
the retention target, ratio, and caps only from measured idle-frame recovery and
combat-spike behavior.
