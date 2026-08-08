# Task Execution Packet

## Task
003-nest-skillloadout.md

## Goal
Nest skill-loadout-private controller, catalogs, and five UXML templates under `Assets/Scripts/Ui/Hud/SkillLoadout/`.

## Files Allowed To Modify
- None in place; move listed files and folder meta only.

## Files Allowed To Create
- `Assets/Scripts/Ui/Hud/SkillLoadout/` destination and moved file/meta pairs.

## Files Allowed To Delete
- `Assets/Scripts/Ui/SkillLoadout/` source folder after its remaining files move.
- Its source folder meta only after relocation preserves GUID.

## Files Likely Needed For Reading
- `Assets/Scripts/Ui/SkillLoadout/` remaining UXML files and folder meta.
- Listed C# files and metas in `Assets/Scripts/SkillUi/`.

## Behavior To Preserve
- All file contents, GUIDs, namespaces, serialized references, and UXML template content.

## Behavior To Change
- Paths only: all nine files become children of `Ui/Hud/SkillLoadout/`.

## Relevant Global Context
- Task 002 complete; `Ui/Hud/` exists. Do not rename namespaces. Inspector references resolve by GUID.

## Dependencies Confirmed
- `Ui/Hud/` exists; source `Ui/SkillLoadout/` contains only the five template UXML files and their metas.

## Step-By-Step Instructions
1. Move existing `Ui/SkillLoadout` folder and its meta into `Ui/Hud/SkillLoadout`, preserving folder GUID.
2. Move four C# files and metas from `SkillUi` into that folder.
3. Confirm all nine file GUIDs, old flat folder absence, and no content changes.

## Acceptance Criteria
- `Ui/Hud/SkillLoadout/` contains nine listed files with original GUIDs.
- `Ui/SkillLoadout/` absent.
- No C# content changed.

## Validation Required
- Static paths, GUIDs, namespace/content comparisons. No Unity test/build.

## Hard Boundaries
- Do not modify namespaces or unrelated files.
- Do not combine asmdef rename; that is task 004.
