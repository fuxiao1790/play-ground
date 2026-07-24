# Task Execution Packet

## Task

005-create-gamelogic-package.md

## Goal

Create `com.playground.game-logic` / `PlayGround.GameLogic` and move game-logic source into it with all Unity metadata intact.

## Files Allowed To Modify

- `Packages/manifest.json`
- `Packages/com.playground.game-logic/package.json`
- `Packages/com.playground.game-logic/Runtime/PlayGround.GameLogic.asmdef` and `.meta`

## Files Allowed To Move

- `Assets/Scripts/{Skills,Mob,Spawn,Player,Camera,Audio,Level,Game,Persistence}/**` and paired `.meta`.
- `Assets/Scripts/Common/{Stats,StatusEffects}/**` and paired `.meta`.
- `Assets/Scripts/Common/PersistentScriptableObject.cs` and `.meta`.
- `Assets/Scripts/Skills/Authoring/AoeConfig.cs` and `.meta` via the Skills move.

## Behavior To Preserve

- All C# source, namespaces, and asset GUIDs.
- Existing GameLogic-to-Sim calls and contracts.

## Relevant Global Context

- Game logic may reference PlayGround.Sim, never UI.
- Moved code directly uses Unity.Collections, Unity.Entities, Unity.InputSystem, Unity.Mathematics, Unity.Profiling.Core, and Unity.VisualEffectGraph.Runtime.
- Debugging stays under Assets until task 009.

## Dependencies Confirmed

- PlayGround.Sim package is present and its asmdef has no upward reference.

## Step-By-Step Instructions

1. Create package manifest and asmdef with PlayGround.Sim plus the listed Unity references only.
2. Add the file package dependency to the root manifest.
3. Move only the listed game-logic folders/files with paired metadata.
4. Leave Debugging and `PlayGround.Runtime.asmdef` untouched for later tasks.

## Acceptance Criteria

- GameLogic references PlayGround.Sim, not PlayGround.SkillUi.
- Moved game-logic files have unchanged GUIDs.
- No moved game-logic source remains in Assets.

## Validation Required

- Compare pre/post GUIDs for every moved `.meta`.
- Search GameLogic for `PlayGround.SkillUi`.
- Run `git diff --check`.
- Do not launch Unity while its interactive editor is open.

## Hard Boundaries

- Do not move Debugging, UI, Simulation, or unrelated Common code.
- Do not delete the Runtime asmdef or change imports/namespaces.
