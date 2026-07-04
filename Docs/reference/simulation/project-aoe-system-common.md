# Projectile/AOE System Common

All docs in `Docs/` are design references. They describe current
implementation intent and should be checked against code before large changes.

This is the detailed shared projectile/AOE ECS doc. Use [index.md](./index.md)
for the simulation overview and aspect map.

This page covers shared runtime rules for the projectile and AOE ECS systems.
It is about common ownership, spawn flow, target proxies, reuse/despawn,
consequence events, and frame timing. Domain-specific details still live in
`projectile-system.md` and `aoe-system.md`.

## Core Rules

- Projectiles and AOEs are high-count ECS entities, not one GameObject per
  gameplay entity.
- Scene objects submit spawn intent; ECS systems expand, allocate, simulate,
  collide, and prepare render/VFX data.
- Runtime entities must carry a domain tag: `ProjectileTag` or `AoeTag`.
- `Active` is only a generic occupancy flag. It does not identify domain or
  faction.
- Faction is explicit through `CombatFaction`.
- Hot despawn disables enableable components. It does not destroy projectile or
  AOE entities.
- Spawn must flow through event -> expansion -> command -> apply.

## Verified Code Map

- `Assets/Scripts/System/Common/CombatRoot.cs`: managed spawn submission,
  target registry ownership, render resource ownership, faction registration,
  ECS world/scope acquire and release, and per-faction teardown.
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`: shared combat world
  owner, shared scope owner, projectile/AOE spawn buffers, template registries,
  `Active`, common lifetime, common kinematics, and common collision data.
- `Assets/Scripts/System/Common/CombatScope.cs`: shared `CombatScope` tag and
  `CombatFaction`.
- `Assets/Scripts/System/Common/CombatTargetProxy.cs`: ECS target proxy create,
  push, and delete.
- `Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs`: target-bucketed
  hit finalize, ECS-owned target health/status application, and presentation
  bridge source data.
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`: projectile and
  lingering-AOE lifetime expiry.
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`: shared timed-spawn
  producer for projectile and AOE child events.
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`: common render data,
  render active tag, faction/type shared components, and render matrix prep.
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`: shared batched
  render submission.
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`: shared VFX request
  dispatch from scope buffers.
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`: projectile
  event/command data and impact/burst event builders.
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`:
  projectile event drain and command expansion.
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`: projectile
  slot reuse and cold creation.
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`: projectile
  collision consequences and projectile deactivation.
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`: AOE event/command data and
  impact/on-hit event builders.
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`: AOE event drain,
  command expansion, and spawn VFX request emission.
