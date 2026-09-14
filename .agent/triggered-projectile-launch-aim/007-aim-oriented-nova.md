# 007 - Aim-Oriented Nova

## Context

Tasks `001`-`006` implemented earlier requirement by replacing only shot index `0`
velocity. User superseded that behavior: nova spread itself must be calculated from
aimed direction.

## Change

In `ProjectileSpawnExpansionSystem`, keep existing nearest-hostile acquisition and
hash synchronization. Replace successful-acquisition pattern handling with one
aim-oriented radial wave:

```text
direction(i) = Rotate(aimDirection, 360 degrees * i / count)
velocity(i) = direction(i) * Speed
```

- `i = 0` points directly at acquired target.
- `i = 1..count-1` form remaining equal nova slots.
- `count = 1` produces direct shot only.
- Successful acquisition ignores stored forward/side-spray/radial pattern and
  spread/jitter because aimed result is explicitly a nova.
- Failed/disabled acquisition executes existing pattern switch unchanged.

Remove obsolete `hasAim`/`aimedVelocity` shot-replacement parameters from normal
pattern functions. Pass normalized aimed direction only into aimed radial path.
Reuse/generalize existing rotate and radial helpers; do not add second fan-out
system or entity state.

## Acceptance Criteria

- Exactly radial slot `0` has zero offset from acquired direction.
- Other slots are spaced by exactly `360/count` around acquired direction.
- Whole nova rotates when target direction rotates.
- Projectile count, IDs, render Z, payload, timed child state, and lane routing stay
  unchanged.
- Discrete and continuous lanes receive identical aimed nova behavior.
- No target/disabled/missing hash/invalid faction/non-positive range/coincident
  target retains existing normal pattern byte-for-byte.
- No homing state, target entity state, component, archetype, pool, structural
  change, or allocation added.

## Dependencies

Depends on completed tasks `001`-`006`.

## Estimated Scope

Small production delta concentrated in projectile expansion pattern selection.
