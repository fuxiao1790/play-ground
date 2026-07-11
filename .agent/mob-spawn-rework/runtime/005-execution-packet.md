# Task Execution Packet

## Task
005-gameroot-integration.md

## Goal
Add SpawnController reference and fallback binding to GameRoot without changing static mob path or Configure signature.

## Files Allowed To Modify
- Assets/Scripts/Game/GameRoot.cs
- .agent/mob-spawn-rework/implementation-log.md

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Assets/Scripts/Game/GameRoot.cs
- Assets/Scripts/Spawn/SpawnController.cs

## Behavior To Preserve
- Existing mobs[] discovery/registration loop.
- GameRoot.Configure(CombatRoot, PlayerRoot, MobRoot[]) signature.
- Scenes with static mobs and no controller behave as before.

## Behavior To Change
- GameRoot may find a SpawnController and bind combatRoot/player target in Start.

## Relevant Global Context
- Cross-MonoBehaviour handoff belongs in Start, not Awake.
- SpawnController.Bind fills null fields only.

## Dependencies Confirmed
- SpawnController exists with Bind(CombatRoot, Transform).

## Step-By-Step Instructions
- Add using PlayGround.Spawn.
- Add serialized SpawnController field.
- In Awake, find SpawnController if null.
- In Start, call spawnController?.Bind(combatRoot, player != null ? player.transform : null).

## Acceptance Criteria
- Wired controller with zero static mobs can spawn.
- Static mobs/no controller path unchanged.
- Existing GameRoot tests still pass.

## Validation Required
- Compile/build or explain blocked validation.

## Hard Boundaries
- Additive only.
- Do not change GameRoot Configure signature.
