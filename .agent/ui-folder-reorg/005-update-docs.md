---
name: 005-update-docs
description: Update Docs/ui.md, Docs/folder-structure.md, Docs/architecture/layer-rules.md to the new paths and assembly name
---

# Update Docs

## Scope

Three doc files reference the old paths/assembly name directly.

## Changes

1. `Docs/ui.md`:
   - "The `PlayGround.SkillUi` assembly owns feature-specific UI
     controllers." → "The `PlayGround.Ui` assembly owns feature-specific UI
     controllers."
   - Dependency diagram: `PlayGround.SkillUi -> PlayGround.GameLogic ->
     PlayGround.Sim` → `PlayGround.Ui -> PlayGround.GameLogic ->
     PlayGround.Sim`.
   - "Root and templates: `Assets/Scripts/Ui/SkillLoadout/`" →
     `Assets/Scripts/Ui/Hud/` (shared root: `SkillLoadoutUi.uxml`,
     `SkillLoadoutUi.uss`, `ResourceBarUi.uss`) plus
     `Assets/Scripts/Ui/Hud/SkillLoadout/` (skill-bar-private templates).
   - "Runtime controller: `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`" →
     `Assets/Scripts/Ui/Hud/SkillLoadout/SkillLoadoutUi.cs`.
   - "Compiled assembly: `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef`"
     → `Assets/Scripts/Ui/PlayGround.Ui.asmdef`.
2. `Docs/folder-structure.md`:
   - `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef`: `PlayGround.SkillUi`;
     skill-loadout UI." → `Assets/Scripts/Ui/PlayGround.Ui.asmdef`:
     `PlayGround.Ui`; HUD and skill-loadout UI (organized by component under
     `Ui/Hud/`)."
3. `Docs/architecture/layer-rules.md`:
   - `PlayGround.SkillUi -> PlayGround.GameLogic -> PlayGround.Sim` →
     `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`.

## Acceptance criteria

- No remaining occurrences of `PlayGround.SkillUi`, `Assets/Scripts/SkillUi`,
  or `Assets/Scripts/Ui/SkillLoadout/` (old flat path) in `Docs/`.

## Dependencies

Should run after tasks 001–004 so the paths being documented actually exist.
