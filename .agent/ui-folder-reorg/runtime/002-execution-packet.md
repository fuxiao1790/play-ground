# Task Execution Packet

## Task
002-build-hud-root.md

## Goal
Create `Assets/Scripts/Ui/Hud/`, move shared HUD assets/controllers there, remove dead ResourceBars template, and fix UXML stylesheet path.

## Files Allowed To Modify
- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uxml`
- Moved file locations only; `ResourceBarsUi.uss` is renamed to `ResourceBarUi.uss`.

## Files Allowed To Create
- `Assets/Scripts/Ui/Hud/` and moved file/meta destinations.

## Files Allowed To Delete
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml` and `.meta`.
- `Assets/Scripts/Ui/ResourceBars/` and `.meta` after empty.

## Files Likely Needed For Reading
- Source/meta pairs listed in `002-build-hud-root.md`.
- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uxml`.

## Behavior To Preserve
- All source/style/UXML content except required stylesheet path; preserve every listed file GUID.

## Behavior To Change
- Shared HUD files now sit beside one another under `Ui/Hud/`.
- UXML stylesheet reference becomes `ResourceBarUi.uss`.

## Relevant Global Context
- Move files with metas; no Unity Inspector work; no runtime behavior change.

## Dependencies Confirmed
- Task 001 complete. `Persistence/PlayerSaveController.cs` exists and is outside this move.

## Step-By-Step Instructions
1. Create `Ui/Hud`.
2. Move `GameplayInputSurface.cs`, `ResourceBarUi.cs`, `SkillLoadoutUi.uxml`, and `SkillLoadoutUi.uss` with metas.
3. Delete dead `ResourceBarsUi.uxml` pair.
4. Move/rename `ResourceBarsUi.uss` to `Hud/ResourceBarUi.uss` with meta.
5. Update only second `Style src` line in moved UXML.
6. Remove empty ResourceBars folder and meta.

## Acceptance Criteria
- Five expected live files in `Ui/Hud`, original GUIDs preserved.
- ResourceBars folder absent.
- Both UXML styles resolve as same-folder references.
- No other content changes.

## Validation Required
- Static paths, GUIDs, UXML references, and content comparisons. No Unity test/build.

## Hard Boundaries
- Do not move or edit files outside task scope.
- Do not change namespaces or architecture.
