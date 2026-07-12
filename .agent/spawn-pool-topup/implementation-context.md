# Implementation Context

## Architectural Decisions
- Delete spawn-apply ECB cold paths for projectiles, impact AOEs, and lingering AOEs.
- Grow the matching disabled-slot pool to exact command demand before reuse fill.
- Keep the existing reuse jobs as the single command materialization path.
- Keep cleanup ownership in `CombatPoolCleanupSystem`; top-up only grows pools.

## Global Invariants
- Disabled pool discovery is by disabled `Active` only.
- Fresh entities from `EntityManager.CreateEntity(archetype, count)` have enableable components enabled, so top-up must disable `Active`.
- Use `EntityQuery.CalculateEntityCount()` so disabled-slot counts honor enable filters.
- `Reuse` counter reports command count after top-up; `TopUp` counter reports created deficit.
- Existing `LastColdCreateCount` and `EntitiesSpawnedViaEcb` may carry top-up-created counts for compatibility.

## Ownership Boundaries
- Spawn apply systems own converting command arrays into projectile/AOE entities.
- `SpawnPoolTopUp` is a shared combat spawning helper only; it owns no persistent state.
- No new systems, components, or runtime data paths.

## Data Flow
- Commands are drained from lane singletons.
- Apply system calls `SpawnPoolTopUp.EnsureDisabledSlots` with command count demand.
- Existing reuse job fills disabled slots directly.
- No command suffix is recorded to or played back from an ECB.

## Lifecycle / Allocation Rules
- Top-up may allocate a temporary `NativeArray<Entity>` on the main thread and disposes it in the helper.
- Top-up performs one batch structural create only when demand exceeds disabled slots.
- Disabling `Active` on created entities is non-structural.

## ECS / Job / Threading Constraints
- Structural create must happen on the main thread before chunk arrays and component type handles are fetched.
- Reuse jobs stay single-threaded Burst `IJob`.
- No job should call the top-up helper.

## Determinism Requirements
- Command fields continue to provide IDs and state.
- Reuse fill remains sequential.

## Producer / Consumer Separation
- Spawn apply systems consume spawn commands only.
- Damage, VFX, target callbacks, and spawn expansion paths are unchanged.

## Reused Mechanisms
- Existing archetypes, disabled-slot queries, chunk fill jobs, enableable pooling, and cleanup trimming.

## Introduced Mechanisms
- `PlayGround.System.Combat.Spawning.SpawnPoolTopUp.EnsureDisabledSlots`.
- `*.TopUp` profiler counters replacing `*.Cold` counter names.

## Validation Requirements
- Compile/build after changes.
- Verify no dangling old record helpers or cold-create markers remain.
- Verify top-up call ordering before chunk arrays/type handles.
- Run available Unity test suites if feasible.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Docs/profiling.md`
- `Assets/Scripts/System/Stats/CombatStatsComponents.cs`
