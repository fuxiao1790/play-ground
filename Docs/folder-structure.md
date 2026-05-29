# Folder Structure

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

Quick map.

## Current Folders

- `Assets/`: Unity assets, scenes, prefabs, scripts, art, audio, settings
- `Assets/Scenes/`: Unity scene files
- `Assets/Settings/`: URP and project settings assets
- `Docs/`: design docs
- `Packages/`: Unity package manifest and lock files
- `ProjectSettings/`: Unity project settings

## Target Asset Folders

Create these as systems are ported:

- `Assets/Scripts/`: runtime C# code
- `Assets/Scripts/Common/`: shared helpers, damage payloads, state machine core
- `Assets/Scripts/Player/`: player root, movement, facing, animation, attack loadout
- `Assets/Scripts/Attack/`: attack MonoBehaviours and attack authoring code
- `Assets/Scripts/Mob/`: mob root, state, triggers, behaviours, attacks
- `Assets/Scripts/Spawn/`: spawn root, spawn points, spawn config
- `Assets/Scripts/Camera/`: camera follow, mouse bias, zoom
- `Assets/Scripts/Level/`: play area wall and arena setup
- `Assets/Scripts/Audio/`: gameplay audio manager and pooled one-shots
- `Assets/Scripts/Debugging/`: shared debug overlay
- `Assets/Scripts/System/Projectile/`: scoped projectile runtime
- `Assets/Scripts/System/Aoe/`: scoped AOE runtime
- `Assets/Scripts/System/Vfx/`: optional batched VFX runtime
- `Assets/Prefabs/Player/`: player prefab and child attack loadout prefabs
- `Assets/Prefabs/Mobs/`: bat, slime, skeleton, and later mob prefabs
- `Assets/Prefabs/Attacks/`: projectile and AOE attack prefabs
- `Assets/Prefabs/Projectiles/`: authored projectile visual/collision templates
- `Assets/Prefabs/Effects/`: AOE effect visuals and transient effects
- `Assets/Prefabs/Level/`: play area and environment prefabs
- `Assets/ScriptableObjects/`: shared authored data
- `Assets/ScriptableObjects/Mobs/`: mob behavior and trigger configs
- `Assets/ScriptableObjects/Spawn/`: spawn pools and spawn tuning
- `Assets/Art/`: sprites, tiles, animation source
- `Assets/Audio/`: sound effects and music
- `Assets/Tests/EditMode/`: pure and editor tests
- `Assets/Tests/PlayMode/`: scene/runtime tests

## Good First Places To Look

- Want project summary: [project-overview.md](./project-overview.md)
- Want architecture: [architecture.md](./architecture.md)
- Want coding rules: [coding-standards.md](./coding-standards.md)
- Want player attacks: [player-attacks.md](./player-attacks.md)
- Want projectiles: [projectile-system.md](./projectile-system.md)
- Want AOEs: [aoe-system.md](./aoe-system.md)
- Want mobs: [mobs.md](./mobs.md) and [mob-behaviour.md](./mob-behaviour.md)
- Want spawning: [spawn-system.md](./spawn-system.md)
- Want tests: [testing.md](./testing.md)

## Current Projectile Runtime Map

- `Assets/Scripts/System/Projectile/ProjectileRoot.cs`: scene-object bridge,
  target snapshot sync, hit replay, child spawn request replay, counters, and
  batched render submission
- `Assets/Scripts/System/Projectile/ProjectileSimulationSystem.cs`: per-scope
  projectile event buffer clearing at the start of simulation
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`: homing target
  refresh, reacquire, and steering
- `Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs`: position
  integration
- `Assets/Scripts/System/Projectile/ProjectileChildSpawnSystem.cs`: timed child
  spawn request events
- `Assets/Scripts/System/Projectile/ProjectileLifetimeSystem.cs`: lifetime
  countdown and active-state disable for expired projectiles
- `Assets/Scripts/System/Projectile/ProjectileContactGateSystem.cs`: repeat-hit
  contact gate expiry
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`: target mask
  filtering, shape hit checks, pierce, and hit events
- `Assets/Scripts/System/Projectile/ProjectileCollisionMath.cs`: pure
  circle/box/capsule narrow-phase math
- `Assets/Scripts/System/Projectile/ProjectileRenderPrepareSystem.cs`:
  late-simulation render matrix preparation for scoped projectile draw
  submission