- AOE spawn apply file: impact and lingering AOE
  slot reuse and cold creation.
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`: AOE collision consequences
  and pulse deactivation.
- `Assets/Scripts/System/Status/StatusProcessSystem.cs`: status-stack
  detonation producer that emits projectile or AOE spawn events.

## Shared Combat Scope

`CombatRoot` instances share one ECS world and one ref-counted `CombatScope`
entity. The first scope acquire creates:

- `CombatScope`
- `DynamicBuffer<ProjectileSpawnEvent>`
- `DynamicBuffer<AoeSpawnEvent>`
- `DynamicBuffer<VfxSpawnRequestElement>`
- `ProjectileSpawnTemplate`
- `AoeSpawnTemplate`

The scope is shared across domains and factions. Scope membership alone does
not mean projectile, AOE, player faction, or mob faction. Systems must use
domain tags and faction data.

## Managed Boundary

`CombatRoot` is the managed bridge. It may touch Unity objects, serialized
authoring data, render resources, target registries, and scope buffers.

Managed projectile spawn uses `CombatRoot.Spawn(ProjectileSpawnRequest)`. The
root validates render resources, reserves projectile ids, builds one
`ProjectileSpawnEvent`, and appends it to the shared scope
`DynamicBuffer<ProjectileSpawnEvent>`.

Managed AOE spawn uses `CombatRoot.Spawn(AoeSpawnRequest)` or
`CombatRoot.Spawn(ProjectileAoeSpawnRequest)`. The root validates type id and
geometry, reserves an AOE id, builds one `AoeSpawnEvent`, and appends it to the
shared scope `DynamicBuffer<AoeSpawnEvent>`.

Managed gameplay code should not create projectile or AOE entities directly.
It should submit spawn intent and let ECS expansion/apply systems handle the
rest.

## Target Proxy Model

Projectile and AOE collision use ECS target proxies instead of live collider
queries in hot simulation.

Target proxies carry:

- `TargetProxyTag`
- `TargetPosition`
- `TargetCollisionShape`
- `TargetFaction`
- `TargetHealth`
- `TargetStackEntry`
- managed `TargetCompanion`

Simulation jobs read unmanaged target proxy data. Managed companion access is
restricted to the presentation bridge path that calls target callbacks after
simulation data has been finalized.

Actor roots push target proxy position and shape before simulation. Dead or
disabled targets delete proxies after the frame boundary chosen by the actor
root.

## Spawn Event To Command Rule

Spawn intent and allocation intent are separate.

Events are gameplay intent:

- `ProjectileSpawnEvent`
- `AoeSpawnEvent`

Commands are one-entity allocation intent:

- `ProjectileSpawnCommand`
- `AoeSpawnCommand`

Projectile flow:

`ProjectileSpawnEvent` -> `ProjectileSpawnExpansionSystem` ->
`ProjectileSpawnCommand` -> `ProjectileSpawnApplySystem`.

AOE flow:

`AoeSpawnEvent` -> `AoeSpawnExpansionSystem` -> `AoeSpawnCommand` ->
`ImpactAoeSpawnApplySystem` or `LingeringAoeSpawnApplySystem`.

Do not add a direct path that creates projectile or AOE entities from managed
gameplay code, collision systems, status systems, or timed-spawn systems.

## Internal Event Producers

ECS systems that create follow-up combat effects produce spawn events, not
entities.

Current producers:

- `TimedSpawnSystem` reads stored template events, stamps source position,
  faction, source id, and deterministic tick data, then enqueues projectile or
  AOE events.
- `ProjectileCollisionSystem` enqueues impact projectile events and impact AOE
  events from accepted projectile hits.
- `AoeCollisionCore` enqueues projectile burst events and on-hit AOE events
  from accepted AOE hits.
- `StatusProcessSystem` enqueues stack detonation projectile or AOE events.

These producers write to `ProjectileSpawnExpansionSystem.EventQueue` or
`AoeSpawnExpansionSystem.EventQueue`. Producer job handles are combined into
the expansion system's `ProducerHandle`, because the native queues are not
tracked by ordinary component dependencies.

## Expansion Ownership

Expansion systems own spawn math and command production.

`ProjectileSpawnExpansionSystem` drains:

- `ProjectileSpawnExpansionSystem.EventQueue`
- shared scope `DynamicBuffer<ProjectileSpawnEvent>`

It expands count/spread/jitter/pattern data, resolves per-shot ids and
velocities, computes bounds, updates render Z, and writes commands into one
projectile command container.

`AoeSpawnExpansionSystem` drains:

- `AoeSpawnExpansionSystem.EventQueue`
- shared scope `DynamicBuffer<AoeSpawnEvent>`

It computes bounds, resolves deterministic ids, emits spawn VFX requests with
trigger `0`, and writes `AoeSpawnCommand` values into impact or lingering
command containers.

Apply systems should not interpret volley, scatter, jitter, or pattern math.

## Apply Ownership

Apply systems own reuse and cold creation.

Projectile apply uses one disabled-slot query:
`WithAll<ProjectileTag>()` plus `WithDisabled<Active>()`. Timed vs non-timed
projectiles reuse across the same pool; `TimedSpawnComponent` is enabled or
disabled during reset.

AOE apply is split by pool. Impact AOEs reuse slots without
`CombatLifetimeComponent`. Lingering AOEs reuse slots with
`CombatLifetimeComponent`. Timed vs non-timed lingering AOEs reuse across the
same lingering pool; `TimedSpawnComponent` is enabled or disabled during reset.

Reusable slots are found with `WithDisabled<Active>()`. Each apply system runs
one single-threaded Burst reuse job that resets all per-instance data and
enables the needed enableable components. Commands after the reused prefix are
cold-created through an `EntityCommandBuffer`.

Cold-created entities become part of the reusable pool after their first
despawn.

## Despawn And Reuse

Normal projectile and AOE despawn disables enableable components. It does not
destroy entities.

Shared occupancy:

- `Active`

Domain/render enableable state:

- `ProjectileCollisionActiveTag`
- `AoeCollisionActiveTag`
- `CombatRenderActiveTag`
- `CombatLifetimeComponent`
- `ProjectileTrackingComponent`

Projectile despawn paths:

- `CombatLifetimeSystem` disables `Active` and `CombatRenderActiveTag` on
  lifetime expiry.
- `ProjectileCollisionSystem` disables `Active` and `CombatRenderActiveTag`
  when the source projectile is invalid, expired, or consumed by pierce.

AOE despawn paths:

- `CombatLifetimeSystem` disables `Active`, `AoeCollisionActiveTag`, and
  `CombatRenderActiveTag` on lingering AOE lifetime expiry.
- `AoeCollisionCore` disables `Active`, `AoeCollisionActiveTag`, and
  `CombatRenderActiveTag` for invalid AOEs and pulse AOEs after their collision
  pass.

This pattern keeps entities in stable archetypes and avoids structural churn on
hot combat paths.

## Consequence Events

Projectile and AOE collision systems emit plain data consequences. They do not
call managed target callbacks and do not allocate follow-up entities directly.

Current consequence paths include:

- hit events into `CombatApplyFinalizeSystem`
- `ProjectileSpawnEvent` values for impact projectiles, AOE projectile bursts,
  and stack detonation projectiles
- `AoeSpawnEvent` values for impact AOEs, on-hit AOEs, timed AOEs, and stack
  detonation AOEs
- `VfxPendingSpawn` values that flush into `VfxSpawnRequestElement` buffers

Follow-up spawns stay in ECS event flow and return to expansion/apply.

## Rendering And VFX

Projectile and AOE visuals share the sprite-atlas render path.

Runtime entities carry common render data:

- `CombatRenderComponent`
- `CombatRenderElement`
- `CombatRenderActiveTag`
- `CombatRenderKindId`

`CombatRenderKindId` is a plain kind identifier; the atlas UV rect itself
lives on `CombatRenderComponent.UvRect`, computed once by the shared
`CombatRenderResourceRegistry` when the spawn command is built (one shared,
manually-assembled `SpriteAtlas`-backed texture, one mesh, one material for
every registered kind) rather than selecting a per-kind resource bundle.
`CombatRenderPrepareSystem` prepares matrices for active renderable entities.
`CombatBatchedRenderSystem` runs in presentation, scatters matrices and UV
rects together in one active-only pass (reading each entity's own
components directly, no per-frame registry lookup), and submits the shared
mesh/material in as few `Graphics.RenderMeshInstanced` calls as the
1023-instance cap requires.

VFX requests are data until presentation. Collision, lifetime, pulse, and spawn
systems write VFX request data into native queues/streams or scope buffers.
`CombatVfxDispatchSystem` drains `VfxSpawnRequestElement` buffers and dispatches
through `CombatVfxRoot`.

## Teardown Exceptions

Normal projectile and AOE despawn should not delete entities.

Deliberate deletion still exists for lifecycle teardown:

- `CombatRoot.OnDestroy` destroys projectile and AOE entities for that root's
  faction.
- `CombatScopeOwner.Release` destroys the shared `CombatScope` after the last
  combat root releases it.
- `CombatTargetProxy.Delete` destroys target proxy entities when actor target
  proxies are no longer valid.

These are not normal combat despawn paths.

## Frame Timing

Important ordering:

1. `CombatLifetimeSystem` expires existing projectiles and AOEs.
2. `TimedSpawnSystem` emits interval spawn events.
3. Projectile tracking, movement, contact gates, and collision run.
4. AOE contact gates and AOE collision run.
5. `CombatApplyFinalizeSystem` applies hit events to ECS target health and
   status data.
6. `StatusProcessSystem` emits stack detonation spawn events.
7. `ProjectileSpawnExpansionSystem` and `AoeSpawnExpansionSystem` drain
   completed event producers plus managed scope buffers.
8. Projectile and AOE apply systems reuse disabled slots or cold-create
   overflow.
9. Render preparation and presentation systems run.

Because apply runs after movement and collision, newly spawned/reused
projectiles and AOEs do not move, collide, or fire timed spawns until the next
simulation update.

## Change Rules

- Keep event data as gameplay intent.
- Keep command data as one-entity allocation intent.
- Keep spawn math in expansion systems.
- Keep reuse and cold creation in apply systems.
- Keep hot despawn as enable/disable, not destroy/create.
- Require domain tags on projectile and AOE systems.
- Do not treat `Active`, common combat components, or scope membership as a
  domain marker.
- Do not route internal follow-up spawns through managed target callbacks.
- Do not read `TargetCompanion` from simulation jobs.
- Delete projectile and AOE entities only for root/scope teardown.

## Supporting Tests

Current tests that support this map include:

- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`: projectile event to
  command to apply flow, timed-spawn enable state, child spawn
  behavior, and command shape checks.
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`: AOE expansion/apply, impact
  versus lingering lifetime shape, collision consequences, and event queue
  inspection.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`: projectile
  collision producing follow-up spawn events.
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`: managed AOE spawn requests,
  timed template registration, and AOE runtime behavior.
- `Assets/Tests/EditMode/ProjectileAuthoringEditModeTests.cs`: spawn event and
  command data constraints, including AOE command size checks.
