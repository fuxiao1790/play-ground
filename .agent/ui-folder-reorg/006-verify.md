---
name: 006-verify
description: Confirm every moved file kept its guid, no stale references remain, and report the final tree
---

# Verify

## Scope

Final check pass — no code changes, just verification and a report.

## Checks

1. For every file moved in tasks 001–004, diff its `.meta` guid against the
   value recorded in that task file — all must be unchanged.
2. Grep the full repo (excluding `.git/`) for `PlayGround.SkillUi`,
   `Assets/Scripts/SkillUi`, and `Assets/Scripts/Ui/SkillLoadout/` (old flat
   path) — the only expected remaining hits are historical/cosmetic
   `m_EditorClassIdentifier` strings inside `.unity`/`.asset` YAML (these are
   editor display cosmetics, regenerated automatically the next time Unity
   saves the scene/asset — not required reading for this task, just don't
   mistake them for a missed rename).
3. Grep for `ResourceBarsUi` — should have zero hits (fully removed/renamed).
4. Confirm `Assets/Scripts/Ui/` final tree matches:
   ```
   Assets/Scripts/Ui/
     PlayGround.Ui.asmdef
     Hud/
       GameplayInputSurface.cs
       ResourceBarUi.cs
       ResourceBarUi.uss
       SkillLoadoutUi.uss
       SkillLoadoutUi.uxml
       SkillLoadout/
         SkillLoadoutUi.cs
         SkillUiCatalog.cs
         SkillUiSupportCatalog.cs
         SkillUiTriggerCatalog.cs
         SkillNodeColumn.uxml
         SkillPicker.uxml
         SkillPickerChoice.uxml
         SkillSupportButton.uxml
         SkillTriggerButton.uxml
   ```
5. Confirm `Assets/Scripts/SkillUi/` and `Assets/Scripts/Ui/ResourceBars/`
   no longer exist.
6. Report to the user: open the project in Unity once to let it regenerate
   `play-ground.slnx`/`.csproj` files (auto-generated, do not hand-edit) and
   confirm the console shows no missing-script or compile errors on the
   `GameUI` GameObject in `BenchmarkLarge.unity`.

## Dependencies

Runs last, after all other tasks.
