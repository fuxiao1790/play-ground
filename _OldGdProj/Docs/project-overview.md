# Project Overview

## Summary

`play-ground` is a small Godot 4.6 .NET top-down prototype built around a C# gameplay core, a camera that biases toward the mouse, timer-driven mob spawning, and event-driven mob behaviour.
The project uses a 1280x720 base viewport with `canvas_items` stretch and 16:9 aspect preservation, so the gameplay world keeps the original single-viewport physics/input model while text is rendered directly at the target resolution.

The current playable loop is:

- Load `scenes/level/main.tscn`
- Move the player with WASD
- Dash with Space
- Face the mouse cursor
- Shoot with left click or by holding left mouse
- Zoom the camera with the mouse wheel
- Spawn mobs at configured spawn points
- Let mobs wander, detect the player, chase, react to damage, attack, and die

The basic mob behaviour loop is implemented.

Gameplay packages for player, attack, audio, mob, spawning, camera, and level are
implemented in C# under `scripts_cs/`. Headless smoke coverage lives under
`tests/`.

## Entry Scene

The main scene is [`scenes/level/main.tscn`](../scenes/level/main.tscn). It contains:

- `Camera2D`: custom follow camera with mouse bias, zoom, wall-sized release limits, and expanded debug-build limits
- `PlayAreaWall`: scene-owned 1280x720 tiled boundary that blocks players and mobs
- `Player`: instance of [`scenes/player/player.tscn`](../scenes/player/player.tscn)
- `DebugLayer/DebugLabel`: on-screen player debug text
- `MobSpawnner`: root spawner node
- `SpawnPoint_A` and `SpawnPoint_B`: timer-driven spawn points

## Runtime Technology

- Godot .NET project: [`play-ground.csproj`](../play-ground.csproj)
- Main solution: [`play-ground.sln`](../play-ground.sln)
- C# gameplay namespaces:
  - `PlayGround.Player`
  - `PlayGround.Mob`
  - `PlayGround.Spawn`
  - `PlayGround.Camera`
  - `PlayGround.Level`
  - `PlayGround.Common`
- Gameplay one-shot sounds are manager-owned through the `AudioManager` autoload,
  which reuses 2D audio players and culls duplicate currently playing sounds.

## Controls

- `W`: move up
- `A`: move left
- `S`: move down
- `D`: move right
- `Space`: dash in the current movement direction, or toward the mouse when idle
- Left mouse: shoot
- Hold left mouse: repeat fire
- Mouse position: controls facing
- Mouse wheel up/down: camera zoom

## Physics Layers

- player body against mobs and environment: layer `1` (`1`)
- player hitbox: layer `2` (`2`)
- player projectile: layer `3` (`4`)
- player AOE reserve: layer `4` (`8`)
- mob body against players and environment: layer `11` (`1024`)
- mob hitbox against player attacks: layer `12` (`2048`)
- mob projectile: layer `13` (`4096`)
- mob AOE reserve: layer `14` (`8192`)

Current wall collision stays on layer `2`, so player and mob body masks include
the wall layer plus the opposing body layer. Player and mob roots are
`RigidBody2D` nodes with gravity disabled and rotation locked, so Godot's
physics solver handles body separation against mobs, players, and walls.

## Current Feature Status

Implemented now:

- Player movement with acceleration, friction, and speed cap
- Player dash with cooldown and directional fallback toward the mouse
- Player facing toward the mouse
- Player click/hold shooting
- Centralized one-shot audio playback with same-sound simultaneous culling
- Player animation state switching between idle and walk
- C# player root and focused C# player helpers
- C# camera follow with oval boundary, mouse bias, zoom, and debug-build wall-limit override
- C# scene-defined tiled play-area wall with physics collision for players and mobs
- 1280x720 base viewport with 16:9 aspect preservation and target-resolution text rendering
- Player attack loadout support with multiple equipped attacks firing simultaneously
- Timed mob spawning from scene-configured spawn points
- Mob idle, walk, and hurt animation definitions
- Debug overlays for the player and optional mob state labels
- C# event-driven mob behaviour FSM separate from the animation FSM
- Scene-composed mob trigger resources, behaviour resources, and trigger maps
- C# spawn root, spawn points, coordinator hook, and shared spawn config resource
- Scoped projectile runtime with a pure `ProjectileWorld`, Godot adapter roots,
  scene-baked hit shapes, multimesh rendering, tracking, and snapshotted
  projectile damage
- Collision-driven projectile damage for player-to-mob and mob-to-player shots
- Scoped AOE runtime with scene-baked shapes, target-group sync, spatial-hash
  target queries, pulse damage, lingering tick damage, and projectile-triggered
  impact explosions

Scaffolded but incomplete:

- Rich mob attack patterns
- Full player damage feedback/death handling

## Folder Guide

- `scripts_cs/Player/`: player root orchestration plus movement, facing, shooting, and animation helpers
- `scripts_cs/Audio/`: centralized gameplay one-shot sound playback and duplicate culling
- `scripts_cs/Mob/`: shared mob root, mob animation helper, behaviour FSM helpers, triggers, and reusable behaviour resources
- `scripts_cs/Spawn/`: spawn root, spawn points, shared config resource, and coordinator extension hook
- `scripts_cs/Camera/`: camera follow, mouse bias, zoom, debug drawing, and debug-build limit override
- `scripts_cs/Level/`: play-area wall drawing and collision setup
- `scripts_cs/Common/`: reusable shared C# helpers
- `tests/`: headless smoke tests
- `scenes/mobs/`: mob scene variants
- `assets/`: imported art and audio assets
- `data/environment/`: tileset resource
- `docs/`: project documentation and review notes

Key docs in `docs/`:

- [`project-overview.md`](./project-overview.md): project summary and current feature status
- [`architecture.md`](./architecture.md): runtime structure and subsystem relationships
- [`coding-standards.md`](./coding-standards.md): C# scene-object conventions
- [`projectile-system.md`](./projectile-system.md): current state of the scoped projectile runtime package
- [`aoe-system.md`](./aoe-system.md): current state of the scoped AOE runtime package
- [`mob-behaviour.md`](./mob-behaviour.md): event-driven mob behaviour design
- [`release.md`](./release.md): Windows export notes
- [`gameplay.md`](./gameplay.md): gameplay loop direction and open design ideas

## Scene Notes

The player scene is [`scenes/player/player.tscn`](../scenes/player/player.tscn). It defines:

- `RigidBody2D` root
- Main collision shape
- `Hurtbox` `Area2D`
- `AnimatedSprite2D` with `idle`, `walk`, `jump`, and `stagger`
- C# root [`scripts_cs/Player/Player.cs`](../scripts_cs/Player/Player.cs)

Mob scenes currently present:

- [`scenes/mobs/bat.tscn`](../scenes/mobs/bat.tscn)
- [`scenes/mobs/slime.tscn`](../scenes/mobs/slime.tscn)
- [`scenes/mobs/skeleton.tscn`](../scenes/mobs/skeleton.tscn)

## Verification

Current smoke coverage:

- `display_contract_smoke`
- `play_area_wall_smoke`
- `audio_manager_smoke`

Local note: headless Godot on this machine is run with explicit `--log-file`
paths for smoke tests because the default `user://logs` path has been unstable.
