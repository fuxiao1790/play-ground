# 001 Component Lifecycle Model

## Goal

Make timed spawning an enableable component state so the timed vs non-timed
archetype variants collapse within the projectile and lingering-AOE domains.
Impact AOE is not touched by this task.

## Scope

- Change `TimedSpawnComponent` to implement `IEnableableComponent`.
- Keep `TimedSpawnStateComponent` as always-present plain state on projectiles
  and lingering AOEs unless profiling shows a need to make it enableable too.
- `TimedSpawnComponent` and `TimedSpawnStateComponent` remain **absent** from the
  impact AOE archetype.
- Update lifecycle comments in `CombatEcsComponents.cs`:
  - `TimedSpawnComponent`: present on projectiles and lingering AOEs, enabled only
    while emitting interval children; absent from impact AOEs. Note the
    `CombatLifetimeComponent`-must-be-enabled invariant.
  - `CombatLifetimeComponent`: present+enabled on projectiles and lingering AOEs,
    absent from impact AOEs; presence is the impact-vs-lingering discriminator.
- Define reset rules (applied by the domain apply systems in 002/003):
  - Projectile with timed child: set `TimedSpawnComponent`, reset
    `TimedSpawnStateComponent`, enable `TimedSpawnComponent`.
  - Projectile without timed child: set `TimedSpawnComponent` to a safe inert
    value, reset state, disable `TimedSpawnComponent`.
  - Lingering AOE with timed child: same as projectile-with-timed-child, and
    `CombatLifetimeComponent` is enabled.
  - Lingering AOE without timed child: disable `TimedSpawnComponent`;
    `CombatLifetimeComponent` is enabled.
  - Impact AOE: no timed-spawn or lifetime components exist to set.
  - Finite lifetime (projectile / lingering AOE): set
    `CombatLifetimeComponent.Remaining`, enable `CombatLifetimeComponent`.

## Acceptance Criteria

- `TimedSpawnComponent` enabled state is the only runtime timed-spawn selector.
- `TimedSpawnTag` is no longer needed for simulation filtering.
- Impact AOE archetype is unchanged (no timed-spawn, no lifetime).
- The `enable timed spawn ⇒ enable lifetime` invariant is documented and never
  violated by a reset path.
- ECS lifecycle comments describe per-archetype presence and enable/disable ownership.

## Dependencies

None.

## Complexity

Small.
