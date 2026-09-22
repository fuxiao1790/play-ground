# Projectile/AOE System Common

All docs in `Docs/` are design references. They describe current
implementation intent and should be checked against code before large changes.

This is the detailed shared combat-domain ECS doc. Use [index.md](./index.md)
for the simulation overview and aspect map. The filename predates the Targeted
domain; the rules here cover all three.

This page covers shared runtime rules for the projectile, AOE, and targeted ECS
systems. It is about common ownership, spawn flow, target proxies, reuse/despawn,
consequence events, and frame timing. Domain-specific details still live in
`projectile-system.md`, `aoe-system.md`, and `targeted-system.md`.

## Core Rules

- Projectiles, AOEs, and targeted chains are high-count ECS entities, not one
  GameObject per gameplay entity.
- Scene objects submit spawn intent; ECS systems expand, allocate, simulate,
  collide, and prepare render/VFX data.
- Runtime entities must carry a domain tag: `ProjectileTag`, `AoeTag`, or
  `TargetedTag`.
- `Active` is only a generic occupancy flag. It does not identify domain or
  faction.
- Faction is explicit through `CombatFaction`.
- Hot despawn disables enableable components. It does not destroy projectile,
  AOE, or targeted entities.
- Spawn must flow through event -> expansion -> command -> apply.

## Verified Code Map

- `Assets/Scripts/System/Core/CombatRoot.cs`: managed spawn submission,
  target registry ownership, render resource ownership, faction registration,
  ECS world/scope acquire and release, and per-faction teardown.
- `Assets/Scripts/System/Core/CombatScopeOwner.cs`: shared combat world
  owner, shared scope owner, projectile/AOE spawn buffers, template registries,
  `Active`, common lifetime, and common kinematics.
- `Assets/Scripts/System/Api/Collision/CombatCollisionComponents.cs`: common
  collision components, collision active gate, and target snapshot buffer.
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`: target proxy
  spatial hashes used by projectile, tracking, and AOE collision.
- `Assets/Scripts/System/Core/CombatScope.cs`: shared `CombatScope` tag and
  `CombatFaction`.
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`: ECS target proxy create,
  push, and delete.
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`: single-pass
  hit finalize, ECS-owned target health/status application, and presentation
  bridge source data.
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`: projectile and
  lingering-AOE lifetime expiry.
- `Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`: shared timed-spawn
  producer for projectile, AOE, and targeted child events.
- `Assets/Scripts/System/Rendering/CombatRenderComponents.cs`: common render data,
  faction/type shared components, and render matrix prep.
- `Assets/Scripts/System/Rendering/CombatBatchedRenderSystem.cs`: shared batched
  render submission.
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`: shared VFX request
  dispatch from scope buffers.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`: projectile
  event/command data and impact/burst event builders.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`:
  projectile event drain, command expansion, and the discrete/continuous lane
  fan-out.
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs` and
  `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`:
  per-lane projectile slot top-up and reuse.
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs` and
  `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`:
  per-lane projectile collision consequences and projectile deactivation.
- `Assets/Scripts/System/Aoes/AoeSpawnPipeline.cs`: AOE event/command data and
  impact/on-hit event builders.
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs`: impact and lingering AOE event drain,
  command expansion, and spawn VFX request emission.
