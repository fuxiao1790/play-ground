# Task Execution Packet

## Task

009-create-debugging-package.md

## Goal

Create the exempt `com.playground.debugging` leaf package and move Debugging scripts into it without changing code or script GUIDs.

## Files Allowed To Modify

- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `Packages/com.playground.debugging/package.json`
- `Packages/com.playground.debugging/Runtime/PlayGround.Debugging.asmdef` and `.meta`

## Files Allowed To Move

- `Assets/Scripts/Debugging/**` and paired `.meta`.

## Behavior To Preserve

- Global namespace for `PerformanceText`.
- Debug overlay and VFX tester behavior.
- All asset script GUIDs.

## Relevant Global Context

- Debugging is an exempt leaf. It references Sim and GameLogic; it does not need SkillUi.
- It directly uses Unity.Entities, UnityEngine.UI, and Visual Effect Graph.
- Core packages may not reference PlayGround.Debugging.

## Acceptance Criteria

- Sim, GameLogic, and SkillUi asmdefs have no Debugging reference.
- Moved Debugging metadata GUIDs are unchanged.
- No Debugging source remains under Assets.

## Validation Required

- Search core asmdefs for PlayGround.Debugging.
- Compare moved metadata GUIDs.
- Parse packages-lock JSON and run `git diff --check`.

## Hard Boundaries

- Do not modify Debugging source content, namespaces, or core package asmdefs.
