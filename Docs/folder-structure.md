# Folder Structure

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

Quick map.

## Docs Layout

```text
Docs/
  project-overview.md
  folder-structure.md
  coding-standards.md
  performance.md
  profiling.md
  testing.md
  release.md
  todo.md
  architecture/
    index.md
    layer-rules.md
    data-flow-overview.md
    phase-order.md
  layers/
    scene-and-authoring.md
    game-logic.md
    combat-bridge.md
    ecs-simulation.md
    presentation-and-feedback.md
  flows/
    runtime-frame.md
    skill-to-combat-spawn.md
    spawn-event-to-entity.md
    collision-to-combat-result.md
    target-proxy-lifecycle.md
    vfx-dispatch.md
    mob-spawn-and-behaviour.md
  contracts/
    combat-root-api.md
    spawn-requests.md
    spawn-events-and-commands.md
    target-proxy.md
    combat-hit-and-tick-results.md
    skill-runtime-snapshots.md
    vfx-requests.md
    render-batch-data.md
  decisions/
    adr-001-hybrid-gameobject-ecs-runtime.md
    adr-002-plain-data-snapshot-boundary.md
    adr-003-event-command-spawn-pipeline.md
    adr-004-target-proxy-collision.md
    adr-005-enableable-pooling-for-combat-entities.md
    adr-006-ecs-aggregated-combat-results.md
  reference/
    architecture/
    design/
    game-logic/
    simulation/
```

## Documentation Authority

- `Docs/architecture/`: entry points, global layer rules, phase order, and flow
  maps.
- `Docs/layers/`: authoritative ownership and dependency boundaries.
- `Docs/flows/`: cross-layer runtime sequences.
- `Docs/contracts/`: boundary-crossing data shapes and APIs.
- `Docs/decisions/`: ADRs explaining major architecture choices.
- `Docs/reference/`: preserved detailed notes from before the layered
  reorganization. Use as implementation detail, not as the primary authority
  when it repeats a layer/contract rule.

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
- `Assets/Scripts/System/Combat/`: combat ECS runtime, organized by ownership
- `Assets/Scripts/System/Application/`: hit application, finalization,
  and compact native combat result data
- `Assets/Scripts/System/Presentation/`: presentation bridge systems that
  resolve managed target feedback from finalized combat result data
- `Assets/Scripts/System/Combat/Targets/`: target proxy, target registry,
  target interfaces, and target-owned shape state
- `Assets/Scripts/System/Combat/Projectiles/`: projectile ECS runtime
- `Assets/Scripts/System/Combat/Aoes/`: AOE ECS runtime
- `Assets/Scripts/System/Combat/Collision/`: shared collision math,
  shape primitives, and target broadphase infrastructure
- `Assets/Scripts/System/Combat/Spawning/`: cross-feature spawn templates,
  timed child spawning, and interval child contracts
- `Assets/Scripts/System/Combat/Lifetime/`: transient combat entity lifetime,
  arming, active state, death utility, and pool cleanup
- `Assets/Scripts/System/Combat/Rendering/`: combat sprite render data,
  matrix preparation, and batched submission
- `Assets/Scripts/System/Combat/Vfx/`: combat VFX dispatch runtime
- `Assets/Scripts/System/Combat/Status/`: ECS status processing
- `Assets/Scripts/System/Combat/Stats/`: combat stats and diagnostics
- `Assets/Scripts/System/Combat/Core/`: combat root, scope, faction, and
  bootstrap-level scope ownership
- `Assets/Scripts/System/Combat/Platform/`: Unity ECS world integration
- `Assets/Prefabs/`: authored runtime prefabs
- `Assets/ScriptableObjects/`: authored reusable data
- `Assets/Tests/EditMode/`: editor and pure tests
- `Assets/Tests/PlayMode/`: scene/runtime tests
- `Docs/`: design and implementation docs
- `Packages/`: Unity package manifest and lock files
- `ProjectSettings/`: Unity project settings

## Good First Places To Look

- Project summary: [project-overview.md](./project-overview.md)
- Architecture entry point: [architecture/index.md](./architecture/index.md)
- Global layer rules: [architecture/layer-rules.md](./architecture/layer-rules.md)
- Phase order: [architecture/phase-order.md](./architecture/phase-order.md)
- Runtime flow map:
  [architecture/data-flow-overview.md](./architecture/data-flow-overview.md)
- Scene/authoring ownership:
  [layers/scene-and-authoring.md](./layers/scene-and-authoring.md)
- Game logic ownership: [layers/game-logic.md](./layers/game-logic.md)
- Combat bridge ownership: [layers/combat-bridge.md](./layers/combat-bridge.md)
- ECS simulation ownership: [layers/ecs-simulation.md](./layers/ecs-simulation.md)
- Presentation ownership:
  [layers/presentation-and-feedback.md](./layers/presentation-and-feedback.md)
- Spawn event/command contract:
  [contracts/spawn-events-and-commands.md](./contracts/spawn-events-and-commands.md)
- Target proxy contract: [contracts/target-proxy.md](./contracts/target-proxy.md)
- Combat result contract:
  [contracts/combat-hit-and-tick-results.md](./contracts/combat-hit-and-tick-results.md)
- Skill details:
  [reference/game-logic/skill-system.md](./reference/game-logic/skill-system.md)
- Simulation details: [reference/simulation/index.md](./reference/simulation/index.md)
- ECS patterns: [reference/simulation/ecs-notes.md](./reference/simulation/ecs-notes.md)
- Coding rules: [coding-standards.md](./coding-standards.md)
- Tests: [testing.md](./testing.md)

## Current Combat Runtime Map

