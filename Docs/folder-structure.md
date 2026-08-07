# Folder Structure

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

Quick map.

## Docs Layout

```text
Docs/
  project-overview.md
  ui.md
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
- `Assets/Scripts/PlayGround.GameLogic.asmdef`: `PlayGround.GameLogic`; the root
  assembly for skills, actors, spawning, persistence, authoring, stats, and status.
- `Assets/Scripts/System/PlayGround.Sim.asmdef`: `PlayGround.Sim`; ECS runtime plus
  combat bridge, ECS-serving presentation roots, `System/Authoring/` prefab
  components, and `System/Shared/` primitives.
- `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef`: `PlayGround.SkillUi`;
  skill-loadout UI.
- `Assets/Scripts/Debugging/PlayGround.Debugging.asmdef`: exempt Debugging leaf.
- `Assets/Prefabs/`: authored runtime prefabs
- `Assets/ScriptableObjects/`: authored reusable data
- `Assets/Tests/EditMode/`: editor and pure tests
- `Assets/Tests/PlayMode/`: scene/runtime tests
- `Docs/`: design and implementation docs
- `Packages/`: Unity package manifest and lock files; no PlayGround embedded package.
- `ProjectSettings/`: Unity project settings

## Good First Places To Look

- Project summary: [project-overview.md](./project-overview.md)
- UI architecture: [ui.md](./ui.md)
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
- Skill stat modifiers:
  [reference/game-logic/skill-modifiers.md](./reference/game-logic/skill-modifiers.md)
- General numeric modifier pattern (not skill-specific):
  [reference/architecture/numeric-modifiers.md](./reference/architecture/numeric-modifiers.md)
- Simulation details: [reference/simulation/index.md](./reference/simulation/index.md)
- ECS patterns: [reference/simulation/ecs-notes.md](./reference/simulation/ecs-notes.md)
- Coding rules: [coding-standards.md](./coding-standards.md)
- Tests: [testing.md](./testing.md)

## Current Combat Runtime Map

- `Assets/Scripts/System/Core/CombatRoot.cs`: per-faction scene bridge for
  projectile, AOE, and targeted registration, managed spawn submission, render resources,
  target registry, and ECS world/scope lifetime.
- `Assets/Scripts/System/Core/CombatScope.cs`: shared `CombatScope` tag and
  `CombatFaction`.
- `Assets/Scripts/System/Core/CombatScopeOwner.cs`: shared scope entity
  owner and cross-feature scope buffer/template registry bootstrap.
- `Assets/Scripts/System/Platform/CombatEcsWorld.cs`: Unity ECS world
  acquire/release integration for combat runtime roots.
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`: ECS target proxy entity
  creation, shape push, health/status seed data, and deletion.
- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`: managed target
  registry that creates proxy entities for registered `ICombatTarget` objects.
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`: single-pass
  hit finalize, ECS health/status application, and compact result lane writes.
- `Assets/Scripts/System/Application/CombatApplyResults.cs`: compact
  combat result lane and `CombatTickResult` contract data.
- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`: presentation
  bridge that resolves `TargetCompanion` and replays finalized combat feedback.
- `Assets/Scripts/System/Lifetime/CombatLifecycleComponents.cs`: shared
  transient entity active, kinematics, arming, and lifetime data.
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry.
- `Assets/Scripts/System/Api/Collision/CombatCollisionComponents.cs`: shared
  collision components, enableable collision gate, and collision target buffer.
- `Assets/Scripts/System/Api/Collision/Narrowphase/CombatCollisionMath.cs`: shared bounds and
  narrow-phase collision math.
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`: shared target
  proxy spatial hashes for projectile, tracking, and AOE collision.
- `Assets/Scripts/System/Rendering/CombatRenderComponents.cs`: shared batched
  sprite render components and matrix preparation.
- `Assets/Scripts/System/Rendering/CombatBatchedRenderSystem.cs`: instanced render
  submission in `PresentationSystemGroup`.

## Current Projectile Runtime Map

