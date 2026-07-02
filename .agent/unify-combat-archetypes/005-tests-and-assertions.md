# 005 Tests And Assertions

## Goal

Update tests to assert enableable-state behavior for timed spawn instead of
archetype/tag presence, while keeping impact vs lingering as separate archetypes.

## Scope

- Projectile spawn pipeline tests (`ProjectileSpawnPipelineTests.cs`):
  - One projectile archetype expected instead of two.
  - Replace `ComponentType.Exclude<TimedSpawnTag>()` and
    `ComponentType.ReadOnly<TimedSpawnTag>()` pool/count filters (lines ~191, 542,
    551) with `IsComponentEnabled<TimedSpawnComponent>()` checks.
  - Replace `typeof(TimedSpawnTag)` in test archetype/query construction
    (lines ~435, 480) with the unified archetype; timed vs non-timed is an enabled
    bit, not a distinct archetype.
  - Non-timed projectiles have `TimedSpawnComponent` present but disabled.
- Prototype/playmode checks (`BareMinimumPrototypePlayModeTests.cs`, lines ~890,
  901): replace `HasComponent<TimedSpawnTag>()` with
  `IsComponentEnabled<TimedSpawnComponent>()`.
- AOE tests (`AoeSimulationTests.cs`):
  - Impact AOEs still have **no** `CombatLifetimeComponent` (unchanged assertion).
  - Lingering AOEs have lifetime present and enabled.
  - Timed lingering AOEs have `TimedSpawnComponent` present and enabled.
  - Non-timed lingering AOEs have `TimedSpawnComponent` present but disabled.
  - Update any assertion expecting a distinct timed-spawner lingering archetype.
- Reuse-crossing coverage (within a pool only):
  - basic projectile slot reused by timed projectile, and the reverse
  - non-timed lingering slot reused by timed lingering, and the reverse
  - assert impact and lingering pools stay disjoint: a lingering command does not
    claim a disabled impact slot, and an impact command does not claim a disabled
    lingering slot.

## Acceptance Criteria

- Tests no longer assert presence/absence of `TimedSpawnTag`.
- Tests still assert impact AOEs lack `CombatLifetimeComponent`.
- Tests assert timed vs non-timed via `IsComponentEnabled<TimedSpawnComponent>()`.
- Archetype-count assertions reflect three archetypes total (projectile, impact,
  lingering) and one projectile / one lingering archetype (not two each).
- Existing collision, tracking, timed-spawn, and lifetime tests pass.

## Dependencies

Depends on 001 through 004.

## Complexity

Medium.