- `Assets/Scripts/System/Combat/Core/CombatRoot.cs`: per-faction scene bridge for
  projectile and AOE registration, managed spawn submission, render resources,
  target registry, and ECS world/scope lifetime.
- `Assets/Scripts/System/Combat/Core/CombatScope.cs`: shared `CombatScope` tag and
  `CombatFaction`.
- `Assets/Scripts/System/Combat/Core/CombatScopeOwner.cs`: shared scope entity
  owner and cross-feature scope buffer/template registry bootstrap.
- `Assets/Scripts/System/Combat/Platform/CombatEcsWorld.cs`: Unity ECS world
  acquire/release integration for combat runtime roots.
- `Assets/Scripts/System/Combat/Targets/CombatTargetProxy.cs`: ECS target proxy entity
  creation, shape push, health/status seed data, and deletion.
- `Assets/Scripts/System/Combat/Targets/CombatTargetRegistry.cs`: managed target
  registry that creates proxy entities for registered `ICombatTarget` objects.
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`: single-pass
  hit finalize, ECS health/status application, and compact result lane writes.
- `Assets/Scripts/System/Application/CombatApplyResults.cs`: compact
  combat result lane and `CombatTickResult` contract data.
- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`: presentation
  bridge that resolves `TargetCompanion` and replays finalized combat feedback.
- `Assets/Scripts/System/Combat/Lifetime/CombatLifecycleComponents.cs`: shared
  transient entity active, kinematics, arming, and lifetime data.
- `Assets/Scripts/System/Combat/Lifetime/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry.
- `Assets/Scripts/System/Combat/Collision/CombatCollisionComponents.cs`: shared
  collision components, enableable collision gate, and collision target buffer.
- `Assets/Scripts/System/Combat/Collision/CombatCollisionMath.cs`: shared bounds and
  narrow-phase collision math.
- `Assets/Scripts/System/Combat/Collision/TargetSpatialHashSystem.cs`: shared target
  proxy spatial hashes for projectile, tracking, and AOE collision.
- `Assets/Scripts/System/Combat/Rendering/CombatRenderComponents.cs`: shared batched
  sprite render components and matrix preparation.
- `Assets/Scripts/System/Combat/Rendering/CombatBatchedRenderSystem.cs`: instanced render
  submission in `PresentationSystemGroup`.

## Current Projectile Runtime Map

- `Assets/Scripts/System/Combat/Projectiles/ProjectileRuntimeEvents.cs`: managed
  projectile payload/counter helpers.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileSpawnRequest.cs`: managed
  projectile spawn request DTO passed to `CombatRoot`.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileSpawnPipeline.cs`:
  `ProjectileSpawnEvent`, `ProjectileSpawnCommand`, and impact/burst event
  helpers.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileSpawnExpansionSystem.cs`: drains
  projectile events and expands volley data into per-shape command queues.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileSpawnApplySystem.cs`: unified
  projectile apply system; reuse disabled `Active` slots before cold creation.
- `Assets/Scripts/System/Combat/Spawning/TimedSpawnSystem.cs`: shared timed child spawn
  event production.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileTrackingSystem.cs`: target proxy
  acquisition and homing steering.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileMovementSystem.cs`: movement and
  projectile bounds refresh.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileContactGateSystem.cs`: repeat-hit
  contact gate expiry.
- `Assets/Scripts/System/Combat/Projectiles/ProjectileCollisionSystem.cs`: spatial hash
  collision, hit/spawn/VFX event emission, pierce, gates, and deactivation.

## Current AOE Runtime Map

- `Assets/Scripts/System/Combat/Aoes/AoeRuntimeEvents.cs`: managed AOE spawn request and
  counters.
- `Assets/Scripts/System/Combat/Aoes/AoeConfig.cs`: ScriptableObject authoring for AOE
  type definitions.
- `Assets/Scripts/System/Combat/Aoes/AoeSpawnPipeline.cs`: `AOE variant spawn event`,
  `AoeSpawnCommand`, and impact/on-hit AOE helpers.
- `Assets/Scripts/System/Combat/Aoes/AoeSpawnExpansionSystem.cs`: contains impact and
  lingering AOE expansion systems that drain AOE events and
  writes command stream.
- AOE spawn apply file: impact and lingering AOE
  apply systems; reuse disabled `Active` slots before cold creation.
- `Assets/Scripts/System/Combat/Aoes/ImpactAoeCollisionSystem.cs`: impact AOE target
  proxy collision, hit/spawn/VFX event emission, and deactivation.
- `Assets/Scripts/System/Combat/Aoes/LingeringAoeCollisionSystem.cs`: lingering AOE
  interval collision and hit/spawn/VFX event emission.
- `Assets/Scripts/System/Combat/Aoes/AoePulseVfxSystem.cs`: interval pulse VFX for
  lingering AOEs.
- `Assets/Scripts/System/Combat/Aoes/AoeTypeRegistry.cs`: runtime AOE type lookup.

## Current VFX Runtime Map

- `Assets/Scripts/System/Combat/Vfx/VfxEcsComponents.cs`: VFX request data and ECS
  catalog key.
- `Assets/Scripts/System/Combat/Vfx/VfxFlushJob.cs`: Burst job flushing native VFX
  events into scope buffers.
- `Assets/Scripts/System/Combat/Vfx/CombatVfxDispatcher.cs`: GPU resource and dispatch
  owner.
- `Assets/Scripts/System/Combat/Vfx/CombatVfxRoot.cs`: scene-object VFX root and static
  registry.
- `Assets/Scripts/System/Combat/Vfx/CombatVfxDispatchSystem.cs`: presentation system
  that drains `VfxSpawnRequestElement` buffers and dispatches through the
  matching `CombatVfxRoot`.
