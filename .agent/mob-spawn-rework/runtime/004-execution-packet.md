# Task Execution Packet

## Task
004-spawn-controller.md

## Goal
Add SpawnController MonoBehaviour implementing ISpawnSink, owning table, behaviour, placement, pool, combat wiring, and death reclaim.

## Files Allowed To Modify
- .agent/mob-spawn-rework/implementation-log.md

## Files Allowed To Create
- Assets/Scripts/Spawn/SpawnController.cs

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Assets/Scripts/Game/GameRoot.cs
- Assets/Scripts/Mob/MobRoot.cs
- Assets/Scripts/Spawn/MobPool.cs
- Assets/Scripts/Spawn/MobSpawnTable.cs
- Assets/Scripts/Spawn/SpawnBehaviour.cs
- Assets/Scripts/Spawn/SpawnPlacement.cs

## Behavior To Preserve
- Existing static GameRoot mob path remains untouched for this task.

## Behavior To Change
- Add controller-managed pooled mob spawning.

## Relevant Global Context
- Awake only validates and creates self-owned runtime state; no cross-MonoBehaviour calls or spawning.
- Register order requires active + initialized mob before Register.
- Reclaim is deferred after SoftDied.

## Dependencies Confirmed
- MobRoot.InitializeForSpawn exists.
- MobPool.Rent returns active initialized mob.
- SpawnBehaviourRuntime, SpawnPlacement, SpawnContext, and ISpawnSink exist.

## Step-By-Step Instructions
- Add serialized table/behaviour/placement/spawnPoints/cap/combatRoot/vfxRoot/target/prewarm/randomSeed.
- Awake validates required SO refs, creates inactive pool root, pool, rng, behaviour runtime, discovers points if needed, and prewarms max(prewarm, cap).
- Update early returns without combatRoot, otherwise ticks behaviour and reclaims dead mobs.
- Spawn chooses prefab, resolves placement, rents, wires, increments active count.
- WireMob registers, binds combat/VFX, sets target, and subscribes SoftDied.
- OnMobSoftDied unsubscribes and queues reclaim.
- Bind fills null combatRoot/target only.

## Acceptance Criteria
- Controller spawns up to cap and holds there with valid config.
- Killed mobs are returned next Update and activeCount decrements.
- No reclaim re-entrancy.
- Awake does no cross-MonoBehaviour work and prewarms.

## Validation Required
- Compile/build or explain blocked validation.
- Later PlayMode tests cover behavior.

## Hard Boundaries
- Do not modify files outside allowed list except direct compile fixes.
- Do not integrate GameRoot until task 005.
