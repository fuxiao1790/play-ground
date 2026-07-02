# 005 Tests And Assertions

## Goal

Update tests to assert enableable-state behavior instead of archetype presence.

## Scope

- Update projectile spawn pipeline tests:
  - one projectile archetype expected
  - timed spawn checked by `IsComponentEnabled<TimedSpawnComponent>()`
  - non-timed projectiles still have `TimedSpawnComponent` but disabled.
- Update AOE tests:
  - impact AOEs have `CombatLifetimeComponent` present but disabled
  - lingering AOEs have lifetime present and enabled
  - timed lingering AOEs have `TimedSpawnComponent` present and enabled
  - non-timed AOEs have `TimedSpawnComponent` present but disabled.
- Update prototype/playmode checks that currently use `HasComponent<TimedSpawnTag>()`.
- Add reuse-crossing coverage:
  - basic projectile slot reused by timed projectile
  - timed projectile slot reused by basic projectile
  - impact AOE slot reused by lingering AOE
  - lingering/timed AOE slot reused by impact AOE.

## Acceptance Criteria

- Tests no longer assert absence of `CombatLifetimeComponent` for impact AOEs.
- Tests no longer assert presence/absence of `TimedSpawnTag`.
- Existing collision, tracking, timed-spawn, and lifetime tests pass.

## Dependencies

Depends on 001 through 004.

## Complexity

Medium.

