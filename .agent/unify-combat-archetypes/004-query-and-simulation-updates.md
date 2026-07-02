# 004 Query And Simulation Updates

## Goal

Update all simulation systems that currently interpret component presence as behavior.

## Scope

- `TimedSpawnSystem`
  - Replace `[WithAll(... TimedSpawnTag)]` with enabled `TimedSpawnComponent`.
  - Keep `Active` and enabled `CombatLifetimeComponent` filtering.
  - Use query attributes or `EntityQueryBuilder` enabled-component filters so the
    job is scheduled only for active lifetime entities with enabled timed spawn;
    do not schedule all lifetime entities and branch on `TimedSpawnComponent`
    enabled state inside `Execute`.
  - Continue to guard against `CombatFaction.None`.
- `ImpactAoeCollisionSystem`
  - Replace `WithNone<CombatLifetimeComponent>` with disabled lifetime selection.
  - Use query-level disabled lifetime selection; do not process all AOEs and skip
    lingering ones with an `if` inside the collision job.
  - Keep impact behavior as one-shot collision that deactivates after pass.
- `LingeringAoeCollisionSystem`
  - Keep lifetime-present access but process enabled and disabled lifetime according to intended pulse/lingering behavior.
  - Ensure it does not double-process impact AOEs if impact system owns disabled-lifetime AOEs. If one collision system handles both, remove duplicate impact path.
  - If lingering remains a separate system, use query-level enabled lifetime
    selection; do not schedule disabled-lifetime impact AOEs and reject them in
    the job body.
- `AoePulseVfxSystem`
  - Continue to skip when lifetime is disabled or pulse interval is inert.
- `CombatLifetimeSystem`
  - Confirm enabled `CombatLifetimeComponent` query skips impact AOEs and non-lifetime slots.
- Projectile tracking/collision
  - Confirm all projectiles still have enabled lifetime on spawn.

## Acceptance Criteria

- No simulation behavior depends on `HasComponent<TimedSpawnTag>()`.
- No AOE behavior depends on missing `CombatLifetimeComponent`.
- No simulation job uses per-entity `if` checks as the primary selection mechanism
  for timed-spawn enabled/disabled or lifetime enabled/disabled categories.
- Impact AOEs collide once.
- Lingering AOEs tick lifetime, repeat gates, pulse VFX, and expire.
- Timed spawners emit children only when `TimedSpawnComponent` is enabled.

## Dependencies

Depends on 001, 002, and 003.

## Complexity

Medium.
