# Folder Structure

Quick guide for where stuff lives.

## Main Project Folders

- `assets/`: source art, textures, audio, and imported asset files
- `data/`: shared data resources; environment setup lives in `data/environment/`
- `docs/`: project docs, architecture notes, gameplay notes, and coding rules
- `scenes/`: Godot scenes grouped by feature or game object
- `scripts_cs/`: main C# gameplay code grouped by system
- `tests/`: headless smoke tests written in GDScript
- `builds/`: exported game builds
- `.profiles/`: captured profiling output

## Scene Folders

- `scenes/level/`: main level scene and level-specific setup
- `scenes/player/`: player scene
- `scenes/mobs/`: mob scene variants like bat, slime, and skeleton
- `scenes/attacks/`: attack-related scenes
- `scenes/projectiles/`: projectile scenes

## C# Code Folders

- `scripts_cs/Player/`: player root script plus movement, facing, animation, and attack helpers
- `scripts_cs/Mob/`: mob root script, behaviour logic, animation helpers, triggers, and resources
- `scripts_cs/Projectile/`: projectile runtime and projectile-specific helpers
- `scripts_cs/System/Aoe/`: scoped AOE runtime and adapter code
- `scripts_cs/System/Vfx/`: standalone visual effect renderer and effect definitions
- `scripts_cs/Attack/`: attack definitions and attack flow code
- `scripts_cs/Spawn/`: spawn root, spawn points, and spawn config
- `scripts_cs/Camera/`: camera follow, zoom, and debug camera behavior
- `scripts_cs/Level/`: level-specific runtime code like play area setup
- `scripts_cs/Audio/`: shared gameplay audio manager code
- `scripts_cs/Debug/`: shared debug overlay ownership
- `scripts_cs/Common/`: reusable shared helpers and shared combat damage payloads
- `scripts_cs/System/`: cross-system runtime helpers and coordination code

## Good First Places To Look

- Want game entry point: `scenes/level/main.tscn`
- Want player logic: `scenes/player/` and `scripts_cs/Player/`
- Want mob logic: `scenes/mobs/` and `scripts_cs/Mob/`
- Want projectile or attack flow: `scenes/projectiles/`, `scripts_cs/Projectile/`, and `scripts_cs/Attack/`
- Want AOE flow: `scenes/attacks/basic_aoe_attack.tscn`, `scripts_cs/System/Aoe/`, and `docs/aoe-system.md`
- Want tests: `tests/`
- Want project rules: `docs/project-overview.md` and `docs/coding-standards.md`
