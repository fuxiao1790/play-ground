# Task Execution Packet

## Task
001-mobroot-pooling-refactor.md

## Goal
Make MobRoot reusable by adding one per-life InitializeForSpawn path and removing self-Destroy.

## Files Allowed To Modify
- Assets/Scripts/Mob/MobRoot.cs
- .agent/mob-spawn-rework/implementation-log.md

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Assets/Scripts/Mob/MobRoot.cs

## Behavior To Preserve
- First spawned mobs initialize during Awake.
- SoftDie and OnDisable still unregister and delete combat proxy.
- No auto-registration in OnEnable.

## Behavior To Change
- Soft death no longer destroys the GameObject.
- Per-life state is reset through InitializeForSpawn.

## Relevant Global Context
- MobRoot must be active and alive before Register so target proxy creation succeeds.
- Pool/controller owns lifetime after soft death.

## Dependencies Confirmed
- No task dependencies.
- Current MobRoot contains self-Destroy in DeleteCombatTargetProxy and lacks InitializeForSpawn/IsAlive.

## Step-By-Step Instructions
- Move health and initial wander setup from Awake into InitializeForSpawn.
- Add IsAlive property.
- Reset alive/death flags, health, statuses, colliders, renderer, simulated state, velocity, and wander state in InitializeForSpawn.
- Call InitializeForSpawn from Awake after random is created.
- Remove Destroy(gameObject) from DeleteCombatTargetProxy.

## Acceptance Criteria
- SoftDie does not destroy the object.
- SetActive(false), SetActive(true), InitializeForSpawn makes the mob active/alive again.
- IsCombatTargetActive can become true after reuse before Register.
- First-spawn behavior remains initialized from Awake.

## Validation Required
- Compile/build.
- Later PlayMode tests cover reuse.

## Hard Boundaries
- Do not modify files outside the allowed list except direct compile fixes.
- Do not change architecture.
- Do not combine with later tasks.