- `Assets/Scripts/System/Projectiles/ProjectileRuntimeEvents.cs`: managed
  projectile payload/counter helpers.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs`: managed
  projectile spawn request DTO passed to `CombatRoot`.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`:
  `ProjectileSpawnEvent`, `ProjectileSpawnCommand`, and impact/burst event
  helpers.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`: drains
  projectile events and expands volley data into per-shape command queues.
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`: discrete-lane
  apply; reuses disabled `Active` slots before cold creation.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`: continuous-lane
  apply; reuses disabled slots from the continuous archetype before cold creation.
- `Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`: shared timed child spawn
  event production.
- `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs`: target proxy
  acquisition and homing steering.
- `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`: movement and
  projectile bounds refresh.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousOriginSystem.cs`: captures continuous
  step origin before shared projectile movement.
- `Assets/Scripts/System/Projectiles/ProjectileContactGateSystem.cs`: repeat-hit
  contact gate expiry.
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`: spatial hash
  collision, hit/spawn event emission, pierce, gates, and deactivation.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`: continuous-lane
  collision against current footprint plus travel corridor, nearest-first.
- `Assets/Scripts/System/Api/Collision/Narrowphase/CombatSweepMath.cs`: swept
  corridor geometry and closest-approach math; overlap remains in `CombatCollisionMath`.

## Current AOE Runtime Map

- `Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs`: managed AOE spawn request and
  counters.
- `Assets/Scripts/Skills/Authoring/AoeConfig.cs`: ScriptableObject authoring for AOE
  type definitions.
- `Assets/Scripts/System/Aoes/AoeSpawnPipeline.cs`: `AOE variant spawn event`,
  `AoeSpawnCommand`, and impact/on-hit AOE helpers.
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs`: contains impact and
  lingering AOE expansion systems that drain AOE events and
  writes command stream.
- AOE spawn apply file: impact and lingering AOE
  apply systems; reuse disabled `Active` slots before cold creation.
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`: impact AOE target
  proxy collision, hit/spawn/VFX event emission, and deactivation.
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`: lingering AOE
  interval collision and hit/spawn/VFX event emission.
- `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs`: interval pulse VFX for
  lingering AOEs.
- `Assets/Scripts/System/Aoes/AoeTypeRegistry.cs`: runtime AOE type lookup.

## Current Targeted Runtime Map

- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`: the spawn event and
  the command shape.
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`: the event
  lane, echo fan-out, and resolved command production.
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`: the single
  targeted pool; disabled-slot reuse and cold creation.
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`: the target-proxy
  spatial-hash walk, falloff, link VFX emission, and walk-end expiry.
- `Assets/Scripts/System/Targeted/TargetedTypeRegistry.cs`: runtime visual type lookup.

## Current Skill Targeted Files

- `Assets/Scripts/Skills/Skill/TargetedSkill.cs`: the targeted skill asset and
  its base type.
- `Assets/Scripts/Skills/Trigger/OnImpactTargetedTrigger.cs` and
  `TargetedIntervalSpawnTrigger.cs`: targeted trigger links.
- `Assets/Scripts/Skills/Support/MultipleChainsSupport.cs`: targeted echo count
  and mana support.
- `Assets/Scripts/Skills/Validator/TargetedPrefab.cs`: targeted prefab and VFX validation.

## Current VFX Runtime Map

- `Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`: shared VFX timing slots and
  `VfxTimingData` ECS components.
- `Assets/Scripts/System/Vfx/VfxDataShapes.cs`: `VfxDataShape` enum, per-shape
  request structs (`CircularVfxSpawnRequest`, `TimedCircularVfxSpawnRequest`, `LineSegmentVfxSpawn`), and the
  `VfxDataShapeTable` (buffer contracts + id encode/decode).
- `Assets/Scripts/System/Vfx/VfxEmit.cs`: Burst helper that decodes shape from
  the id and enqueues the concrete request to the matching per-shape queue.
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`: GPU resource and dispatch
  owner.
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`: scene-object VFX root and static
  registry.
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`: presentation system
  that completes producers, buckets each shape's queue by graph id, and dispatches
  through the matching `CombatVfxRoot`.
