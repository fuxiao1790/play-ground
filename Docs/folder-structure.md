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
- `Assets/Scripts/System/Common/`: shared combat ECS data, shape baking, bounds,
  and narrow-phase collision helpers used by projectile and AOE runtimes
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

- `Assets/Scripts/System/Common/CombatEcsComponents.cs`: shared
  `CombatKinematicsComponent`, `CombatCollisionComponent`, `CombatHitComponent`,
  and `CombatTargetElement` data used by projectile now and AOE later
- `Assets/Scripts/System/Common/CombatShapeType.cs`: shared circle, rectangle,
  and capsule shape enum
- `Assets/Scripts/System/Common/CombatTargetShapeUtility.cs`: shared collider
  shape baking from Unity `Collider2D`
- `Assets/Scripts/System/Common/CombatCollisionMath.cs`: shared bounds and
  narrow-phase collision math
- `Assets/Scripts/System/Projectile/ProjectileRoot.cs`: scene-object bridge,
  spawn request submission, target snapshot sync, hit replay, raw payload
  dispatch, counters, and batched render submission
- `Assets/Scripts/System/Projectile/ProjectileSimulationSystem.cs`: per-scope
  projectile event buffer clearing at the start of simulation
- `Assets/Scripts/System/Projectile/ProjectileSpawnSystem.cs`: per-scope
  projectile recycle-buffer draining, spawn request materialization, inactive
  entity reuse by scope/render type/slot kind, and cold entity creation through
  ECB
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`: homing target
  refresh, reacquire, and steering
- `Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs`: position
  integration
- `Assets/Scripts/System/Projectile/ProjectileChildSpawnSystem.cs`: timed child
  projectile spawn request creation
- `Assets/Scripts/System/Projectile/ProjectileRecycleFlushJob.cs`: native queue
  flush into scoped projectile recycle buffers
- `Assets/Scripts/System/Projectile/ProjectileLifetimeSystem.cs`: lifetime
  countdown, active-state disable, and recycle record enqueue for expired
  projectiles
- `Assets/Scripts/System/Projectile/ProjectileContactGateSystem.cs`: repeat-hit
  contact gate expiry
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`: target mask
  filtering, shape hit checks, pierce, hit events, and recycle record enqueue
  for hit-despawned projectiles
- `Assets/Scripts/System/Projectile/ProjectileCollisionMath.cs`: projectile
  compatibility adapter over shared common collision math
- `Assets/Scripts/System/Projectile/ProjectileRenderPrepareSystem.cs`:
  late-simulation render matrix preparation for scoped projectile draw
  submission

## Current AOE Runtime Map

- `Assets/Scripts/System/Aoe/AoeRoot.cs`: scene-object bridge, AOE type baking,
  spawn request submission, target snapshot sync, hit replay, counters, and
  batched render submission
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`: AOE scope, tag, identity,
  active, spawn, hit, recycle, contact-gate, and render ECS data
- `Assets/Scripts/System/Aoe/AoeSimulationSystem.cs`: per-scope AOE hit-buffer
  clearing at the start of simulation
- `Assets/Scripts/System/Aoe/AoeSpawnSystem.cs`: per-scope AOE recycle-buffer
  draining, spawn request materialization, inactive entity reuse by scope/type,
  and cold entity creation
- `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs`: target mask filtering,
  bounds/narrow-phase collision, hit events, and pulse-AOE recycle records
- `Assets/Scripts/System/Aoe/AoeRenderPrepareSystem.cs`: late-simulation render
  matrix preparation for batched AOE draw submission

