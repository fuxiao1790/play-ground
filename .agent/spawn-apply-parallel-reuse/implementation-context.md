# Implementation Context

## Architectural Decisions
- Spawn apply uses one parallel dead-slot reuse job per domain.
- Apply captures disabled-slot chunks at apply time and does not perform structural changes until reuse completes.
- Worker count is clamped to the matched chunk count; each worker owns a contiguous chunk range.
- Command lane count equals worker count. Each worker reads only its own lane.
- Overflow command indices are cold-created through ECB after reuse.

## Global Invariants
- No shared claim cursor in reuse jobs.
- Reuse writes directly to chunk component arrays.
- Cold creation uses the correct one-archetype domain pool.
- Reuse and cold-create reset the same component state.
- Command payloads are read from the flat command array by index.

## Ownership Boundaries
- Projectile apply owns projectile disabled slots and projectile cold creation.
- Impact AOE apply owns impact AOE disabled slots and impact cold creation.
- Lingering AOE apply owns lingering AOE disabled slots and lingering cold creation.
- Shared helper code may own partition/range/lane mechanics only.

## Data Flow
- Events are gathered by expansion systems.
- Expansion emits flat per-domain command containers.
- Apply builds chunk ranges and a worker-lane command-index stream.
- Reuse consumes worker lanes and dead slots.
- Remainder command indices become ECB-created entities.

## Lifecycle / Allocation Rules
- Native chunk arrays, worker ranges, command streams, and remainder queues are TempJob/Temp scoped and disposed in apply.
- No structural change happens before captured chunks are no longer used.
- ECB plays back once only when cold entities are created.

## ECS / Job / Threading Constraints
- Reuse is `IJobParallelFor` over worker indices.
- Each worker owns disjoint chunk ranges, so no two workers write the same chunk.
- Shared job writes are limited to a parallel remainder queue.
- Enableable state must be reset on reuse.

## Determinism Requirements
- Command data remains source of deterministic IDs and jitter.
- Consumers must not depend on spawned entity order or chunk placement.

## Producer / Consumer Separation
- Event queues remain multi-producer.
- Command containers remain per-domain handoffs.
- Apply owns laning and dead-slot placement.

## Reused Mechanisms
- Existing per-domain command containers.
- Existing reset helpers for AOE cold-create paths.
- Existing profiler counter names and last-count fields.

## Introduced Mechanisms
- Worker chunk range table.
- Worker-lane `NativeStream` of command indices.
- Parallel remainder `NativeQueue<int>`.

## Validation Requirements
- Build compiles.
- Existing spawn, collision, lifetime, and timed-spawn tests pass where run.
- New or updated tests cover reuse, overflow, and count splits.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Common/`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Docs/flows/spawn-event-to-entity.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/aoe-system.md`
- `Docs/profiling.md`
