# Task Execution Packet

## Task
001-spawn-pool-topup-helper.md

## Goal
Add shared `SpawnPoolTopUp.EnsureDisabledSlots` helper for exact pre-fill pool growth.

## Files Allowed To Modify
- None

## Files Allowed To Create
- `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Lifetime/CombatLifecycleComponents.cs`
- `Assets/Scripts/System/Spawning/*.cs`

## Behavior To Preserve
- Existing spawn apply behavior remains unchanged until later tasks wire the helper.

## Behavior To Change
- Add reusable main-thread helper that creates missing pool slots and disables `Active`.

## Relevant Global Context
- Use `CalculateEntityCount()`, not count without filtering.
- `CreateEntity` is structural and must be called before callers fetch chunks/type handles.
- Fresh enableable components default enabled; disable `Active` on created entities.
- Temporary native array must be disposed in helper.

## Dependencies Confirmed
- None required.
- `Active` exists in `PlayGround.System.Combat.Lifetime`.
- Existing spawning namespace is `PlayGround.System.Combat.Spawning`.

## Step-By-Step Instructions
- Create internal static helper under spawning namespace.
- Compute `deficit = demand - disabledSlotQuery.CalculateEntityCount()`.
- Return 0 with no create when deficit is non-positive.
- Batch create exactly `deficit` entities from archetype using `Allocator.Temp`.
- Disable `Active` on each created entity.
- Dispose created array and return deficit.

## Acceptance Criteria
- Compiles; no job references.
- Returns 0 when enough disabled slots already exist.
- Returns and creates exactly the deficit, with `Active` disabled.

## Validation Required
- Build/compile after implementation.

## Hard Boundaries
- Do not modify files outside the allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
