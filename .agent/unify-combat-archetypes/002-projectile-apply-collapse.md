# 002 Projectile Apply Collapse

## Goal

Collapse basic and timed-spawner projectile pools into one projectile archetype and one reuse path.

## Scope

- Replace the two projectile archetypes with one archetype containing:
  - existing basic projectile components
  - `TimedSpawnComponent`
  - `TimedSpawnStateComponent`
- Merge `BasicProjectileSpawnApplySystem` and `ChildSpawnerProjectileSpawnApplySystem` behavior, or keep only thin compatibility wrappers if tests require named systems during migration.
- Drain both current projectile command containers or first collapse expansion output into one command container.
- Reuse query becomes `ProjectileTag` plus disabled `Active`, with no `TimedSpawnTag` include/exclude.
- Reuse job writes common projectile data for every command and sets timed-spawn data/enabled state from `cmd.HasTimedSpawner` or equivalent.
- Cold-create path creates only the unified projectile archetype and sets enableable states explicitly.

## Acceptance Criteria

- A disabled basic projectile slot can be reused by a timed-spawner projectile.
- A disabled timed-spawner projectile slot can be reused by a non-timed projectile.
- No projectile apply query uses `TimedSpawnTag`.
- Cold-create counter still reports overflow entity creation.
- Basic projectile behavior, tracking, collision, render, and contact gates still reset correctly.

## Dependencies

Depends on 001.

## Complexity

Medium.

