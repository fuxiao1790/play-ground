# Folder Structure

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

Quick map.

## Docs Layout

```text
Docs/
  project-overview.md       project vision and scope
  architecture.md           system ownership and runtime shape
  folder-structure.md       this file
  coding-standards.md       C# and Unity rules
  performance.md            performance constraints and stress tests
  profiling.md              profiling data reference
  snapshotting.md           snapshot and spawn safety model
  testing.md                test strategy and coverage goals
  release.md                Unity Windows build notes
  design/
    gameplay.md
  game-logic/
    skill-system.md
    mobs.md
    mob-behaviour.md
    spawn-system.md
  simulation/
    projectile-system.md
    aoe-system.md
    ecs-notes.md
    vfx-system.md
```

## Current Folders

- `Assets/`: Unity assets, scenes, prefabs, scripts, art, audio, settings
- `Assets/Scenes/`: Unity scene files
- `Assets/Scripts/`: runtime C# code
- `Assets/Scripts/Common/`: shared non-ECS gameplay helpers
- `Assets/Scripts/Player/`: player root, movement, facing, animation, health
- `Assets/Scripts/Mob/`: mob root, behavior, triggers, attacks, status handling
- `Assets/Scripts/Spawn/`: spawn root, spawn points, spawn config
- `Assets/Scripts/Skills/`: skill definitions, runtime skill compilation,
  supports, triggers, validators
- `Assets/Scripts/System/Common/`: shared combat ECS bridge, target proxies,
  damage dispatch, lifetime, shape/collision math, render data
- `Assets/Scripts/System/Projectile/`: projectile ECS runtime
- `Assets/Scripts/System/Aoe/`: AOE ECS runtime
- `Assets/Scripts/System/Vfx/`: VFX dispatch runtime
- `Assets/Prefabs/`: authored runtime prefabs
- `Assets/ScriptableObjects/`: authored reusable data
- `Assets/Tests/EditMode/`: editor and pure tests
- `Assets/Tests/PlayMode/`: scene/runtime tests
- `Docs/`: design and implementation docs
- `Packages/`: Unity package manifest and lock files
- `ProjectSettings/`: Unity project settings

## Good First Places To Look

- Project summary: [project-overview.md](./project-overview.md)
- Architecture: [architecture.md](./architecture.md)
- Coding rules: [coding-standards.md](./coding-standards.md)
- Player controls and game loop: [design/gameplay.md](./design/gameplay.md)
- Skill/support system: [game-logic/skill-system.md](./game-logic/skill-system.md)
- Projectiles: [simulation/projectile-system.md](./simulation/projectile-system.md)
- AOEs: [simulation/aoe-system.md](./simulation/aoe-system.md)
- VFX: [simulation/vfx-system.md](./simulation/vfx-system.md)
- Mobs: [game-logic/mobs.md](./game-logic/mobs.md) and
  [game-logic/mob-behaviour.md](./game-logic/mob-behaviour.md)
- Spawning: [game-logic/spawn-system.md](./game-logic/spawn-system.md)
- Tests: [testing.md](./testing.md)
- ECS patterns: [simulation/ecs-notes.md](./simulation/ecs-notes.md)

## Current Common Combat Map

- `Assets/Scripts/System/Common/CombatRoot.cs`: per-faction scene bridge for
  projectile and AOE registration, managed spawn submission, render resources,
  target registry, and ECS world/scope lifetime.
- `Assets/Scripts/System/Common/CombatScope.cs`: shared `CombatScope` tag and
  `CombatFaction`.
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`: common kinematics,
  collision, hit payload, generic `Active`, common lifetime, and compatibility
  target element.
- `Assets/Scripts/System/Common/CombatTargetProxy.cs`: ECS target proxy entity
  creation, shape push, and deletion.
- `Assets/Scripts/System/Common/CombatTargetRegistry.cs`: managed target
  registry that creates proxy entities for registered `ICombatTarget` objects.
- `Assets/Scripts/System/Common/DamageDispatchBridge.cs`:
  `DamageFinalizeSystem` and presentation damage replay into managed targets.
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry.
- `Assets/Scripts/System/Common/CombatCollisionMath.cs`: shared bounds and
  narrow-phase collision math.
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`: shared batched
  sprite render components and matrix preparation.
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`: instanced render
  submission in `PresentationSystemGroup`.

## Current Projectile Runtime Map

- `Assets/Scripts/System/Projectile/ProjectileRuntimeEvents.cs`: managed
  projectile payload/counter helpers.
- `Assets/Scripts/System/Projectile/ProjectileSpawnRequest.cs`: managed
  projectile spawn request DTO passed to `CombatRoot`.
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`:
  `ProjectileSpawnEvent`, `ProjectileSpawnCommand`, and impact/burst event
  helpers.
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`: drains
  projectile events and expands volley data into per-shape command queues.
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`: basic and
  child-spawner apply systems; reuse disabled `Active` slots before cold
  creation.
- `Assets/Scripts/System/Projectile/TimedProjectileSpawnSystem.cs`: timed child
  projectile event production.
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`: target proxy
  acquisition and homing steering.
- `Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs`: movement and
  projectile bounds refresh.
- `Assets/Scripts/System/Projectile/ProjectileContactGateSystem.cs`: repeat-hit
  contact gate expiry.
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`: spatial hash
  collision, damage/spawn/VFX event emission, pierce, gates, and deactivation.
- `Assets/Scripts/System/Projectile/ProjectileCollisionMath.cs`: projectile
  compatibility wrapper over common collision math.

## Current AOE Runtime Map

- `Assets/Scripts/System/Aoe/AoeRuntimeEvents.cs`: managed AOE spawn request and
  counters.
- `Assets/Scripts/System/Aoe/AoeConfig.cs`: ScriptableObject authoring for AOE
  type definitions.
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`: `AoeSpawnEvent`,
  `AoeSpawnCommand`, and impact AOE helper.
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`: drains AOE events and
  writes command stream.
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`: applies AOE commands by
  reusing disabled `Active` slots before cold creation.
- `Assets/Scripts/System/Aoe/AoeContactGateSystem.cs`: lingering AOE repeat-hit
  gate expiry.
- `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs`: target proxy collision,
  damage/projectile-burst/VFX event emission, contact gates, and pulse
  deactivation.
- `Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`: interval pulse VFX for
  lingering AOEs.
- `Assets/Scripts/System/Aoe/AoeTypeRegistry.cs`: runtime AOE type lookup.

## Current VFX Runtime Map

- `Assets/Scripts/System/Vfx/VfxEcsComponents.cs`: VFX request data and ECS
  catalog key.
- `Assets/Scripts/System/Vfx/VfxFlushJob.cs`: Burst job flushing native VFX
  events into scope buffers.
- `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`: GPU resource and dispatch
  owner.
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`: scene-object VFX root and static
  registry.
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`: presentation system
  that drains `VfxSpawnRequestElement` buffers and dispatches through the
  matching `CombatVfxRoot`.
