# Task Execution Packet

## Task
003-spawn-pool-topup-batch-enable.md

## Goal
Remove per-entity `Active` toggles during cold pool creation.

## Files Allowed To Modify
- `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`
- Five spawn-apply files named by task detail, only because installed Entities lacks `SetComponentEnabled<T>(NativeArray<Entity>, bool)`:
  - `ProjectileDiscreteSpawnApplySystem.cs`
  - `ProjectileContinuousSpawnApplySystem.cs`
  - `AoeSpawnApplySystem.cs`
  - `TargetedSpawnApplySystem.cs`
- `.agent/burst-onupdate-work/implementation-log.md`

## Files Allowed To Create/Delete
- None.

## Behavior To Preserve
- Fresh slots return with `Active` disabled; live active pool members never change; return remains `deficit`.

## Behavior To Change
- Use `ColdSlotTag` enabled only at archetype creation. Query matches only current fresh batch, disables both `Active` and marker at query granularity.

## Relevant Global Context
- Structural creation stays main-thread. Enableable toggles are non-structural. Tag lifecycle must be documented. No per-entity loop.

## Dependencies Confirmed
- Installed `com.unity.entities` 6.4.0 has entity and `EntityQuery` `SetComponentEnabled` overloads, but no `NativeArray<Entity>` overload.

## Step-By-Step Instructions
1. Add documented enableable `ColdSlotTag` in spawn helper.
2. Add marker to five spawn archetypes and fresh-slot queries scoped by lane/domain.
3. Pass each fresh-slot query to helper.
4. After cold creation, disable `Active` then `ColdSlotTag` through that query.

## Acceptance Criteria
- No per-entity active-toggle loop.
- Only fresh creation batch is disabled.
- Return semantics preserved; tag present on all five archetypes with lifecycle comment.

## Validation Required
- Static checks. User must run pool/spawn Unity tests with XML output.

## Hard Boundaries
- Do not disable a pool-wide query or change live-pool state.
