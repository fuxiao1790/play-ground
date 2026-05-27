# Architecture

## Runtime Shape

The project leans on small C# orchestration scripts backed by focused helper classes.

The high-level runtime path is:

1. `scenes/level/main.tscn` loads the play-area wall scene, camera, player, debug layer, and projectile roots.
2. The player updates movement, facing, and animation state every physics frame.
3. The player attack loadout can request one-shot sounds.
4. Spawn points fire timers and ask the spawner root to create mob instances.
5. Mob scenes initialize animation state and run a separate event-driven behaviour FSM.

## Scene And Script Ownership

Use this section as the central map for scene-to-script ownership and dependency
direction. Detailed coding rules stay in [`coding-standards.md`](./coding-standards.md);
this section explains which runtime pieces own which scenes, scripts, and
configuration.

General rule:

- each gameplay object has one root scene node with one root script
- the root script owns node references, exported scene configuration, setup
  validation, signal wiring, subsystem creation, and update order
- focused helper classes, child nodes, or resources own gameplay decisions and
  reusable behavior
- scene files own authored composition such as child nodes, exported resources,
  collision shapes, animation frames, projectile templates, and configured
  loadouts
- runtime systems should depend on typed scene references and exported fields,
  not string-built lookups or hidden global discovery

Current ownership map:

- [`scenes/level/main.tscn`](../scenes/level/main.tscn): entry composition root;
  owns the level instances for camera, play-area wall, player, debug layer,
  and projectile roots
- [`scenes/level/play_area_wall.tscn`](../scenes/level/play_area_wall.tscn)
  with [`scripts_cs/Level/PlayAreaWall.cs`](../scripts_cs/Level/PlayAreaWall.cs):
  wall scene owns exported bounds, tile sizing, collision layer, tile
  textures; root script builds rendering and collision bands from that scene
  configuration
- [`scenes/player/player.tscn`](../scenes/player/player.tscn) with
  [`scripts_cs/Player/Player.cs`](../scripts_cs/Player/Player.cs): player scene
  owns body shape, hurtbox, sprite, and equipped attack children; root script
  orchestrates movement, facing, animation, state, and attack helpers
- attack child scenes under the player with
  [`scripts_cs/Attack/ProjectileAttack.cs`](../scripts_cs/Attack/ProjectileAttack.cs):
  each projectile attack scene owns its projectile template, recovery, damage,
  sound, tracking, spread, count, jitter, optional impact AOE, and muzzle
  position through its own node transform. Projectile attacks may also compose
  child hit-effect components such as
  [`ProjectileStackExplosionEffect`](../scripts_cs/Attack/ProjectileStackExplosionEffect.cs),
  while per-mob enum-indexed stack counts are stored by the mob root helper
- mob scenes in [`scenes/mobs/`](../scenes/mobs/) with
  [`scripts_cs/Mob/Mob.cs`](../scripts_cs/Mob/Mob.cs): each mob scene owns its
  animation frames, health values, trigger resources, behavior resources, and
  trigger-to-behavior maps; root script coordinates health, animation, local
  events, behavior state, and enum-indexed debuff stack state through
  [`MobDebuffStackState`](../scripts_cs/Mob/MobDebuffStackState.cs)
- spawn nodes with [`scripts_cs/Spawn/MobSpawnerRoot.cs`](../scripts_cs/Spawn/MobSpawnerRoot.cs),
  [`scripts_cs/Spawn/SpawnPoint.cs`](../scripts_cs/Spawn/SpawnPoint.cs), and
  [`scripts_cs/Spawn/SpawnConfig.cs`](../scripts_cs/Spawn/SpawnConfig.cs):
  spawn points own local timing, overlap checks, and optional mob scene lists;
  the spawner root owns global spawn caps and final mob instantiation; shared
  config resources can own reusable mob scene pools
- projectile runtime roots with
  [`scripts_cs/System/Projectile/Root.cs`](../scripts_cs/System/Projectile/Root.cs):
  each root owns one scoped projectile flow, target group discovery, projectile
  scene baking, target hurtbox baking, listener maps, event replay, and renderer
  coordination; [`ProjectileWorld`](../scripts_cs/System/Projectile/ProjectileWorld.cs)
  owns only plain runtime data and simulation
