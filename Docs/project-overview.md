# Project Overview

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

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
- support player projectiles, mob projectiles, AOE damage, and stack-triggered explosions
- scale attacks to very high entity and visual counts without collapsing frame rate

Current Unity state:

- Unity editor version: `6000.4.8f1`
- render path: Universal Render Pipeline 2D
- input package is installed
- 2D animation, tilemap, SpriteShape, and Unity Test Framework packages are installed

Runtime rules:

- implement runtime using Unity-native scenes, prefabs, MonoBehaviours, ScriptableObjects, Physics2D, pooling, and PlayMode/EditMode tests
- design combat systems around pooled objects, batched rendering, allocation-light
  updates, and plain-data simulation boundaries from the start
- hybrid model: low-count actors live as scene objects, while high-count projectile,
  AOE, and beam gameplay lives in data-oriented runtimes
 - project mixes OOP (MonoBehaviour) and DOP (ECS/DOTS); OOP-side code should
   use the simplest possible constructs (plain components, minimal callbacks,
   and minimal lifetime coupling) to avoid performance or lifetime clashes with
   DOTS/ECS systems.

## Runtime Target

Main Unity scene should become:

- `Assets/Scenes/Main.unity`

It should contain:

- `GameRoot`: scene-level composition and service references
- `GameplayCamera`: orthographic follow camera with mouse bias and zoom
- `PlayArea`: configurable arena bounds and collision, with 1280x720 only as the current prototype default
- `Player`: player prefab instance
- `MobSpawnerRoot`: spawn cap and spawn point coordination
- `ProjectileRoot_PlayerToMob`: scoped player projectile runtime
- `ProjectileRoot_MobToPlayer`: scoped mob projectile runtime
- `AoeRoot_PlayerToMob`: scoped player AOE runtime
- `AoeRoot_MobToPlayer`: scoped mob AOE runtime
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
- mob bodies collide with player body, other mob bodies if desired, and environment
- player projectiles query mob hurtboxes through runtime collision data
- mob projectiles query player hurtboxes through runtime collision data
- player AOEs query mob hurtboxes through runtime collision data
- mob AOEs query player hurtboxes through runtime collision data

The high-volume projectile and AOE systems should not depend on live trigger overlap callbacks for core hit detection. They should bake collider shape data and run data-oriented collision where practical.

High-scale effects should be split by responsibility:

- gameplay simulation decides hits, damage, lifetime, and chaining
- rendering displays pooled or batched visuals
- particles are visual-only unless an explicit gameplay system emits damage events
- lasers and beams use specialized continuous-hit runtimes instead of pretending to
  be many tiny projectiles

Hybrid runtime boundary:

- player and mobs stay as Unity GameObjects with Rigidbody2D/Collider2D
- expected target count is far below projectile count; player plus mobs below roughly `50` is an early performance target, not a hard design cap
- wall, player, and mob body movement/collision uses built-in Unity Physics2D
- projectile, AOE, and future beam gameplay simulation uses data-oriented runtime
  worlds
- projectile visuals should prove the batched path early; AOE visuals may start
  with pooled GameObjects and later add batched renderers, but
  their gameplay state is not owned by scene objects

## Feature Status

Implemented:

- spawn system root, spawn points, spawn config/pools, cap rules, and mob prefab
  selection
- player movement, dash, aim/facing, attack loadout, health/hurt handling, and
  basic animation drivers
- mob roots with health, hurtboxes, death cleanup, behavior FSM hooks, and mob
  projectile attack support
- scoped projectile runtime with split ECS stages for tracking, movement, child
  spawn requests, lifetime, contact gates, collision, hit replay, counters, and
  batched rendering by projectile type
- scoped DOTS AOE runtime with ECS scope entities, spawn/recycle buffers, target
  snapshots, pulse and lingering collision, hit replay, counters, and batched rendering by AOE type

Not yet implemented:

- camera follow and zoom
- play area wall
- audio manager
- full gameplay smoke/stress coverage

Performance-critical systems to design before content grows:

- AOE stress tuning and visual-budget policies beyond the current batched path
- laser/beam runtime for continuous collision and rendering
- effect budget controls for particles, decals, and transient visuals
- explicit profiling counters for AOE, beam, spawn, mob, and render cost beyond
  the current projectile counters

## Locked Prototype Decisions

- first playable goal, win/loss, difficulty model, reward loop, loot fantasy,
  and base-building are intentionally undecided
- foundation and performance proof matter more than full gameplay loop right now
- camera should frame local combat; minimap is planned later
- player health and death can wait until after the combat foundation is proven
- attacks are composable child components under the actor
- attack damage, crit, child spawns, and AOE payloads are snapshotted when the
  attack fires
- projectile POC target: about 50k projectiles on screen with 20 targets at 120 fps
- Jobs/Burst should be used for high-volume projectile work
- beam and laser gameplay is future work

## Key Docs

- [folder-structure.md](./folder-structure.md): target Unity folders
- [architecture.md](./architecture.md): runtime ownership and system shape
- [coding-standards.md](./coding-standards.md): Unity C# rules
- [gameplay.md](./gameplay.md): gameplay loop and open design
- [player-attacks.md](./player-attacks.md): player loadout and attack authoring
- [projectile-system.md](./projectile-system.md): projectile runtime target
- [aoe-system.md](./aoe-system.md): AOE runtime target
- [mobs.md](./mobs.md): mob runtime and content target
- [mob-behaviour.md](./mob-behaviour.md): mob AI design
- [testing.md](./testing.md): Unity test approach
- [release.md](./release.md): Unity Windows build notes
