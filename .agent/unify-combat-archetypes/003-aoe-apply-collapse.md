# 003 AOE Apply Collapse

## Goal

Collapse impact, lingering, and timed-spawner lingering AOE pools into one AOE archetype and one reuse path.

## Scope

- Replace `impactArchetype`, `lingeringArchetype`, and `timedSpawnerLingeringArchetype` with one AOE archetype containing:
  - existing impact/base AOE components
  - `CombatLifetimeComponent`
  - `AoePulseVfxComponent`
  - `AoeContactGateElement`
  - `TimedSpawnComponent`
  - `TimedSpawnStateComponent`
- Remove `AoeSpawnKey` bucketing by lifetime/timed-spawner unless kept only as a temporary command-order helper.
- Reuse query becomes `AoeTag` plus disabled `Active`.
- Reuse job always has lifetime, pulse VFX, contact gate, and timed-spawn handles.
- For impact/pulse AOE:
  - disable `CombatLifetimeComponent`
  - clear contact gates
  - set pulse VFX to inert values
  - disable `TimedSpawnComponent`
- For lingering AOE:
  - enable `CombatLifetimeComponent`
  - set pulse VFX interval
  - clear contact gates
- For timed lingering AOE:
  - enable both `CombatLifetimeComponent` and `TimedSpawnComponent`
  - reset timed-spawn state.

## Acceptance Criteria

- A disabled impact AOE slot can be reused by lingering or timed lingering AOE.
- A disabled lingering/timed AOE slot can be reused by impact AOE.
- No AOE apply query uses presence/absence of `CombatLifetimeComponent` or `TimedSpawnTag` to choose pool.
- Impact AOE has disabled lifetime, inactive pulse VFX, cleared gates, and disabled timed spawn after reset.

## Dependencies

Depends on 001.

## Complexity

Medium.

