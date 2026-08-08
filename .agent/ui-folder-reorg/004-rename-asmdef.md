---
name: 004-rename-asmdef
description: Move and rename the SkillUi asmdef to PlayGround.Ui, delete the emptied SkillUi folder
---

# Rename PlayGround.SkillUi → PlayGround.Ui

## Scope

With every `.cs`/`.uxml`/`.uss` file relocated (tasks 001–003),
`Assets/Scripts/SkillUi/` should contain only the `.asmdef` (+ `.meta`) and
be otherwise empty. Move the asmdef into `Ui/` and rename it.

## Changes

1. Move `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef` (+ `.meta`, guid
   `d394f99bb0fa66f49913142bc287198d`) to
   `Assets/Scripts/Ui/PlayGround.Ui.asmdef`.
2. Rename the file itself to `PlayGround.Ui.asmdef` and edit its JSON:
   - `"name": "PlayGround.SkillUi"` → `"name": "PlayGround.Ui"`
   - `"rootNamespace": "PlayGround.SkillUi"` → `"rootNamespace":
     "PlayGround.Ui"` (cosmetic default for new scripts only; does not
     rename existing namespaces)
   - `references` array (`PlayGround.GameLogic`, `PlayGround.Sim`,
     `Unity.Collections`, `Unity.Entities`, `Unity.InputSystem`) stays
     unchanged.
3. Confirmed via project-wide grep: no other `.asmdef`'s `references` array
   lists `PlayGround.SkillUi`, so no other assembly definition needs editing.
4. Delete `Assets/Scripts/SkillUi/` and its folder `.meta` once verified
   empty.

## Acceptance criteria

- `Assets/Scripts/Ui/PlayGround.Ui.asmdef` exists with guid
  `d394f99bb0fa66f49913142bc287198d`, `"name": "PlayGround.Ui"`,
  `"rootNamespace": "PlayGround.Ui"`.
- `Assets/Scripts/SkillUi/` no longer exists.
- `Assets/Scripts/Ui/` now contains only: `Hud/` (with nested
  `SkillLoadout/`) and `PlayGround.Ui.asmdef`.

## Dependencies

Must run after tasks 001–003 (folder must be empty of content files before
deleting it).
