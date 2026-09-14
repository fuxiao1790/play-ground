# 004 - Expand Aimed Projectile Waves

## Change

Extend `ProjectileSpawnExpansionSystem` job inputs with optional common target
snapshot from `TargetSpatialHashSingleton`. For each resolved event/template:

1. Stamp existing per-instance frame.
2. If mode is `NearestHostile`, faction valid, range positive, and hash available,
   call `CombatTargetAcquisition.TrySelectNthNearest` with rank `0` and
   `ContactGateSeedTargetId` excluded.
3. If target found and target-center direction non-zero, normalize target minus
   event position into one local aimed direction.
4. On successful acquisition, build full wave as radial nova oriented from aimed
   direction: `Rotate(aimedDirection, 360 * i / count) * command.Speed`.
5. Shot index `0` has zero angular offset and points directly at target. Every other
   shot occupies remaining equal full-circle nova slot.
6. Preserve existing IDs, render data, child state, collision mode, payload, count,
   and lane routing. Successful aimed nova does not use normal side-spray/forward
   spread or jitter.
7. Route each finished command through existing discrete/continuous lists.
8. If any precondition/acquisition fails, write every existing pattern velocity
   unchanged.

Acquisition occurs once per event, not per shot. It rotates whole radial pattern;
it does not add projectile count, seed tracking target, store target entity, or
steer after spawn.

When target hash exists, combine expansion dependency with `BuildHandle`. After
scheduling read, combine expansion handle into hash `ConsumerHandle`. Missing hash
singleton remains supported for stripped test worlds and disabled/fallback paths.

## Acceptance Criteria

- Enabled trigger wave uses nearest-hostile direction as radial nova angular origin.
- Shot index `0` uses exact normalized target-center direction times authored speed.
- Shot `i` uses `360 * i / count` degree offset from aimed direction.
- Remaining shots form equal full-circle nova around aimed slot; count `1` produces
  only direct shot.
- Successful aimed nova bypasses normal forward/side-spray spread and jitter.
- Wave count and deterministic IDs unchanged.
- Disabled/no-target/missing-hash/zero-range/coincident-target path preserves
  existing direction and pattern exactly.
- Same-faction targets skipped.
- Contact-gated seed target skipped; next nearest valid hostile selected.
- Discrete and continuous waves receive same aim-oriented nova before lane split.
- Continuous projectile remains without tracking component and flies straight.
- Discrete homing behavior unchanged; if separately enabled, existing tracking may
  steer after aimed launch.
- No new entity state, system, structural change, managed read, or allocation.
- Hash build/consumer handles correctly protect persistent containers.

## Dependencies

Depends on `001-refactor-combat-target-acquisition.md` and
`003-compile-and-template-launch-aim.md`.

## Estimated Scope

Medium: expansion inputs, dependency publication, one acquisition branch, fallback
preservation.
