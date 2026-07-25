# Task Execution Packet

## Task
005-editor-wiring-and-verification.md

## Goal
Perform the documented Unity Editor wiring and playtest verification, which this task assigns to the user.

## Files Allowed To Modify
- None

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scenes/BenchmarkLarge.unity`
- `Assets/Scripts/SkillUi/GameplayInputSurface.cs`

## Behavior To Preserve
- Do not modify authored scene or EventSystem setup from code.

## Behavior To Change
- In the Unity Editor, the user adds `GameplayInputSurface` to `GameUI`, assigns `PlayerRoot`, and verifies runtime pointer delivery/playtest behavior.

## Dependencies Confirmed
- `GameplayInputSurface`, `PlayerRoot` registration APIs, modal suspension, and USS class all exist.
- `BenchmarkLarge.unity` contains `GameUI` with `UIDocument` and `SkillLoadoutUi`, but no `GameplayInputSurface` component is serialized.
- Static scene search found no `EventSystem` or `InputSystemUIInputModule` serialized in scenes/prefabs.

## Validation Required
- User Editor wiring.
- Confirm existing button pointer delivery or add/verify EventSystem with `InputSystemUIInputModule`.
- Run all five playtest checks in task 005.

## Hard Boundaries
- This task is explicitly user-editor work. Do not edit scene YAML or add EventSystem from code.
