# Project Overview

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

## Summary

`play-ground` is a Unity 6000.4 2D URP top-down action game.

The long-term game identity is attack scaling pushed to extreme levels: many
projectiles, beams, lasers, AOEs, particles, chained effects, and screen-filling
spell combinations. Performance is a primary design constraint, not a later
optimization pass.

Core gameplay loop:

- move with WASD
- dash with Space
- face or aim at the mouse
- fire with left mouse, including hold-to-fire
- zoom camera with mouse wheel
- spawn mobs from authored spawn points
- let mobs wander, detect player, chase, react to damage, attack, and die
- support player projectiles, mob projectiles, AOE damage, and stack-triggered
  explosions
- scale attacks to very high entity and visual counts without collapsing frame
  rate

Current Unity state:

- Unity editor version: `6000.4.8f1`
- render path: Universal Render Pipeline 2D
- input package is installed
- Entities/DOTS is used for high-count combat simulation
- 2D animation, tilemap, SpriteShape, and Unity Test Framework packages are
  installed

## Runtime Rules

- Use Unity-native scenes, prefabs, MonoBehaviours, ScriptableObjects,
  Physics2D, pooling, and PlayMode/EditMode tests for low-count authored
  gameplay.
- Use data-oriented ECS/DOTS runtimes for high-count projectiles, AOEs, future
  beams, combat VFX requests, and target proxy data.
- Keep the OOP/DOP boundary explicit. MonoBehaviours own authored references,
  Unity object lifetimes, and gameplay callbacks. ECS systems own scalable
  simulation, pooling, event queues, and render preparation.
- Snapshot combat data before spawn. Runtime ECS entities should carry the data
  they need instead of reaching back into ScriptableObjects, prefabs, or live
  actor state.
- Use target proxy entities for projectile and AOE collision. Do not use live
  trigger callbacks as the authoritative high-count hit path.
- Use generic `Active` enable/disable for projectile and AOE slot reuse.
- Keep managed target callback access restricted to `DamageDispatchBridge`.

## Runtime Target

Main Unity scene should be:

- `Assets/Scenes/Main.unity`

It should contain:

- `GameRoot`: scene-level composition and service references
- `GameplayCamera`: orthographic follow camera with mouse bias and zoom
- `PlayArea`: configurable arena bounds and collision, with 1280x720 only as
  the current prototype default
- `Player`: player prefab instance
- `MobSpawnerRoot`: spawn cap and spawn point coordination
- `PlayerCombatRoot`: `CombatRoot` instance for player-fired projectiles and
  AOEs targeting mobs
- `MobCombatRoot`: `CombatRoot` instance for mob-fired projectiles and AOEs
  targeting the player
- `DebugOverlay`: shared top-left runtime debug text

## Controls

- `W`: move up
- `A`: move left
- `S`: move down
- `D`: move right
- `Space`: dash in movement direction, or toward mouse when idle
- left mouse: fire
- hold left mouse: repeat fire
- mouse position: aim/facing
- mouse wheel: camera zoom

Use Unity Input System actions for these bindings.

## Physics Layers

Use named Unity layers.

Required gameplay layers:

- `PlayerBody`
- `PlayerHurtbox`
- `PlayerProjectile`
- `PlayerAoe`
- `MobBody`
- `MobHurtbox`
- `MobProjectile`
- `MobAoe`
- `Environment`

Collision matrix intent:

- player body collides with mob bodies and environment
- mob bodies collide with player body, other mob bodies if desired, and
  environment
- player projectiles query mob hurtboxes through ECS target proxy data
- mob projectiles query player hurtboxes through ECS target proxy data
- player AOEs query mob hurtboxes through ECS target proxy data
- mob AOEs query player hurtboxes through ECS target proxy data

Player/mob/wall body collision remains Physics2D. Projectile and AOE gameplay
collision is data-oriented and uses baked proxy shapes.

## Feature Status

Implemented:

- spawn system root, spawn points, spawn config/pools, cap rules, and mob prefab
  selection
- player movement, dash, aim/facing, skill loadout, health/hurt handling,
  status hooks, and animation drivers
- mob roots with health, hurtboxes, proxy lifecycle, death cleanup, behavior FSM
  hooks, stack/status handling, and mob projectile attack support
- shared `CombatRoot` bridge for projectile and AOE authoring, managed spawn
  submission, target registries, render resources, and ECS world/scope lifetime
- target proxy bridge with `TargetPosition`, `TargetCollisionShape`,
  `TargetFaction`, and managed `TargetCompanion`
- projectile ECS runtime with Event -> Command expansion, basic and
  child-spawner apply systems, disabled-slot reuse, tracking, movement, timed
  child spawns, generic lifetime, contact gates, collision, damage/spawn/VFX
  event output, and batched rendering
- AOE ECS runtime with Event -> Command expansion, apply/reuse, generic
  lifetime, pulse VFX, contact gates, pulse and lingering collision,
  damage/projectile-burst/VFX event output, and batched rendering
- native damage transport through `DamageReplayEvent`,
  `DamageFinalizeSystem`, and `DamageDispatchBridge`
- VFX request buffering and dispatch through `CombatVfxRoot` and
  `CombatVfxDispatchSystem`

Not yet implemented or still thin:

- camera follow and zoom
- play area wall
- audio manager
- full gameplay smoke/stress coverage
- beam/laser runtime
- stronger damage aggregation before managed replay

## Performance-Critical Work

Performance work to keep visible as content grows:

- projectile and AOE stress scenes with clear counters and thresholds
- AOE visual-budget policies beyond the current batched path
- beam/laser runtime for continuous collision and rendering
- effect budget controls for particles, decals, and transient visuals
- explicit profiling counters for AOE, beam, spawn, mob, VFX, damage dispatch,
  and render cost
- damage aggregation so dense hit frames cross the ECS/Mono boundary closer to
  target count than raw hit count

## Locked Prototype Decisions

- first playable goal, win/loss, difficulty model, reward loop, loot fantasy,
  and base-building are intentionally undecided
- foundation and performance proof matter more than full gameplay loop right now
- camera should frame local combat; minimap is planned later
- attacks are composable child components and skill definitions under the actor
- attack damage, crit, child spawns, impact AOEs/projectiles, and stack payloads
  are snapshotted before spawn
- projectile POC target: about 50k projectiles on screen with 20 targets at
  120 fps
- Jobs/Burst should be used for high-volume projectile and AOE work
- beam and laser gameplay is future work

## Key Docs

- [folder-structure.md](./folder-structure.md): file map
- [architecture.md](./architecture.md): runtime ownership and system shape
- [coding-standards.md](./coding-standards.md): Unity C# rules
- [design/gameplay.md](./design/gameplay.md): gameplay loop and open design
- [game-logic/skill-system.md](./game-logic/skill-system.md): skill and support
  model
- [simulation/projectile-system.md](./simulation/projectile-system.md):
  projectile runtime
- [simulation/aoe-system.md](./simulation/aoe-system.md): AOE runtime
- [simulation/vfx-system.md](./simulation/vfx-system.md): VFX runtime
- [game-logic/mobs.md](./game-logic/mobs.md): mob runtime and content target
- [game-logic/mob-behaviour.md](./game-logic/mob-behaviour.md): mob AI design
- [testing.md](./testing.md): Unity test approach
- [release.md](./release.md): Unity Windows build notes
