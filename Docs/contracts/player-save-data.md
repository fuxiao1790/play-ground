# Player Save Data

## Purpose

Persist low-count player state across application restarts without serializing
Unity runtime objects, ECS entities, compiled skill definitions, or cooldowns.

## Ownership

- `PlayerSaveController` owns save/load timing and assembles the complete DTO.
- `PlayerSaveStore` owns version validation and JSON file replacement under
  `Application.persistentDataPath`.
- `PlayerRoot` owns capture and restoration of player position and health.
- `SkillDriver` owns reconstruction and compilation of the runtime loadout.
- Authored `Skill`, `SkillSupport`, and `TriggerLink` assets own a baked Unity
  asset GUID used only as their persistent identity.

## Version 1 Shape

The top-level `PlayerSaveData` contains:

- format version;
- 2D player position and current health;
- ordered skill loadout nodes;
- asset GUID strings for each skill, support slot, and outgoing trigger.

Empty fields use an empty string. Support arrays preserve empty slots. Runtime
clones, catalog-order IDs, cooldown progress, status effects, and ECS state are
not serialized.

## Load Flow

1. Read and validate `player-save.json` from
   `Application.persistentDataPath`.
2. Restore player position and health. A saved dead player restarts at maximum
   health so a shutdown after death cannot create a dead-on-load loop.
3. Resolve asset GUIDs from the authored default loadout plus the UI catalog.
4. Ask `SkillDriver` to rebuild runtime `SkillSet` clones and compile the
   restored loadout. Each resolved skill asset supplies its authored maximum
   support count, so that value does not need a separate save field.
5. If the file is missing, corrupt, from an unsupported version, or references
   removed content, keep authored defaults and report a warning.

## Save Timing

The configured gameplay scene saves:

- immediately after an accepted loadout edit;
- every 30 seconds using unscaled time;
- when the application pauses or quits.

`OnDisable` performs event cleanup. `OnDestroy` must not capture or save player
state because referenced gameplay objects may already have completed teardown.

Writes use a temporary file followed by replacement so an interrupted write
does not partially overwrite the last complete save.
