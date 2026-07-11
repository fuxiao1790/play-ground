# Task Execution Packet

## Task
002-mob-spawn-table-and-pool.md

## Goal
Add weighted mob prefab table and per-prefab recycling pool.

## Files Allowed To Modify
- .agent/mob-spawn-rework/implementation-log.md

## Files Allowed To Create
- Assets/Scripts/Spawn/MobSpawnTable.cs
- Assets/Scripts/Spawn/MobPool.cs

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Assets/Scripts/Mob/MobRoot.cs
- Deleted git file 801e7423~1:Assets/Scripts/Spawn/MobSpawnPool.cs

## Behavior To Preserve
- Weighted prefab selection from old MobSpawnPool algorithm.

## Behavior To Change
- Add explicit runtime pool using InitializeForSpawn from task 001.

## Relevant Global Context
- MobPool owns inactive pool root.
- Rented mobs must be active, positioned, and InitializeForSpawn'ed before controller registers them.
- Source prefab identity must survive disable.

## Dependencies Confirmed
- MobRoot.InitializeForSpawn exists.
- MobRoot.IsAlive exists.
- Deleted weighted algorithm was read from git.

## Step-By-Step Instructions
- Create MobSpawnTable ScriptableObject with Entry array, HasEntries, Configure, and ChoosePrefab.
- Create MobPool plain class with dictionary keyed by prefab and PooledMob origin component.
- Rent pops or instantiates, sets position, activates, initializes, and returns mob.
- Return disables, reparents, and pushes to the origin stack.
- Prewarm creates inactive instances spread across table prefabs.

## Acceptance Criteria
- Rent/Return/Rent for the same prefab reuses the same instance.
- Rent returns active mob with fresh state.
- ChoosePrefab respects weights and skips null prefabs.

## Validation Required
- Compile/build or explain blocked validation.
- Later PlayMode tests cover reuse and table selection behavior.

## Hard Boundaries
- Do not modify files outside the allowed list except direct compile fixes.
- Do not combine with later tasks.
