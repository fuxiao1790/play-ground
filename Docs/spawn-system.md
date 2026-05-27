# Spawn System

All docs in `Docs/` are preliminary. They describe the current port intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Current Shape

`Assets/Scenes/Main.unity` owns a separate editable `MobSpawnerRoot` scene object.
It is not created by `GameRoot`.

Current scene composition:

- `MobSpawnerRoot`: owns global mob cap, final mob instantiation, target binding,
  projectile target registration, and fallback mob authoring while real prefabs
  are still being built.
- `SpawnPoint_East`, `SpawnPoint_West`, `SpawnPoint_North`, `SpawnPoint_South`:
  scene-authored child points that can be moved in the editor.
- `SpawnPoint`: owns local timer, spawn radius, local cap, spacing checks, and
  optional point-specific pool/config override.
- `MobSpawnPool`: ScriptableObject weighted prefab selection.
- `SpawnConfig`: ScriptableObject reusable point timing/radius/local-cap data.
- `SpawnCoordinator`: optional hook for higher-level spawn rules.

## Runtime Rules

- Spawner enforces `maxMobs` against alive spawned mobs.
- Spawn points can enforce `maxLocalMobs`.
- Spawn points choose pool in this order: point pool, point config pool, spawner
  fallback pool.
- Spawned mobs are activated after instantiate, registered with the player
  projectile target registry, and targeted at the player transform when assigned.
- Soft-dead mobs stop counting toward caps. Hard-destroyed mobs are also removed
  from tracking.
- Spawn points draw selected gizmo radius in the scene view.

## Temporary Fallback

Until authored mob prefabs and spawn pools are created, `MobSpawnerRoot` creates a
runtime fallback mob prefab using the assigned `runtimeMobSprite`. This exists so
the scene can spawn real `MobRoot` objects without relying on `GameRoot` or the
removed target dummy.

Replace this fallback by assigning real mob prefabs through `MobSpawnPool` assets
under `Assets/ScriptableObjects/Spawn/`.

## Old References

- `_OldGdProj/Script_Cs/Spawn/MobSpawnerRoot.cs`
- `_OldGdProj/Script_Cs/Spawn/SpawnPoint.cs`
- `_OldGdProj/Script_Cs/Spawn/SpawnConfig.cs`
- `_OldGdProj/Script_Cs/Spawn/SpawnCoordinator.cs`
