# Implementation Context

## Architectural Decisions
- Move in-scope `OnUpdate` work into nested `[BurstCompile]` job structs. `.Run()` is allowed only where task specifies it; use scheduled jobs and existing dependency handles where prescribed.
- Keep managed `DynamicBuffer<*SpawnEvent>` and lane `NativeQueue<*SpawnEvent>` paths separate; they have different safety contracts.
- Do not change system ordering attributes or bridge ownership.

## Global Invariants
- Preserve gameplay behavior and exact numeric semantics.
- Structural changes stay on main thread. Enableable toggles are non-structural.
- Simulation must not read `TargetCompanion` or live Unity objects.
- No `unsafe` in gameplay/shared runtime code.
- Combat hot paths remain allocation-light; native-handle owners dispose on every teardown path.

## Ownership Boundaries
- Lane singleton components own native containers and existing `ProducerHandle`/`PendingHandle`/`ConsumerHandle` fields.
- Managed bridges remain managed; Burst job boundaries are explicit.

## Data Flow
- Managed gameplay submits spawn events to scope buffers; parallel ECS producers submit to lane queues. Expansion merges both sources.
- Scheduled producers publish through existing lane handle fields; sinks complete before draining.

## Lifecycle / Allocation Rules
- One ref-counted `CombatScope` entity.
- Do not add per-entity allocation in combat paths.
- Changed native containers require disposal on normal and destruction paths.

## ECS / Job / Threading Constraints
- Jobs access only unmanaged ECS/native data.
- Chain scheduled work through `state.Dependency` or explicitly named existing singleton handle specified by each task.
- `NativeQueue` order under parallel writers is not meaningful.

## Determinism Requirements
- Preserve existing behavior; no task depends on `NativeQueue` ordering.

## Producer / Consumer Separation
- Do not introduce new handle fields or direct access to another system's private collections.
- Keep combat results, spawn events/commands, and VFX requests as separate typed paths.

## Reused Mechanisms
- Nested `[BurstCompile]` `IJob`, `IJobEntity`, or `IJobChunk` structs.
- Existing lane handle chaining and `TargetSpatialHashSingleton.ConsumerHandle`.
- `TargetedAcquisition` native snapshot API and `NativeQueue<int>.ParallelWriter` counters.

## Introduced Mechanisms
- Task 002 introduces one explicitly registered generic `GatherSpawnEventsJob<T>` shared by four expansion systems.

## Validation Requirements
- Do not run Unity tests. User must run PlayMode and EditMode commands with XML `-testResults`; review XML before claiming pass.
- Perform static/search-based acceptance checks where possible.

## Files / Systems Mentioned By The Plan
- `ResourceRegenSystem`, four spawn expansion systems, `SpawnPoolTopUp`, `ExternalSpawnGateSystem`, target proxy apply systems, stats systems, `CombatApplyBridge`, `TargetSpatialHashSystem`.
