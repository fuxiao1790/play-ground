# Task Execution Packet

## Task
004-rename-asmdef.md

## Goal
Move and rename `PlayGround.SkillUi.asmdef` into `Ui/PlayGround.Ui.asmdef`, update only its `name` and `rootNamespace`, then remove empty `SkillUi` folder.

## Files Allowed To Modify
- `Assets/Scripts/Ui/PlayGround.Ui.asmdef`

## Files Allowed To Create
- `Assets/Scripts/Ui/PlayGround.Ui.asmdef` and moved `.meta` pair.

## Files Allowed To Delete
- `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef` and `.meta`.
- `Assets/Scripts/SkillUi/` and `Assets/Scripts/SkillUi.meta` after empty.

## Files Likely Needed For Reading
- `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef` and `.meta`.
- Other `.asmdef` files for reference comparison.

## Behavior To Preserve
- References array and all other asmdef JSON fields; asmdef GUID.

## Behavior To Change
- File path/name, JSON `name`, and JSON `rootNamespace` become `PlayGround.Ui`.

## Relevant Global Context
- Tasks 001–003 complete; no other asmdef references old assembly. Existing C# namespaces remain unchanged.

## Dependencies Confirmed
- `Assets/Scripts/SkillUi/` contains only asmdef and asmdef meta.

## Step-By-Step Instructions
1. Confirm source folder contains only asmdef pair.
2. Move/rename asmdef and meta into `Assets/Scripts/Ui`.
3. Edit only two JSON values.
4. Remove empty source folder and its folder meta.

## Acceptance Criteria
- Destination asmdef GUID `d394f99bb0fa66f49913142bc287198d`.
- JSON name/rootNamespace both `PlayGround.Ui`; references unchanged.
- `Assets/Scripts/SkillUi/` absent.

## Validation Required
- Static folder, GUID, JSON, and reference checks. No Unity test/build.

## Hard Boundaries
- Do not rename existing C# namespaces or alter asmdef references.
- Do not edit other assembly definitions.