- AOE runtime roots with
  [`scripts_cs/System/Aoe/AoeRoot.cs`](../scripts_cs/System/Aoe/AoeRoot.cs):
  each root owns one scoped AOE target group, AOE scene baking, target sync,
  optional effect scene lifetime, hit replay, and spawn requests;
  [`AoeWorld`](../scripts_cs/System/Aoe/AoeWorld.cs) owns plain runtime data,
  spatial-hash target queries, pulse hits, lingering tick intervals, and target
  re-entry gates
- standalone GPU VFX root with
  [`scripts_cs/System/Vfx/GpuVfxRoot.cs`](../scripts_cs/System/Vfx/GpuVfxRoot.cs):
  scene-level visual service for transient effects; gameplay adapters may submit
  semantic visual events, but this root owns only visual command staging,
  effect resolution, transient visual state, rendering, and diagnostics
- [`scripts_cs/Debug/Debug.cs`](../scripts_cs/Debug/Debug.cs): shared screen
  debug output owns aggregate scene-level text; gameplay objects may own only
  their own local debug widgets
- [`scripts_cs/Audio/AudioManager.cs`](../scripts_cs/Audio/AudioManager.cs):
  `AudioManager` autoload owns one-shot sound pooling and duplicate culling;
  gameplay scripts request playback but do not own audio player pools

When adding a new scene/script pair, add the ownership entry here first. Then
put detailed behavior notes in the system-specific doc only when the feature
needs deeper explanation.

## Player System

The player is coordinated by [`scripts_cs/Player/Player.cs`](../scripts_cs/Player/Player.cs).

Supporting pieces:

- [`scripts_cs/Player/PlayerMovement.cs`](../scripts_cs/Player/PlayerMovement.cs): input, acceleration, friction, dash, rigid-body velocity
- [`scripts_cs/Player/PlayerFacing.cs`](../scripts_cs/Player/PlayerFacing.cs): flip or rotate toward the mouse
- [`scripts_cs/Player/PlayerAttack.cs`](../scripts_cs/Player/PlayerAttack.cs): attack scene loadout and per-frame attack updates
- [`scripts_cs/Player/PlayerStateDriver.cs`](../scripts_cs/Player/PlayerStateDriver.cs): locomotion state decisions for the FSM
- [`scripts_cs/Player/PlayerAnimator.cs`](../scripts_cs/Player/PlayerAnimator.cs): animation requests with priority handling
- [`scripts_cs/Common/StateMachineCore.cs`](../scripts_cs/Common/StateMachineCore.cs): reusable transition callback core

Player projectile attacks are loadout-driven:

- direct `ProjectileAttack` and `AoeAttack` children under the player are the
  equipped attacks
- `max_attack_count` caps how many child attacks are configured
- one held `primary_attack` input can perform every ready equipped attack in the same frame
- each attack owns its own recovery, projectile scene, sound, damage, tracking, spread, count, and jitter

Equipped attacks fire from their own attack node `GlobalPosition`, so separate player muzzles are modeled as separate attack nodes at separate local positions.

Each attack scene still owns attack-local volley shaping through `projectile_volley_spread_degrees`, `projectile_jitter_degrees`, and `projectile_count`.

Spread firing is built by [`scripts_cs/Attack/ProjectileVolleyBuilder.cs`](../scripts_cs/Attack/ProjectileVolleyBuilder.cs), which distributes projectile directions evenly across the authored cone and applies optional per-shot angle jitter.

For player muzzle offsets, prefer multiple equipped attack scenes. Use one attack's volley fields for a single attack pattern such as a fan, burst, or jitter shot.

## Audio System

[`scripts_cs/Audio/AudioManager.cs`](../scripts_cs/Audio/AudioManager.cs) owns gameplay one-shot sound requests through the `AudioManager` autoload. Gameplay code requests sounds with a stream, world position, priority placeholder, and same-sound simultaneous cap.

Before playing a request, the manager spaces starts for the same stream by a small interval. The default spacing is `0.04s`, so a sound requested every `0.02s` is smoothed to the same audible cadence as a `0.04s` attack sound. Each stream is assigned one pooled `AudioStreamPlayer2D`, and the requested same-sound cap is applied to that player through Godot's built-in `max_polyphony`. The pool is capped by stream lane count so attacks cannot create unbounded audio nodes.

