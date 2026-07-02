# 003 AOE Apply Split And Lingering Collapse

## Goal

Split the single `AoeSpawnApplySystem` into two domain apply systems —
`ImpactAoeSpawnApplySystem` and `LingeringAoeSpawnApplySystem` — and collapse the
lingering and timed-spawner lingering archetypes into one lingering archetype via
enableable `TimedSpawnComponent`. Impact AOE stays its own lean archetype and its
own apply path.

## Current State Notes

- `AoeSpawnApplySystem` owns three archetypes (`impactArchetype`,
  `lingeringArchetype`, `timedSpawnerLingeringArchetype`) and buckets drained
  commands by `AoeSpawnKey(lifetime > 0, hasTimedSpawner)` into per-key queries
  and a single `AoeSpawnJob` parameterized by `HasLingeringComponents` /
  `HasTimedSpawner`.
- `AoeSpawnExpansionSystem` writes all commands into one `PendingCommands`
  `NativeStream`.
- On expiry, `CombatLifetimeSystem` disables `Active` but leaves
  `CombatLifetimeComponent` enabled, so lingering dead slots keep present+enabled
  lifetime. Impact AOEs never have the component.

## Scope

### Archetypes

- Keep the impact archetype exactly as today (no lifetime, no pulse VFX, no
  contact-gate buffer, no timed-spawn).
- Replace `lingeringArchetype` and `timedSpawnerLingeringArchetype` with one
  `LingeringAoe` archetype containing the current lingering components plus
  `TimedSpawnComponent` and `TimedSpawnStateComponent`.

### Expansion routing

- Route AOE commands into two containers by `cmd.Lifetime > 0f`: an impact
  container and a lingering container (timed and non-timed lingering both go to
  the lingering container). Mirror `ProjectileSpawnExpansionSystem`'s two-container
  shape, or keep the `NativeStream` and split on drain — routing in expansion is
  preferred so each apply system does not scan the other domain's commands.
- Repoint expansion `[UpdateBefore(AoeSpawnApplySystem)]` to both new apply systems.

### Impact apply (`ImpactAoeSpawnApplySystem`)

- One archetype, one reuse job, one cold path.
- Reuse query: `WithAll<AoeTag>()`, `WithDisabled<Active>()`,
  `WithNone<CombatLifetimeComponent>()`. This selects only impact slots and can
  never claim a lingering slot.
- Reset writes the current impact fields only (identity, kinematics, collision,
  hit gate, hit spawn, area, render). No lifetime/pulse/gate/timed handles.

### Lingering apply (`LingeringAoeSpawnApplySystem`)

- One archetype, one reuse job, one cold path.
- Reuse query: `WithAll<AoeTag>()`, `WithDisabled<Active>()`,
  `WithAll<CombatLifetimeComponent>()` (present + enabled; lingering dead slots
  keep enabled lifetime). This selects only lingering slots and can never claim
  an impact slot.
- Reset always has lifetime, pulse VFX, contact gate, and timed-spawn handles:
  - enable `CombatLifetimeComponent`, set `Remaining`
  - set pulse VFX interval, clear contact gates
  - if `HasTimedSpawner(cmd)`: set `TimedSpawnComponent`, reset
    `TimedSpawnStateComponent`, enable `TimedSpawnComponent`
  - else: disable `TimedSpawnComponent`
- The reuse job sets timed-spawn enabled state per command; it must not branch on
  timed vs non-timed eligibility as a slot filter. It may skip already-active
  slots for bounded consumption.

### Removed machinery

- Delete `AoeSpawnKey`, `AoeSpawnBucket`, the bucket pool, `_byKey`,
  `_deadSlotQueriesByKey`, and the `HasLingeringComponents` / `HasTimedSpawner`
  job parameters. Each apply system owns one query and one job.

## Acceptance Criteria

- Impact AOE archetype is unchanged; impact apply never touches lifetime, pulse,
  gate, or timed-spawn handles.
- A disabled non-timed lingering slot can be reused by a timed lingering AOE, and
  vice versa (crossing only inside the lingering pool).
- Impact and lingering reuse pools are disjoint: no impact command claims a
  lingering slot and no lingering command claims an impact slot.
- Impact apply and lingering apply each have exactly one reuse query and one
  reuse job. No `AoeSpawnKey` / bucket maps remain.
- No AOE reuse job contains impact-vs-lingering-vs-timed eligibility `if` checks.
- Disabled `Active`, `AoeTag`, and lifetime presence/absence are enforced by
  query construction.
- Cold-create counters still report overflow for both apply systems.
- Lingering AOEs reset lifetime, pulse VFX, gates, and timed-spawn enabled state
  correctly; timed spawn is only enabled when lifetime is enabled.

## Dependencies

Depends on 001.

## Complexity

Medium.
