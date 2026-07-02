# Single Thread Reuse

## Change

Replace worker-range reuse jobs with one Burst `IJob` per apply pool. The job
walks disabled chunks in query order, consumes command list entries in order,
and reports the reused prefix length.

## Acceptance Criteria

- Projectile apply reuses disabled projectile slots before cold creation.
- Impact AOE apply reuses disabled impact slots before cold creation.
- Lingering AOE apply reuses disabled lingering slots before cold creation.
- Cold creation handles only commands after the reused prefix.
- Parallel helper, command-index streams, and remainder queues are removed.

## Dependencies

Depends on `001-single-thread-expansion.md`.

## Scope

Large.
