---
name: 001-move-player-save-controller
description: Relocate PlayerSaveController.cs out of Ui into Persistence
---

# Move PlayerSaveController.cs to Persistence

## Scope

Move the one non-UI file out of `Assets/Scripts/SkillUi/` before the rest of
that folder becomes `Ui/`.

## Changes

1. Move `Assets/Scripts/SkillUi/PlayerSaveController.cs` (+ its `.meta`,
   guid `86d95e319c5b4c16bcaa094037e53c22`) to
   `Assets/Scripts/Persistence/PlayerSaveController.cs`.
2. In the moved file, change `namespace PlayGround.Skills` to `namespace
   PlayGround.Persistence` — matches its new neighbors `PlayerSaveStore.cs`
   and `PlayerSaveData.cs`, both already `namespace PlayGround.Persistence`.
3. `PlayerSaveController` references `PlayGround.Skills` types
   (`SkillDriver`, `SkillLoadoutRestoreNode`, `Skill`, `SkillSupport`,
   `TriggerLink`, `SkillSet`) and `PlayGround.Player.PlayerRoot` — these stay
   as external references via `using` directives; only the file's own
   namespace changes. It still references `SkillUiCatalog`
   (`PlayGround.Skills.SkillUiCatalog` after task 003 leaves that type's
   namespace untouched) — keep the existing `using PlayGround.Skills;` (or
   whichever namespace `SkillUiCatalog` ends up in — confirm task 003 hasn't
   changed it, since this task lands first).
4. No other `.cs` file references `PlayerSaveController` by type (confirmed:
   only the scene file and the doc mention it, neither needs a namespace-
   qualified reference) — no downstream edits required.

## Acceptance criteria

- `Assets/Scripts/SkillUi/PlayerSaveController.cs` and its `.meta` no longer
  exist.
- `Assets/Scripts/Persistence/PlayerSaveController.cs` exists with guid
  unchanged (`86d95e319c5b4c16bcaa094037e53c22`) and `namespace
  PlayGround.Persistence`.
- Project still compiles conceptually (same `using` set, only the
  `namespace` line and file location changed).

## Dependencies

None — first task, independent of the rest.
