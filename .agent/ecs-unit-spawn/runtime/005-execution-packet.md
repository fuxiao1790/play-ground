# Task Execution Packet

## Task
005-gameobject-side.md

## Goal
Make pooled mobs dormant until proxy confirmation; make ECS death decision drive next-Update actor shutdown and pool reclaim.

## Files Allowed To Modify
- `Assets/Scripts/Spawn/MobPool.cs`
- `Assets/Scripts/Spawn/SpawnController.cs`
- `Assets/Scripts/Mob/MobRoot.cs`
- `.agent/ecs-unit-spawn/implementation-log.md`

## Files Allowed To Create
- None

## Files Likely Needed For Reading
- `CombatTargetProxy.cs`, `CombatTargetRegistry.cs`, target/presentation bridge files, `Resource.cs`, game-root registration.

## Behavior To Preserve
- Registry still tracks mob for AI candidates and retains its active guard.
- WireMob bindings and SoftDied/reclaim mechanics remain except deferred timing.
- OnDisable still deletes a proxy for non-death teardown.
- Scene-placed mob Awake initialization and registry-driven creation continue.

## Behavior To Change
- Pool Rent returns inactive mob without InitializeForSpawn.
- Controller directly requests proxy, tracks in-flight mob, and enables it only after confirmation next Update.
- Cap includes in-flight spawns.
- Mob opts into ECS death; OnCombatSpawned/OnCombatDespawned record only.
- Mob shutdown runs first next Update; no GameObject-side health death decision or death proxy-delete queue.

## Relevant Global Context
- `CombatTargetRegistry` must not be relaxed: disabled pooled mob needs direct `CombatTargetProxy.Create`.
- All pending collections allocate once and reuse.
- No proxy Delete calls in MobRoot except OnDisable genuine teardown.
- A scene-placed mob can retain unused confirmation flag; comment this deliberately.

## Dependencies Confirmed
- Spawn bridge assigns proxy then calls OnCombatSpawned. Despawn bridge clears proxy then calls OnCombatDespawned. ECS deletes proxy in same Presentation frame.

## Step-By-Step Instructions
1. Remove activation/initialization from MobPool.Rent.
2. Add reusable pending-spawn List; include count in CanSpawn; confirm/update before spawn behavior; request direct proxy after WireMob and track pending; increment ActiveCount only after BeginLife confirmation.
3. MobRoot: add death opt-in, confirmation property/callback, BeginLife; set despawn pending in callback.
4. First Update branch handles pending despawn and returns. Replace SoftDie/depleted handler/late delete state with HandleDespawn exact actor cleanup without proxy delete or re-entry guards. Remove Resource.Depleted subscription.
5. Preserve OnDisable delete and normal resource mirroring. Add scene-placed confirmation note.

## Acceptance Criteria
- Pooled actor only enables after non-null proxy confirmation.
- Cap counts pending plus active.
- ECS health-zero drives one next-Update SoftDied/pool reclaim.
- MobRoot has no direct proxy deletion except OnDisable and no Resource.Depleted subscription.
- Teardown retains deletion; scene-placed mob behavior remains viable; no per-spawn allocation.

## Validation Required
- Static source checks and `git diff --check`. Do not run Unity tests.

## Hard Boundaries
- Do not change registry active guard, tests, docs, prefabs, scenes, or systems.