- AOE spawn apply file: impact and lingering AOE
  slot reuse and cold creation.
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`: AOE collision consequences
  and pulse deactivation.
- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`: targeted
  event/command data.
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`: targeted
  event drain and echo fan-out.
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`: the single
  targeted pool's slot reuse and cold creation.
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`: the chain walk, hit
  and VFX emission, and walk-end expiry.
- `Assets/Scripts/System/Status/StatusProcessSystem.cs`: status-stack
  detonation producer that emits projectile or AOE spawn events.

## Shared Combat Scope

`CombatRoot` instances share one ECS world and one ref-counted `CombatScope`
entity. The first scope acquire creates:

- `CombatScope`
- `DynamicBuffer<ProjectileSpawnEvent>`
- `DynamicBuffer<ImpactAoeSpawnEvent>` and `DynamicBuffer<LingeringAoeSpawnEvent>`
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
geometry, reserves an AOE id, builds one `AOE variant spawn event`, and appends it to the
shared scope `DynamicBuffer<ImpactAoeSpawnEvent>` or
`DynamicBuffer<LingeringAoeSpawnEvent>`.

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
- `Health`
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
- `AOE variant spawn event`
- `TargetedSpawnEvent`

Commands are one-entity allocation intent:

- `ProjectileSpawnCommand`
- `AoeSpawnCommand`
- `TargetedSpawnCommand`

Projectile flow:

`ProjectileSpawnEvent` -> `ProjectileSpawnExpansionSystem` ->
`ProjectileSpawnCommand` -> `ProjectileDiscreteSpawnApplySystem` or
`ProjectileContinuousSpawnApplySystem`, chosen by the command's
`ContinuousCollision` flag.

AOE flow:

`AOE variant spawn event` -> `AOE spawn expansion systems` -> `AoeSpawnCommand` ->
`ImpactAoeSpawnApplySystem` or `LingeringAoeSpawnApplySystem`.

Targeted flow:

`TargetedSpawnEvent` -> `TargetedSpawnExpansionSystem` -> `TargetedSpawnCommand`
-> `TargetedSpawnApplySystem`. One lane and one pool: a chain lives exactly as
long as its walk, so there is no variant to route.

Do not add a direct path that creates projectile, AOE, or targeted entities from
managed gameplay code, collision systems, status systems, or timed-spawn systems.

## Internal Event Producers

ECS systems that create follow-up combat effects produce spawn events, not
entities.

Current producers:

- `TimedSpawnSystem` reads stored template events, stamps source position,
  faction, source id, and deterministic tick data, then enqueues projectile,
  AOE, or targeted events.
- `ProjectileDiscreteCollisionSystem` and `ProjectileContinuousCollisionSystem`
  enqueue impact projectile events, impact AOE events, and on-hit targeted
  events from accepted projectile hits. Both go through the shared
  `ProjectileHitEmission` helpers, so the two lanes emit identical consequence
  data.
- `AoeCollisionCore` enqueues projectile burst events, on-hit AOE events, and
  on-hit targeted events from accepted AOE hits.
- `StatusProcessSystem` enqueues stack detonation projectile or AOE events. A
  targeted chain can apply the stacks that lead to a detonation, but is not
  itself a detonation output.
- Targeted producers route through `TargetedSpawnEmission`, which drops any
  event whose kind is not `IntervalChildKind.Targeted`.

These producers write to `ProjectileSpawnExpansionSystem.EventQueue`, the
matching AOE expansion system `EventQueue`, or
`TargetedSpawnEventSingleton.EventQueue`. Producer job handles are combined into
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

`AOE spawn expansion systems` drains:

- `ImpactAoeSpawnExpansionSystem.EventQueue` or `LingeringAoeSpawnExpansionSystem.EventQueue`
- shared scope `DynamicBuffer<ImpactAoeSpawnEvent>` or
  `DynamicBuffer<LingeringAoeSpawnEvent>`

It computes bounds, resolves deterministic ids, emits spawn VFX requests with the
resolved graph-kind ids stored on `AoeSpawnCommand`, and writes commands into
impact or lingering command containers.

`TargetedSpawnExpansionSystem` drains:

- `TargetedSpawnEventSingleton.EventQueue`
- shared scope `DynamicBuffer<TargetedSpawnEvent>`

It dereferences the template, resolves deterministic ids, and fans one event out
into `EchoCount` commands. There is deliberately no scatter, spread, or jitter
math on this path: forks differ by acquisition rank at resolve time, not by
position.

Apply systems should not interpret volley, scatter, jitter, or pattern math.

## Apply Ownership

Apply systems own reuse and cold creation.

Projectile apply uses one disabled-slot query:
`WithAll<ProjectileTag>()` plus `WithDisabled<Active>()`. Timed vs non-timed
projectiles reuse across the same pool; `TimedSpawnComponent` is enabled or
disabled during reset.

AOE apply is split by pool. Impact AOEs reuse slots without
`LingeringAoeTag`. Lingering AOEs reuse slots with `LingeringAoeTag` and plain
`CombatLifetimeComponent` timer data. Timed vs non-timed lingering AOEs reuse
across the same lingering pool; `TimedSpawnComponent` is enabled or disabled
during reset.

Targeted apply uses one disabled-slot query: `WithAll<TargetedTag>()` plus
`WithDisabled<Active>()`. There is no variant discriminator and no second pool.
Targeted archetypes carry no `CombatCollisionComponent` and no
`CombatCollisionActiveTag`, because a chain has no gameplay collider.

Reusable slots are found with `WithDisabled<Active>()`. Each apply system runs
one single-threaded Burst reuse job that resets all per-instance data and
enables the needed enableable components. Commands after the reused prefix are
cold-created through an `EntityCommandBuffer`.

Cold-created entities become part of the reusable pool after their first
despawn.

## Despawn And Reuse

Normal projectile, AOE, and targeted despawn disables enableable components. It
does not destroy entities.

Shared occupancy:

- `Active`

Domain enableable state:

- `CombatCollisionActiveTag` �?one generic collision gate shared by projectiles
  and both AOE archetypes; collision queries still discriminate domain via
  `ProjectileTag` / `AoeTag`.
- `ArmingTag` �?pause overlay while `CombatArmingComponent.Remaining` counts down
  (see Arming below).
- `ProjectileTrackingComponent`

Sprite visibility is **not** a separate enableable gate. There is no
`CombatRenderActiveTag`; the render prepare job derives visibility from `Active`
(and suppresses arming entities via the `ArmingTag` mask), so liveness/visibility
and pool reuse all key on the single `Active` bit.

Despawn routes through the shared `CombatDeathUtility.Kill` helper, which disables
`Active` (plus `CombatCollisionActiveTag` / `ArmingTag` where the entity carries
them) and emits expire VFX from one place:

- `CombatLifetimeSystem` kills projectiles and lingering AOEs on lifetime expiry,
  and has a third job that kills a targeted entity if it ever outlives its
  compiled fail-safe lifetime.
- Both projectile collision systems kill the source projectile when it is
  invalid, expired, or consumed by pierce, via `ProjectileHitEmission.Deactivate`.
- `AoeCollisionCore` kills invalid AOEs and impact AOEs after their collision pass.
- `TargetedResolveSystem` kills the chain the moment its walk ends — after the
  last link, or after a link that finds nothing. This is the normal targeted
  despawn path; the lifetime job above is only a backstop.

This pattern keeps entities in stable archetypes and avoids structural churn on
hot combat paths.

## Arming (initial delay)

Projectiles, AOEs, and targeted chains may spawn with an authored
`ArmSeconds > 0` initial delay.
Arming is a **pause overlay**, not a separate lifecycle phase: spawn apply sets the
normal armed gate values as usual, then additionally enables `ArmingTag` and sets
`CombatArmingComponent.Remaining = ArmSeconds`.

- While `ArmingTag` is enabled the entity is live and reuse-protected (`Active`
  stays enabled) but frozen: movement, all three lifetime jobs, all three
  collision jobs, the targeted resolve, timed-spawn, tracking, and the AOE
  pulse-VFX tick exclude it via
  `WithDisabled<ArmingTag>`, and render prepare degenerates it to an invisible
  instance. Only the telegraph VFX (`AoeVfxIds.ArmingId`) plays.
- `CombatArmingSystem` (runs before `CombatLifetimeSystem`, split into a
  projectile job, an AOE job, and a targeted job like `CombatLifetimeSystem`)
  counts `Remaining`
  down and disables `ArmingTag` at zero. The entity then resumes with its
  already-correct gate values �?no gate rewrite.

### Arming VFX

- **Telegraph (`AoeVfxIds.ArmingId`):** emitted once at spawn (both expansion systems) when
  `ArmSeconds > 0`. It is a dedicated `ArmingEffect` `VisualEffectAsset`,
  authored on the prefab and registered once per graph asset in `SkillDriver`.

- **Spawn burst (`AoeVfxIds.SpawnId`):** for an arming AOE the spawn burst is deferred to
  arm completion �?the AOE arming job emits it when it clears `ArmingTag`, so the
  burst reads as "the AOE going live," not "the slot being materialized." A
  non-arming AOE still emits `AoeVfxIds.SpawnId` at spawn. (Projectiles have
  no AOE VFX emission.)
- **Pulse (`AoeVfxIds.PulseId`):** the lingering pulse only ticks while armed; its interval
  does not advance during arming.
- `ArmSeconds` is a plain `float` on `ProjectileSpawnCommand` / `AoeSpawnCommand`,
  authored on the skill definitions and threaded through the runtime definitions.
- The two components are split (`ArmingTag` enableable + plain
  `CombatArmingComponent`) so the arming job never takes one component as both
  `ref` data and `EnabledRefRW`.

> Any `IJobEntity` that reads the arming bit via `EnabledRefRW<ArmingTag>` and is
> scheduled with an **explicit** `EntityQuery` must include `ArmingTag`
> (`WithDisabled<ArmingTag>()`) in that query, or scheduling throws �?the
> `[WithDisabled]` attribute is ignored for explicitly-scheduled jobs.

## Consequence Events

Projectile and AOE collision systems and the targeted resolve emit plain data
consequences. They do not call managed target callbacks and do not allocate
follow-up entities directly.

Current consequence paths include:

- hit events into `CombatApplyFinalizeSingleSystem`; targeted links set
  `CombatHitEvent.DamageScale` to their falloff, while other producers leave it
  unset (`0`, read as `1`)
- `ProjectileSpawnEvent` values for impact projectiles, AOE projectile bursts,
  and stack detonation projectiles
- `AOE variant spawn event` values for impact AOEs, on-hit AOEs, timed AOEs, and stack
  detonation AOEs
- `TargetedSpawnEvent` values for on-impact and interval-triggered chains
- `CircularVfxSpawnRequest` / `TimedCircularVfxSpawnRequest` values written to the per-shape VFX
  queues via `VfxEmit`, plus `LineSegmentVfxSpawn` values from the targeted
  resolve

Follow-up spawns stay in ECS event flow and return to expansion/apply.

## Rendering And VFX

Projectile, AOE, and targeted visuals share the sprite-atlas render path. A
targeted chain's sprite is optional: with none authored the `LineSegment` VFX
carries the whole visual.

Runtime entities carry common render data:

- `CombatRenderComponent`
- `CombatRenderKindId`

Sprite visibility derives from `Active` (and `ArmingTag`); there is no separate
render-active gate.

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
systems call `VfxEmit.Enqueue`, writing `CircularVfxSpawnRequest` or `TimedCircularVfxSpawnRequest`
into the per-shape native queues owned by `CombatAoeVfxDispatchSystem`. That system
completes producers, buckets each shape by graph id, and dispatches through
`CombatVfxRoot`.

## Teardown Exceptions

Normal projectile, AOE, and targeted despawn should not delete entities.

Deliberate deletion still exists for lifecycle teardown:

- `CombatRoot.OnDestroy` destroys projectile and AOE entities for that root's
  faction. **Known gap:** it holds no equivalent targeted query, so targeted
  entities survive a root teardown until the shared world is released.
- `CombatScopeOwner.Release` destroys the shared `CombatScope` after the last
  combat root releases it.
- `CombatTargetProxy.Delete` destroys target proxy entities when actor target
  proxies are no longer valid.

These are not normal combat despawn paths.

## Frame Timing

Important ordering:

1. `CombatLifetimeSystem` expires existing projectiles and AOEs, and any
   targeted chain past its fail-safe lifetime.
2. `TimedSpawnSystem` emits energy-driven child spawn events.
3. Projectile tracking, movement, contact gates, and collision run.
4. AOE pulse VFX and collision run.
5. `TargetedResolveSystem` walks chains, emits hits and link VFX, and expires
   chains whose walk has ended.
6. `StatusProcessSystem` evaluates stack state accrued by prior updates and
   emits stack detonation spawn events.
7. `CombatApplyFinalizeSingleSystem` applies current hit events to ECS target
   health and stack state. Newly accrued stacks become eligible on the next
   simulation update.
8. `ProjectileSpawnExpansionSystem`, `AOE spawn expansion systems`, and
   `TargetedSpawnExpansionSystem` drain completed event producers plus managed
   scope buffers.
9. Projectile, AOE, and targeted apply systems reuse disabled slots or
   cold-create overflow.
10. Render preparation and presentation systems run.

Because apply runs after movement and collision, newly spawned/reused
projectiles, AOEs, and chains do not move, collide, resolve, or fire timed spawns
until the next simulation update.

## Change Rules

- Keep event data as gameplay intent.
- Keep command data as one-entity allocation intent.
- Keep spawn math in expansion systems.
- Keep reuse and cold creation in apply systems.
- Keep hot despawn as enable/disable, not destroy/create.
- Require domain tags on projectile, AOE, and targeted systems.
- Do not treat `Active`, common combat components, or scope membership as a
  domain marker.
- Do not route internal follow-up spawns through managed target callbacks.
- Do not read `TargetCompanion` from simulation jobs.
- Delete projectile, AOE, and targeted entities only for root/scope teardown.

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
- `Assets/Tests/EditMode/TargetedSpawnPipelineEditModeTests.cs`: targeted event
  drain, echo fan-out into one command per entity, and pool reuse.
- `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`: root cast, chain
  falloff and stagger, trigger-driven chains, link VFX, and mixed-scene pool
  cleanup across the projectile, AOE, and targeted pools.
