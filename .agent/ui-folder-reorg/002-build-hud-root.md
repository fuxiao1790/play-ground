---
name: 002-build-hud-root
description: Create Ui/Hud/, move the shared HUD root + its directly-bound controllers into it, delete the dead ResourceBars template
---

# Build the Hud/ Root

## Scope

Create the shared-root folder and move in everything that binds directly to
the single `GameUI` UIDocument (per scene inspection: `SkillLoadoutUi.uxml`
is the shared source asset for `SkillLoadoutUi`, `PlayerSaveController`,
`GameplayInputSurface`, `ResourceBarUi`, and `PerformanceUi`, all on one
GameObject). `PlayerSaveController` already moved in task 001;
`PerformanceUi` is already mid-move to `Debugging/` outside this task's
scope. That leaves `GameplayInputSurface.cs` and `ResourceBarUi.cs` as the
controllers that belong directly in `Hud/`.

## Changes

1. Create `Assets/Scripts/Ui/Hud/` (new folder; Unity will generate its
   `.meta` on next editor open, or create one with a fresh guid now).
2. Move into `Assets/Scripts/Ui/Hud/`:
   - `Assets/Scripts/SkillUi/GameplayInputSurface.cs` (+ `.meta`, guid
     `080bd4e616de6004b80a91227b8ec8d7`)
   - `Assets/Scripts/SkillUi/ResourceBarUi.cs` (+ `.meta`, guid
     `ae79fab3554323549b5f5d13c4cd90d5`)
   - `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uxml` (+ `.meta`, guid
     `fa59a93ceeaacf04da355ce7974f0785`)
   - `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss` (+ `.meta`, guid
     `a59d1f049fab38e4996311a621821222`)
3. Delete `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml` and its
   `.meta` (guid `5c2470a811093a24ca139e68fbee7483`) — confirmed dead: no
   `UIDocument` or `VisualTreeAsset` field references it anywhere in the
   project.
4. Move + rename `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uss` (+
   `.meta`, guid `36c63ba08153bd54f8decd4962bc2ae8`) to
   `Assets/Scripts/Ui/Hud/ResourceBarUi.uss` — naming matches
   `ResourceBarUi.cs`; content unchanged.
5. Delete the now-empty `Assets/Scripts/Ui/ResourceBars/` folder and its
   `.meta` (guid `66f52c14af78a2a4dab795f526b8594f`).
6. In `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`, update the two `<Style
   src="...">` lines for the new folder depth (both files are now siblings
   in `Hud/`):
   - `<Style src="SkillLoadoutUi.uss" />` — unchanged (still same folder).
   - `<Style src="../ResourceBars/ResourceBarsUi.uss" />` → `<Style
     src="ResourceBarUi.uss" />`.

## Acceptance criteria

- `Assets/Scripts/Ui/Hud/` contains `GameplayInputSurface.cs`,
  `ResourceBarUi.cs`, `SkillLoadoutUi.uxml`, `SkillLoadoutUi.uss`,
  `ResourceBarUi.uss` — each with its original guid preserved.
- `Assets/Scripts/Ui/ResourceBars/` no longer exists.
- `SkillLoadoutUi.uxml`'s two `<Style src>` lines resolve correctly from its
  new location (both same-folder references).
- No `.cs`/`.uxml`/`.uss` content changed except the two `Style src` lines
  and file locations.

## Dependencies

Independent of task 001. Must land before task 003 (which further nests
`SkillLoadout/` under this `Hud/` folder) and task 004 (asmdef move assumes
`Ui/` no longer needs `SkillUi/` for these files).