This is also the baseline example for the project coding standard in [`coding-standards.md`](./coding-standards.md): the preferred root-script role is wiring and orchestration such as transition registration, not gameplay-state decision logic.

## Camera System

[`scripts_cs/Camera/GameplayCamera.cs`](../scripts_cs/Camera/GameplayCamera.cs) extends `Camera2D` and provides:

- smooth following
- mouse-biased framing
- an oval boundary that keeps the player near screen center
- zoom control using the mouse wheel
- scene-authored camera limits for normal play
- debug-build limit expansion so the camera can inspect beyond level walls

`main.tscn` stores the normal camera limits alongside the `PlayAreaWall` bounds.
By default, `allow_beyond_limits_in_debug` expands those limits during
`OS.is_debug_build()` runs. This keeps exported/non-debug builds constrained to
the play area while allowing local debugging outside the wall.

## Play Area Wall

[`scenes/level/play_area_wall.tscn`](../scenes/level/play_area_wall.tscn)
defines the current 1280x720 arena boundary. Its root script,
[`scripts_cs/Level/PlayAreaWall.cs`](../scripts_cs/Level/PlayAreaWall.cs),
uses scene exports for the bounds, tile size, collision layer, and 3x3 wall tile
textures.

The wall renders a one-tile-thick rectangular border from `assets/Interface`
tiles and configures four `StaticBody2D` collision bands on physics layer `2`.
Player and mob movement code does not need wall-specific logic because their
existing collision masks include layer `2`. Player and mob bodies also collide
with each other as `RigidBody2D` roots, with gravity disabled and rotation
locked so Godot's physics solver handles body separation in the top-down plane.

## Resolution And UI

The project uses a 1280x720 base viewport with `canvas_items` stretch and
`keep` aspect mode. This preserves a 16:9 gameplay frame without moving physics
or spawning into a separate `SubViewport`, and text is rendered directly at the
target resolution instead of being upscaled from a low-resolution viewport texture.

## Mob System

[`scripts_cs/Mob/Mob.cs`](../scripts_cs/Mob/Mob.cs) coordinates:

- local behaviour events
- behaviour state
- animation state through the existing animation FSM
- debug drawing
- health and damage

[`scripts_cs/Mob/MobAnimator.cs`](../scripts_cs/Mob/MobAnimator.cs) mirrors the player animator pattern with `IDLE`, `WALK`, and `HURT` states.

Current behaviour baseline:

- mob scenes define animation frames and health values
- damage can be applied through `take_damage()`
- spawned mobs leave `IDLE`, wander, detect the player, chase, react to damage, and die
- behaviour is built from scene-configured trigger resources, reusable behaviour resources, and trigger-to-behaviour maps
- the behaviour FSM is separate from the existing animation FSM

The mob AI design is documented in [`mob-behaviour.md`](./mob-behaviour.md). It keeps the existing animation FSM intact and adds a separate behaviour FSM driven by local typed events, scene-configured triggers, and reusable behaviour resources.

## Spawn System

The spawning setup is split across:

- [`scripts_cs/Spawn/MobSpawnerRoot.cs`](../scripts_cs/Spawn/MobSpawnerRoot.cs): global spawn cap, spawn instantiation, coordinator hook
- [`scripts_cs/Spawn/SpawnPoint.cs`](../scripts_cs/Spawn/SpawnPoint.cs): timer, local overlap checks, spawn area debug drawing
- [`scripts_cs/Spawn/SpawnCoordinator.cs`](../scripts_cs/Spawn/SpawnCoordinator.cs): extension hook for custom spawn rules
- [`scripts_cs/Spawn/SpawnConfig.cs`](../scripts_cs/Spawn/SpawnConfig.cs): shared mob scene pool resource

The current scene uses point-local mob scene lists on each spawn point. Spawn points can also reference a shared `SpawnConfig`; when both are empty, the root-level mob scene list is used as a fallback.

## Data and Assets

- `assets/`: sprite sheets, tiles, and sounds
- `data/environment/tileset.tres`: environment tileset resource

There is no higher-level content registry yet beyond exported scene references.

## Implementation Status

Solid foundation:

- player movement architecture
- animation request pattern
- camera feel scaffolding
- spawn-point based scene composition
- shared spawn config resource usage

Missing gameplay glue:

- mob attacks and player damage
- game-state loop beyond free movement, spawning, and basic mob behaviour
