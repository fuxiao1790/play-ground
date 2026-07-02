# 001 Component Lifecycle Model

## Goal

Make timed spawning and lifetime optional behavior use enableable component state on stable archetypes.

## Scope

- Change `TimedSpawnComponent` to implement `IEnableableComponent`.
- Keep `TimedSpawnStateComponent` as always-present plain state unless profiling shows a need to make it enableable too.
- Update lifecycle comments in `CombatEcsComponents.cs`.
- Define reset rules:
  - Spawn with timed child: set `TimedSpawnComponent`, set `TimedSpawnStateComponent`, enable `TimedSpawnComponent`.
  - Spawn without timed child: set `TimedSpawnComponent` to default or safe inert value, reset state, disable `TimedSpawnComponent`.
  - Spawn with finite lifetime: set `CombatLifetimeComponent.Remaining`, enable `CombatLifetimeComponent`.
  - Spawn without finite lifetime: set remaining to `0`, disable `CombatLifetimeComponent`.

## Acceptance Criteria

- `TimedSpawnComponent` is the only runtime timed-spawn enable bit.
- `TimedSpawnTag` is no longer needed for simulation filtering.
- ECS lifecycle comments mention always-present base archetype data and enable/disable ownership.

## Dependencies

None.

## Complexity

Small.

