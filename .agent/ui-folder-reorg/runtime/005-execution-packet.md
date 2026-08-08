# Task Execution Packet

## Task
005-update-docs.md

## Goal
Update three documentation files to describe `PlayGround.Ui` and final HUD paths.

## Files Allowed To Modify
- `Docs/ui.md`
- `Docs/folder-structure.md`
- `Docs/architecture/layer-rules.md`

## Files Allowed To Create/Delete
- None.

## Behavior To Preserve
- Documentation meaning and architecture boundaries.

## Behavior To Change
- Assembly name, dependency diagram, final paths, and UI grouping descriptions.

## Relevant Global Context
- Tasks 001–004 complete. Final paths: `Ui/Hud/`, `Ui/Hud/SkillLoadout/`, `Ui/PlayGround.Ui.asmdef`.

## Dependencies Confirmed
- Destination asmdef and final UI tree exist; old `SkillUi` folder absent.

## Step-By-Step Instructions
1. Replace old assembly name and dependency diagram.
2. Describe shared HUD root files and nested skill-loadout files.
3. Update controller/asmdef paths.
4. Update any remaining old assembly mention in these docs, including feature-addition guidance.

## Acceptance Criteria
- Docs contain no `PlayGround.SkillUi`, `Assets/Scripts/SkillUi`, or old flat `Assets/Scripts/Ui/SkillLoadout/` occurrences.
- New paths and assembly name match implementation.

## Validation Required
- `rg` stale-token scan across `Docs/`; inspect changed diff. No Unity test/build.

## Hard Boundaries
- Modify only three listed docs.
- Do not alter architecture decisions.
