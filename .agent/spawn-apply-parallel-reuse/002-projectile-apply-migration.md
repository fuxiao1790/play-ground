# 002 Projectile Apply Migration

## Goal

Move the unified projectile apply (from the unify plan's 002) onto the parallel
dead-slot mechanism from 001.

## Scope

- Remove the serial reuse path: `.Schedule` with the shared
  `NativeReference<int> ClaimedCount` and its `[NativeDisableContainerSafetyRestriction]`.
- Reuse the existing index-permutation idea: the projectile apply already permutes
  command indices (the old degenerate `BucketCommandsJob`). Repurpose that into the
  per-lane index scatter from 001, or replace it outright.
- Capture the projectile disabled-slot chunks (`WithAll<ProjectileTag>`,
  `WithDisabled<Active>`) via `ToArchetypeChunkArray`, split them into `W` worker
  chunk ranges, build a `W`-lane command stream, and schedule the
  `IJobParallelFor` reuse job over `W` per 001.
- Chunk-local reuse writes: identity, kinematics, collision, lifetime (+enable),
  hit, tracking (+enable), render, batch id, render element, contact gate, and
  `TimedSpawnComponent` data + `SetComponentEnabled<TimedSpawnComponent>` from
  `cmd.HasTimedSpawner`.
- Overflow command indices → remainder container → ECB cold-create with the
  unified projectile archetype.
- Preserve `LastReuseCount` / `LastColdCreateCount` and the profiler counters.

## Acceptance Criteria

- Projectile reuse runs as `IJobParallelFor` over `W` workers (each owning a lane
  and a chunk range) with no shared claim counter.
- Basic and timed-spawner projectiles still spawn correctly, reuse across each
  other, and reset all enableable state.
- Overflow beyond available free slots is cold-created; counts still reported.
- No `[NativeDisableContainerSafetyRestriction]` remains on the reuse counter.

## Dependencies

Depends on 001 and unify plan 002.

## Complexity

Medium.
