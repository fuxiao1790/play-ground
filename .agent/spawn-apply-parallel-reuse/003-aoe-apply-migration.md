# 003 AOE Apply Migration (Impact And Lingering)

## Goal

Move both AOE apply systems (from the unify plan's 003:
`ImpactAoeSpawnApplySystem` and `LingeringAoeSpawnApplySystem`) onto the parallel
dead-slot mechanism from 001.

## Scope

- Drain the AOE command handoff to a flat per-domain command array (the current
  `AoeSpawnExpansionSystem` already writes a `NativeStream`; either keep it and
  read per-chunk, or drain to a flat array and build the partition table in apply).
- Each apply system captures its own dead-slot chunks via `ToArchetypeChunkArray`,
  splits them into `W` worker chunk ranges, builds a `W`-lane command stream, and
  runs the `IJobParallelFor` reuse job over `W` per 001 (1 lane : 1 worker : many
  chunks).
- Impact apply:
  - Query `WithAll<AoeTag>`, `WithDisabled<Active>`,
    `WithNone<CombatLifetimeComponent>`.
  - Chunk-local reuse writes the impact fields only (no lifetime/pulse/gate/timed).
  - Overflow → remainder → ECB cold-create with the impact archetype.
- Lingering apply:
  - Query `WithAll<AoeTag>`, `WithDisabled<Active>`,
    `WithAll<CombatLifetimeComponent>`.
  - Chunk-local reuse writes lifetime (+enable), pulse VFX, contact gate clear,
    and timed-spawn data + enable/disable per `HasTimedSpawner(cmd)`.
  - Overflow → remainder → ECB cold-create with the lingering archetype.
- Remove the serial reuse path and the shared `NativeReference<int> ClaimedCount`
  usage. Remove `[NativeDisableContainerSafetyRestriction]` on the counter.
- Preserve per-system reuse/cold counts and profiler counters.

## Notes

- Impact and lingering keep disjoint pools by lifetime presence, so each apply job
  only ever touches its own domain's chunks; the parallel partitioning is per
  system.
- The lingering reuse job sets timed-spawn enabled state per command; it must not
  branch on timed vs non-timed as a slot filter.

## Acceptance Criteria

- Both AOE apply systems run as `IJobParallelFor` over `W` workers (each owning a
  lane and a chunk range) with no shared claim counter.
- Impact and lingering reuse stay disjoint; overflow cold-creates the correct
  archetype per system.
- Lingering AOEs reset lifetime, pulse, gates, and timed-spawn enable correctly.
- Counts still reported for both systems.

## Dependencies

Depends on 001 and unify plan 003.

## Complexity

Medium.
