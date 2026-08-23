# Game Settings Data

## Purpose

Persist low-count user options independently from player progress.

## Ownership

- `GameSettings` under `Assets/Scripts/Game/` owns runtime state, change events,
  and application-level file location.
- `GameSettingsData` under `Assets/Scripts/Persistence/` owns versioned plain
  data and authored defaults.
- `GameSettingsStore` under `Assets/Scripts/Persistence/` owns JSON validation
  and atomic file replacement.
- Options UI sends changes to `GameSettings`; presentation consumers subscribe
  to its focused change events.

## Version 1 Shape

`GameSettingsData` contains:

- format version;
- whether mob health bars are displayed.

Mob health bars default to displayed. Settings are stored in
`Application.persistentDataPath/game-settings.json` and are not part of
`player-save.json`, so resetting progress does not reset user preferences.

## Runtime Flow

1. `GameSettings` loads once from `game-settings.json`, falling back to defaults
   when the file is missing or invalid.
2. `PauseMenuUi` projects the current value into its toggle and submits changes
   through `SetDisplayMobHealthBars`.
3. Accepted changes save immediately and publish
   `DisplayMobHealthBarsChanged`.
4. `MobResourceBarUi` releases existing markers when bars are disabled and skips
   target iteration until they are enabled again.
