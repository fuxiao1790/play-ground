# 003 AOE Apply Collapse

## Goal

Collapse impact, lingering, and timed-spawner lingering AOE pools into one AOE
archetype, one AOE apply path, one disabled-slot query, and one scheduled reuse
job.

## Scope

- Replace `impactArchetype`, `lingeringArchetype`, and `timedSpawnerLingeringArchetype` with one AOE archetype containing:
  - existing impact/base AOE components
  - `CombatLifetimeComponent`
  - `AoePulseVfxComponent`
  - `AoeContactGateElement`
  - `TimedSpawnComponent`
  - `TimedSpawnStateComponent`
- Remove `AoeSpawnKey` bucketing by lifetime/timed-spawner. With one AOE
  archetype, command grouping by impact/lingering/timed slot kind is obsolete.
- Delete per-key disabled-slot query caching. AOE apply owns one disabled-slot
  query for the domain.
- Reuse query becomes `AoeTag` plus disabled `Active`.
- The reuse query must be the only AOE slot-category filter:
  - use `WithAll<AoeTag>()`
  - use `WithDisabled<Active>()`
  - do not use `WithAll<CombatLifetimeComponent>()`, `WithNone<CombatLifetimeComponent>()`, `WithAll<TimedSpawnTag>()`, `WithNone<TimedSpawnTag>()`, or exact archetype matching to split impact/lingering/timed pools.
- Reuse job always has lifetime, pulse VFX, contact gate, and timed-spawn handles.
- Schedule exactly one AOE reuse job per frame when there are AOE commands. The
  job runs against the single AOE disabled-slot query.
- Reuse job must not iterate a broader AOE chunk set and branch per entity to
  decide impact vs lingering vs timed eligibility. The command decides which
  enableable states are set on the claimed slot.
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
- Cold-create path creates only the unified AOE archetype and sets lifetime,
  pulse VFX, contact gate, and timed-spawn enableable states explicitly from the
  command.

## Acceptance Criteria

- A disabled impact AOE slot can be reused by lingering or timed lingering AOE.
- A disabled lingering/timed AOE slot can be reused by impact AOE.
- AOE apply has one reuse query and one reuse job; there is no separate impact,
  lingering, or timed-spawner reuse job.
- AOE apply no longer maintains bucket/query maps keyed by lifetime,
  timed-spawner, impact, lingering, or exact archetype.
- No AOE apply query uses presence/absence of `CombatLifetimeComponent` or `TimedSpawnTag` to choose pool.
- No AOE reuse job contains impact-vs-lingering-vs-timed eligibility checks such as
  `if (hasLifetime != commandNeedsLifetime) continue;`.
- Disabled `Active` and `AoeTag` are enforced by query construction, not by
  manually scanning chunks and rejecting entities in the job.
- Impact AOE has disabled lifetime, inactive pulse VFX, cleared gates, and disabled timed spawn after reset.

## Dependencies

Depends on 001.

## Complexity

Medium.
