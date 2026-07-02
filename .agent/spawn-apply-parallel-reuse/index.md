# Spawn Apply Parallel Reuse Replacement

## Summary

Replace worker-lane spawn expansion/apply with one single-threaded Burst
expansion job per domain and one single-threaded Burst dead-slot reuse job per
apply pool.

The new data flow is:

`SpawnEvent` -> single Burst expansion -> `NativeList<SpawnCommand>` ->
single Burst dead-slot reuse -> cold-create unreused command suffix.

## Rationale

The old parallel reuse path depended on worker-owned chunk ranges and
worker-owned command-index lanes. Unity does not let this code pin one native
stream lane to one worker in a useful way, so the system paid for stream setup,
lane distribution, and imbalance without getting reliable worker-local
ownership.

The new structure removes the parallel lane abstraction entirely. Expansion
produces one ordered command list. Apply owns one ordered command cursor and one
disabled-slot scan for each pool.

## Constraints And Invariants

- ECS simulation owns projectile/AOE entities, spawn expansion/apply, and
  `Active`-based reuse. Source: `Docs/layers/ecs-simulation.md`.
- Spawn events are gameplay intent; spawn commands are one-entity allocation
  intent. Expansion owns spawn math. Apply owns reuse and cold creation. Source:
  `Docs/contracts/spawn-events-and-commands.md`.
- Hot despawn must use enableable state, not destroy/create churn. Source:
  `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`.
- Apply runs after collision; spawned/reused entities join simulation on the
  next update. Source: `Docs/flows/spawn-event-to-entity.md`.
- Domain identity must come from `ProjectileTag` or `AoeTag`, not `Active` or
  scope membership. Source: `Docs/coding-standards.md`.

## Reused Vs Introduced

- Reused: existing event -> command -> apply contracts.
- Reused: existing projectile, impact AOE, and lingering AOE pool queries.
- Reused: existing cold-create ECB fallback.
- Introduced: ordered `NativeList<TCommand>` command containers owned by
  expansion systems.
- Removed: `ParallelDeadSlotSpawnApply`, worker ranges, command-index
  `NativeStream`s, and remainder queues.

## Design Validation

- Ownership stays clear: expansion owns command production; apply owns entity
  materialization.
- Enableable pooling remains intact: reusable slots are found through
  `WithDisabled<Active>()`.
- Cold creation now means true pool shortage for that archetype, not worker
  lane imbalance.
- Spawn math stays out of apply systems.
- The data path has one representation of commands at apply time.

## Additive Vs Refactor

Minimal/additive approach:

- resulting data flow: keep native streams and worker lanes, add tuning or
  more balancing.
- new concepts/types introduced: likely chunk popcounts or lane balancers.
- copies/translations added: keep stream-to-array drain and lane-index streams.
- long-term cost: parallel path remains hard to reason about and profile.

Refactor approach:

- resulting data flow: one command list, one reuse cursor, cold suffix.
- existing concepts/types changed or removed: command streams replaced by
  command lists; parallel helper removed.
- copies/translations removed or avoided: no command stream drain, no
  command-index stream, no remainder queue.
- long-term benefit: fewer moving parts and clearer ownership.

Decision:

- choose refactor.
- reason: the old worker-lane model cannot guarantee the worker/stream-lane
  ownership it was designed around, so the abstraction cost is higher than the
  benefit.

## Tasks

- [001-single-thread-expansion.md](001-single-thread-expansion.md)
- [002-single-thread-reuse.md](002-single-thread-reuse.md)
- [003-docs-and-validation.md](003-docs-and-validation.md)

## Open Questions

- Build/test may reveal whether `NativeList<T>.Add` growth in Burst should be
  replaced by a pre-count pass or conservative capacity reservation.
