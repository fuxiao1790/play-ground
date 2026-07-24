# Task Execution Packet

## Task

002-relocate-aoeconfig-authoring.md

## Goal

Move `AoeConfig` from simulation into game-logic authoring, retaining its Unity script GUID and eliminating the last simulation-to-game-logic and Common-to-simulation imports.

## Files Allowed To Modify

- `Assets/Scripts/System/Aoes/AoeConfig.cs` and `.meta` (move only)
- `Assets/Scripts/Skills/Authoring/AoeConfig.cs` and `.meta` (new location created by move)
- `Assets/Scripts/Common/StatusEffects/StackingTriggerDef.cs`
- `Assets/Scripts/Skills/SkillDriver.cs` only if an obsolete import is left after the namespace change

## Files Allowed To Create

- `Assets/Scripts/Skills/Authoring/` directory through the file move.

## Files Allowed To Delete

- Old `Assets/Scripts/System/Aoes/AoeConfig.cs` and `.meta` paths through the move.

## Files Likely Needed For Reading

- `Assets/Scripts/System/Aoes/AoeConfig.cs`
- `Assets/Scripts/Common/StatusEffects/StackingTriggerDef.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Skills/BasicAoePrefab.cs` or its actual location

## Behavior To Preserve

- Existing `AoeConfig` asset script references, fields, and serialized values.
- `AoeConfig.CreateTypeDefinition` and `CreateSpawnGeometry` output.
- Game-logic use of AOE simulation contracts.

## Behavior To Change

- Change `AoeConfig` namespace from `PlayGround.System.Combat.Aoes` to `PlayGround.Skills`.
- Remove unused simulation imports from `AoeConfig`.
- Point `StackingTriggerDef` at `PlayGround.Skills`.

## Relevant Global Context

- `AoeConfig` is authored data and legally depends downward on sim contracts (`AoeTypeDefinition`, `AoeSpawnGeometry`).
- Simulation must not import game logic; Common game-logic authoring must not import simulation merely to name `AoeConfig`.
- A physical Unity script move must carry its `.meta` file so its GUID remains unchanged.

## Dependencies Confirmed

- Task 001 is complete: `CombatRoot` no longer names `AoeConfig`.
- Static inspection found the remaining old-namespace referencer in `StackingTriggerDef`; `SkillDriver` is in `PlayGround.Skills` and imports the old AOE namespace for sim contracts.

## Step-By-Step Instructions

1. Move `AoeConfig.cs` and its paired `.meta` to `Assets/Scripts/Skills/Authoring/` together.
2. Change its namespace to `PlayGround.Skills`.
3. Retain only imports required by its actual type references.
4. Change `StackingTriggerDef` to import `PlayGround.Skills`.
5. Do not remove `SkillDriver`'s old AOE import if it still supplies simulation types other than `AoeConfig`.

## Acceptance Criteria

- No `using PlayGround.(Skills|Player|Mob|Spawn|Persistence|Audio|Game|Level|CameraSystem)` under `Assets/Scripts/System/`.
- No `using PlayGround.System` under `Assets/Scripts/Common/`.
- The moved `.meta` preserves the original AoeConfig GUID.

## Validation Required

- Run both import acceptance searches.
- Compare the AoeConfig `.meta` GUID before and after the move.
- Run a Unity compile check if available; otherwise state why it could not be run.

## Hard Boundaries

- Do not modify files outside the allowed list except an import required by the namespace move.
- Do not change runtime behavior, add a shim, or combine later tasks.
- Stop on architectural ambiguity.
