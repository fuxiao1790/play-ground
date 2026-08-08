# Task Execution Packet

## Task
001-move-player-save-controller.md

## Goal
Move `PlayerSaveController.cs` from `SkillUi` to `Persistence` and change its namespace.

## Files Allowed To Modify
- `Assets/Scripts/Persistence/PlayerSaveController.cs`

## Files Allowed To Create
- `Assets/Scripts/Persistence/PlayerSaveController.cs`
- `Assets/Scripts/Persistence/PlayerSaveController.cs.meta` (by move only)

## Files Allowed To Delete
- `Assets/Scripts/SkillUi/PlayerSaveController.cs`
- `Assets/Scripts/SkillUi/PlayerSaveController.cs.meta`

## Files Likely Needed For Reading
- `Assets/Scripts/SkillUi/PlayerSaveController.cs`
- `Assets/Scripts/SkillUi/PlayerSaveController.cs.meta`
- `Assets/Scripts/Persistence/PlayerSaveStore.cs`
- `Assets/Scripts/Persistence/PlayerSaveData.cs`

## Behavior To Preserve
- All code, using directives, serialized fields, lifecycle behavior, and GUID.

## Behavior To Change
- File location and namespace `PlayGround.Skills` -> `PlayGround.Persistence`.

## Dependencies Confirmed
- None; first task.

## Step-By-Step Instructions
1. Move file and paired meta intact.
2. Change only namespace declaration.
3. Confirm old pair absent, new pair present, GUID unchanged, and no downstream source edits required.

## Acceptance Criteria
- New file exists at `Assets/Scripts/Persistence/PlayerSaveController.cs`.
- Meta GUID is `86d95e319c5b4c16bcaa094037e53c22`.
- Namespace is `PlayGround.Persistence`.
- Old file pair absent.

## Validation Required
- Static diff/content/GUID/path checks only. No Unity test/build.

## Hard Boundaries
- Do not modify other source files.
- Do not change architecture or behavior.
