# Projectile System

All docs in `Docs/` are design references. They describe current implementation
intent and should be checked against code before large changes.

This is the detailed projectile ECS doc. Use [index.md](./index.md) for the
simulation overview and aspect map.

## Summary

Projectiles are high-count combat entities simulated with Unity Entities/DOTS.
Player, mobs, walls, camera, and authoring remain normal Unity scene objects.
Projectile gameplay state does not live on one GameObject per projectile.

Current implementation is built around these rules:

- `CombatRoot` is the scene-object bridge and authoring owner.
- One `CombatRoot` exists per firing faction: player combat and mob combat.
- All combat roots share one ref-counted ECS world and one ref-counted
  `CombatScope` entity.
- `CombatFaction` on events, entities, target proxies, VFX requests, and render
  shared components separates player-faction and mob-faction data.
- Spawn intent is separate from entity allocation intent:
  `ProjectileSpawnEvent` may describe a volley, while
  `ProjectileSpawnCommand` describes exactly one projectile entity.
- Projectiles split into two lanes at spawn: a **discrete** lane that tests only
  the current-position footprint and may home, and a **continuous** lane that
  additionally sweeps a corridor over the frame's travel and never homes. The
  lanes have separate archetypes, reuse pools, and collision systems; membership
  is authored, not derived at runtime.
- Runtime reuse is based on the generic enableable `Active` component.
- Damage crosses back to MonoBehaviours only through `DamageDispatchBridge`.
- Target data is read from ECS proxy entities, not from live colliders or
  target-snapshot buffers.

Primary uses:

- player projectiles hitting mobs
- mob projectiles hitting the player
- timed child projectiles
- impact projectile bursts
- impact AOEs
- batched projectile rendering

Non-goals:

- a global projectile manager that owns every target and callback
- live trigger callbacks as the authoritative high-count hit path
- managed target access from simulation jobs

## Main Files

- `Assets/Scripts/System/Core/CombatRoot.cs`: per-faction scene bridge,
  template registration, spawn submission, render resources, target registry,
  ECS world/scope acquire and release.
- `Assets/Scripts/System/Core/CombatScope.cs`: shared scope tag and
  `CombatFaction`.
- `Assets/Scripts/System/Lifetime/CombatLifecycleComponents.cs`: common kinematics,
  collision, hit payload, generic `Active`, common lifetime, and legacy target
  element type.
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`: ECS target proxy entity
  lifecycle and target shape push.
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`: native hit queue
  finalize plus managed replay into `ICombatTarget.ReceiveHits`.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`:
  `ProjectileSpawnEvent`, `ProjectileSpawnCommand`, and helpers for impact and
  burst events.
- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`:
  `ProjectileTag`, identity, hit, tracking, `ProjectileContinuousTag`,
  `ProjectileContinuousStepComponent`, and the contact gate buffer.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`: drains
  spawn events, expands volley data, and fans each resolved command into the
  discrete or continuous command list.
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`: applies discrete
  projectile commands by reusing disabled entities or cold-creating overflow entities.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`: applies
  continuous projectile commands into their distinct pool/archetype.
- `Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`: emits child
  projectile spawn events from active child-spawner projectiles.
- `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs`: homing target
  refresh, acquisition, and steering.
- `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`: position
  integration and bounds refresh.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousOriginSystem.cs`: captures each
  continuous projectile's position before shared movement.
- `Assets/Scripts/System/Projectiles/ProjectileContactGateSystem.cs`: repeat-hit
  cooldown expiry.
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`: spatial hash
  target broad phase, narrow-phase collision, pierce/contact state, damage
  event emission, impact spawn event emission, and source
  deactivation.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`: continuous-lane
  discrete-plus-corridor collision and nearest-first impact handling.
- `Assets/Scripts/System/Api/Collision/Narrowphase/CombatSweepMath.cs`: travel
  corridor construction, perpendicular support extent, and closest-approach
  parameter used only by the continuous lane.
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry using `CombatLifetimeComponent` and `Active`.
- `Assets/Scripts/System/Rendering/CombatRenderComponents.cs`: shared render data
  and render matrix preparation.
- `Assets/Scripts/System/Rendering/CombatBatchedRenderSystem.cs`: presentation
  submission through `Graphics.RenderMeshInstanced`.

## Runtime Ownership

`CombatRoot` may touch Unity objects. It reads serialized fields, validates
setup, registers projectile templates, builds render resources, owns the target
registry for its faction, and appends managed spawn submissions to the shared
scope buffer.

Projectile ECS systems must not touch GameObjects, Transforms, Colliders, or
Physics2D. They operate on:

- projectile entities
- target proxy entities
- native event queues
- common ECS components
- render buffers

The current scope model is important: there is one shared `CombatScope` entity,
not one scope entity per faction. Faction is carried explicitly on:

- `ProjectileSpawnEvent`
- `ProjectileSpawnCommand`
- `ProjectileIdentityComponent`
- `TargetFaction`
- `DamageReplayEvent` source metadata through identity values
- per-entity render batch data

Projectile systems must query `ProjectileTag` plus the generic components they
need. `Active` alone never makes an entity a projectile.

## Target Proxy Bridge

Targets implement `ICombatTarget`. Player and mob roots store their current
proxy `Entity` on the interface property.

Registration path:

1. A target registers with a root's `CombatTargetRegistry<ICombatTarget>`.
2. The registry creates or reuses a target proxy entity through
   `CombatTargetProxy.Create`.
3. The proxy receives `TargetProxyTag`, `TargetPosition`,
   `TargetCollisionShape`, `TargetFaction`, and managed `TargetCompanion`.
4. `PlayerRoot.Update` and `MobRoot.Update` push proxy position and shape before
   simulation.
5. Dead or disabled targets queue proxy deletion and delete in `LateUpdate`.

Simulation jobs read only unmanaged proxy data. The managed `TargetCompanion` is
only read by `DamageDispatchBridge` after damage events have been finalized.

`CombatTargetElement` still exists in common ECS components for compatibility,
but current projectile collision, tracking, and damage use target proxy queries.
Do not build new projectile logic on `CombatTargetElement`.

## Spawn Pipeline

Projectile spawning has two explicit data levels.

`ProjectileSpawnEvent` is intent. It can include count, spread, jitter, base
direction, speed, child-spawner data, hit payload, tracking config, and render
data. Managed submissions from `CombatRoot.Spawn` append events to the shared
scope buffer. Internal producers write events to
`ProjectileSpawnExpansionSystem.EventQueue`.

`ProjectileSpawnCommand` is allocation intent. It describes one projectile:
one id, one position, one velocity, one collision shape, one hit payload, one
tracking state, and one render state. It contains no count, spread, jitter, or
base direction.

Current flow:

1. Managed code or ECS producers emit `ProjectileSpawnEvent`.
2. `ProjectileSpawnExpansionSystem` drains both the native event queue and the
   shared scope `DynamicBuffer<ProjectileSpawnEvent>`.
3. Expansion resolves volley math, spread, jitter, velocity, ids, bounds, and
   per-shot render Z.
4. Expansion fans commands into discrete and continuous projectile command lists using the
   template-resolved `ContinuousCollision` flag.
5. Each projectile apply system consumes its matching list:
   `ProjectileDiscreteSpawnApplySystem` reads `DiscreteCommands`,
   `ProjectileContinuousSpawnApplySystem` reads `ContinuousCommands`.
6. Each apply system counts its own disabled slots through a query built from
   `WithAll<ProjectileTag>()`, `WithDisabled<Active>()`, and the lane
   discriminator — `WithNone<ProjectileContinuousTag>()` for discrete,
   `WithAll<ProjectileContinuousTag>()` for continuous.
7. If that count is short of the command count,
   `SpawnPoolTopUp.EnsureDisabledSlots` cold-creates the deficit against that
   lane's archetype with `EntityManager.CreateEntity` and disables `Active` on
   each. This is a structural change and must happen before chunk arrays or
   type handles are fetched.
8. Apply then captures the disabled chunks and schedules one single-threaded
   Burst reuse job that walks them with one command cursor, writing every
   command into a slot. Both lanes share
   `ProjectileSpawnApplyUtility.WriteCommon` for the components the archetypes
   have in common; the continuous job additionally seeds
   `ProjectileContinuousStepComponent.Origin` to the spawn position.

The apply systems do not interpret volley patterns. Expansion owns spawn math,
including the lane decision.

## Entity Archetypes And Reuse

Both projectile lanes carry:

- `ProjectileTag`
- `ProjectileIdentityComponent`
- generic `Active`
- `CombatLifetimeComponent`
- `CombatKinematicsComponent`
- `CombatCollisionComponent`
- `ProjectileHitComponent`
- `CombatCollisionActiveTag`
- `ArmingTag`
- `CombatArmingComponent`
- `ProjectileContactGateElement`
- `TimedSpawnComponent`
- `TimedSpawnStateComponent`
- common render components

`CombatCollisionActiveTag` is the single generic collision gate shared with AOEs
(the collision query still filters by `ProjectileTag`). `ArmingTag` +
`CombatArmingComponent` provide the optional `ArmSeconds` initial-delay pause; see
Arming in [project-aoe-system-common.md](./project-aoe-system-common.md).

There are two projectile archetypes. The discrete archetype carries
`ProjectileTrackingComponent`; the continuous archetype instead carries
`ProjectileContinuousTag` and `ProjectileContinuousStepComponent`, and has no
tracking component at all. `ProjectileContinuousTag` is the discriminator every
lane-specific query uses: the continuous systems `WithAll` it, the discrete ones
`WithNone` it. It is added at entity creation and never added or removed at
runtime, so a projectile cannot change lane. Timed child spawning is selected
by enabled `TimedSpawnComponent`; non-timed projectiles still carry it disabled.

Runtime despawn disables `Active` through the shared `CombatDeathUtility.Kill`
helper; sprite visibility derives from `Active`, so there is no separate render
gate to clear. It does not destroy the entity during normal churn. Cold-created
entities are kept until their owning `CombatRoot` is destroyed.

Each lane has its own reuse pool. `ProjectileDiscreteSpawnApplySystem` builds its
dead-slot query with `WithNone<ProjectileContinuousTag>()`;
`ProjectileContinuousSpawnApplySystem` builds its own with
`WithAll<ProjectileContinuousTag>()`. The two pools never share slots, so a
shortage in one lane cannot be covered by dead entities in the other.

Projectile reuse is single-cursor and deterministic per lane. Each lane's reuse
job scans that lane's disabled chunks in query order and consumes its own
command list in order.

Note the ordering: cold creation runs *before* the reuse job, topping the pool
up to demand, and the reuse job then fills every slot including the freshly
created ones. So the lane's `Reuse` counter equals its command count on a normal
frame, and its `TopUp` counter is what indicates true pool shortage for that
lane's archetype. The two lanes report these counters separately, under
`ProjectileDiscreteSpawnApplySystem.*` and
`ProjectileContinuousSpawnApplySystem.*`.

## Timed Child Spawns

`TimedSpawnSystem` replaces the old child request buffer path. It queries active
finite-lifetime entities with enabled `TimedSpawnComponent`, ticks their child
spawn cooldown using variable `deltaTime`, catches up missed intervals, and
enqueues `ProjectileSpawnEvent` or `AOE variant spawn event` values directly into the
matching expansion queue.

Child projectiles use the same projectile pool. They may carry damage, hit-energy
effect, impact AOE, and impact projectile snapshots. They only emit interval
children when `TimedSpawnComponent` is enabled.

## Launch Aim

Trigger-authored launch aim gives one triggered projectile wave a one-time
initial-velocity redirect toward the nearest eligible target under target
policy, resolved once per
`ProjectileSpawnEvent` inside `ProjectileSpawnExpansionSystem`, before
count/spread/jitter fan-out and before the discrete/continuous lane split. It
is authored on the incoming `TriggerLink`, not on `SkillDefinition`: root and
player casts never carry it, and it is copied onto the compiled
`RuntimeProjectileDefinition` only for a trigger's compiled projectile target
(interval child, on-hit target, or hit-energy output), then into the
`ProjectileSpawnCommand` template alongside the other template-level fields
that participate in `SpawnTemplateHash`.

When a wave's template carries `LaunchAimMode == NearestHostile`, `Range > 0`,
and a valid faction, expansion queries the shared `TargetSpatialHashSingleton`
through `CombatTargetAcquisition.TrySelectNthNearest` (the same
nearest-eligible-target selection targeted chains use, rank 0, excluding the
event's contact-gate seed target) once per event. On success it recomputes every shot in the wave as one
radial nova oriented from the acquired direction: shot `i`'s velocity is
`Rotate(aimDirection, 360 degrees * i / count) * Speed`, so shot `0` points
exactly at the target and shots `1..count-1` occupy the remaining equally
`360/count`-spaced slots around it. This bypasses the stored
forward/side-spray/radial pattern and its spread/jitter/RNG entirely for that
wave — a successful aim is a nova, not a partial pattern edit. A missing hash
singleton, non-positive range, `CombatFaction.None`, no eligible target in
range, or a target coincident with the spawn position all fall through to the
unmodified stored pattern output.

Both the discrete and continuous lanes receive the same aimed nova, since
acquisition runs once per event before `WriteCommand` routes each shot by
`ContinuousCollision`. Launch aim only sets each shot's initial velocity; it
adds no per-frame steering, target ownership, or entity state, and it does not
enable tracking — the continuous archetype still has no
`ProjectileTrackingComponent`, and a discrete projectile's separately authored
tracking (see [Tracking And Movement](#tracking-and-movement)) may still steer
after an aimed launch. `ProjectileSpawnExpansionSystem` schedules its
acquisition read dependent on `TargetSpatialHashSingleton.BuildHandle` and
publishes its handle into `ConsumerHandle`, exactly like `TargetedResolveSystem`.

## Tracking And Movement

Tracking is discrete-lane only. `ProjectileTrackingSystem` builds target lookup
data from `TargetProxyTag`, `TargetPosition`, and `TargetFaction`, then runs a
single fused acquisition-and-steering job so both phases share one query pass
over `CombatKinematicsComponent`/`ProjectileTrackingComponent` instead of two.
It evaluates `TargetFaction.CanHit(identity.Faction, targetFaction)` per
candidate, so a hostile-default target still behaves as before, but an
`AllowedFactionOnly` target can accept an attacker of its own registered
faction. It needs no lane filter: continuous projectiles are excluded
structurally because their archetype has no `ProjectileTrackingComponent`.

Movement is shared data math. `ProjectileContinuousOriginSystem` captures only continuous step-start
positions before `ProjectileMovementSystem` integrates both lanes and recomputes current
world bounds through `CombatCollisionMath`.

## Collision And Consequences

Each lane has its own collision system. This section states the contract both
obey; see [Continuous Projectile Lane](#continuous-projectile-lane) for what the
continuous system adds on top of it.

`ProjectileDiscreteCollisionSystem` (`WithNone<ProjectileContinuousTag>`) and
`ProjectileContinuousCollisionSystem` (`WithAll<ProjectileContinuousTag>`) own
hit qualification and source projectile state for their lane. They may:

- query target proxy data
- read the shared `TargetSpatialHashSingleton` broad phase and reject any
  candidate that fails `TargetFaction.CanHit` against the projectile's own
  faction
- perform target bounds and narrow-phase checks
- check and refresh per-projectile contact gates
- decrement pierce count
- disable `Active` (via `CombatDeathUtility.Kill`) when the source projectile is
  consumed
- emit plain data events for damage, impact projectiles, and impact AOEs

Neither builds the spatial hash; `TargetSpatialHashSystem` owns it and both
lanes register themselves as consumers on its handle.

They may not:

- call managed target callbacks
- spawn entities directly
- instantiate GameObjects
- read `TargetCompanion`

Accepted hits can produce:

- `DamageReplayEvent` into `DamageDispatchBridge.DamageQueue`
- `ProjectileSpawnEvent` into projectile expansion for impact projectile bursts
- `AOE variant spawn event` into AOE expansion for impact AOEs

Damage is finalized by `DamageFinalizeSystem` before spawn expansion. Managed
replay runs later in `DamageDispatchBridge` during `PresentationSystemGroup`.

## Continuous Projectile Lane

Authored `continuousCollision` on the projectile's `SkillDefinition` selects the
continuous lane at compile time. `SkillSetCompiler` copies it to
`RuntimeProjectileDefinition.ContinuousCollision`, which reaches ECS as
`ProjectileSpawnCommand.ContinuousCollision`. It is authored, never derived from
speed in simulation. Sweep and tracking cannot combine: the continuous archetype
has no `ProjectileTrackingComponent`, and the compiler sets
`RuntimeProjectileDefinition.SpawnBlocked` when both are on. `SkillDriver` then
refunds the fire instead of casting and surfaces a
`ContinuousCollisionCannotTrack` validation error.

Each continuous tick tests the ordinary projectile footprint at current `Position` plus an oriented
travel corridor from captured `Origin` to `Position`. The corridor has half-length exactly
half the travel distance and half-width from silhouette support perpendicular to travel; it
has no end caps because the discrete tests cover endpoints. Existing rectangle narrowphase
performs corridor overlap. Direction and shape rotation stay constant across a swept step, so
this geometry is exact for the lane.

`ProjectileContinuousOriginSystem` writes `Origin` every frame before movement,
and skips arming projectiles along with movement itself. On reuse the apply
system seeds `Origin` to the spawn position, so a projectile's first simulated
frame has a zero-length segment; `TryBuildTravelCorridor` returns false for it
and only the discrete footprint test runs that frame.

The corridor always spans full movement. No speed threshold or target-size check runs in ECS;
the tracking tunneling advisory is compile-time only. Candidate ordering and impact position
use closest approach on the segment. `BoundsMin/Max` remain current-position bounds; corridor
bounds are temporary collision-job inputs.

Because one swept step can overlap several targets at once, the continuous lane
resolves hits differently from the discrete one. It collects deduplicated
candidates into a fixed list bounded by `CollisionConstants.MaxContinuousHitsPerFrame`
(16), sorts them by closest-approach parameter, and applies damage, contact
gates, and pierce in that nearest-first order. When pierce goes below zero
mid-list it stops there and writes `kinematics.Position` back to that
candidate's impact point, so the projectile dies where it was consumed rather
than at the far end of the step. The discrete lane has no such position
rewrite.

## Lifetime

Projectile lifetime uses the shared `CombatLifetimeSystem`. The projectile job
ticks `CombatLifetimeComponent.Remaining`, then calls `CombatDeathUtility.Kill`
to disable `Active` when time reaches zero.

Projectile collision can also deactivate a projectile immediately when a valid
hit consumes the source.

## Rendering

Projectile visuals draw through one shared, manually-assembled `SpriteAtlas`
asset (each kind's `Sprite` added as a packable in the editor ahead of time
and packed, not packed at runtime). `CombatRoot` builds sprite render
resources by registering each
kind's sprite with the shared `CombatRenderResourceRegistry`, which computes
that sprite's UV rect within the configured atlas and throws if the sprite
isn't actually part of it. Runtime entities carry common render components
plus a plain `CombatRenderKindId` copied from `RenderTypeId` (a kind
identifier only) and a `UvRect` on `CombatRenderComponent`, computed once
when the spawn command is built.

`CombatRenderPrepareSystem` writes matrices for active projectile and AOE
entities. `CombatBatchedRenderSystem` runs in `PresentationSystemGroup`,
filters by domain tag and active render tag, and in one scatter pass fills a
shared transform buffer and a parallel UV-rect buffer by reading each active
entity's own `CombatRenderComponent.UvRect` directly (no per-frame lookup),
then submits `Graphics.RenderMeshInstanced` against the registry's shared
mesh/material, chunked at the 1023-instance cap.

Projectiles always use the batched sprite renderer described above. It is the
only projectile visual path; `ProjectileMovementSystem` emits no VFX requests.

## Current Frame Order

Important simulation ordering:

1. `ProjectileSimulationSystem` starts the projectile phase and exposes active
   counts.
2. `CombatLifetimeSystem` expires projectile and AOE lifetime.
3. `TimedSpawnSystem` emits child spawn events.
4. `ProjectileTrackingSystem` updates homing data (discrete lane only).
5. `ProjectileContinuousOriginSystem` captures continuous step origins; `ProjectileMovementSystem` moves
   both lanes and refreshes bounds.
6. `ProjectileContactGateSystem` expires projectile contact gates.
7. Discrete and continuous projectile collision systems emit damage and spawn events.
8. AOE collision systems run after projectile collision.
9. `DamageFinalizeSystem` freezes the native damage queue.
10. `ProjectileSpawnExpansionSystem` and `AOE spawn expansion systems` drain events
    and produce commands. Projectile expansion writes into two command lists,
    one per lane.
11. `ProjectileDiscreteSpawnApplySystem`, `ProjectileContinuousSpawnApplySystem`,
    and the AOE apply systems reuse disabled slots from their own pool and
    cold-create overflow.
12. `CombatRenderPrepareSystem` prepares render matrices.
13. Presentation systems dispatch damage, VFX, and render batches.

Spawned/reused projectiles do not move, collide, or emit their own timed spawns
until the next simulation update because apply runs after movement/collision.

## Authoring Notes

Projectile authoring flows through skills and spawn requests:

- `ProjectileSpawnRequest` is the managed request passed to `CombatRoot.Spawn`.
  It carries `ContinuousCollision`, which `CombatRoot` stamps onto the spawn
  event.
- `SkillDefinition` projectile entries and the `RuntimeProjectileDefinition`
  they compile into provide shape, speed, lifetime, damage, count, spread,
  pierce, tracking, lane membership (`continuousCollision`), impact
   AOE/projectile, and child-spawn data.
- `TriggerLink` (not `SkillDefinition`) authors trigger-only projectile
  launch-aim mode/range; `SkillSetCompiler` copies it onto a trigger's compiled
  projectile target only, never the root. See
  [Launch Aim](#launch-aim).
- `CombatRoot.RegisterTemplate` registers projectile visual/collision templates
  and builds render resources.
- Spawn data is snapshotted into events before ECS simulation sees it.

Do not add runtime simulation dependencies back to ScriptableObjects or prefab
instances. Spawn requests should carry the values the runtime needs.

## Performance Notes

Current performance-sensitive choices:

- no one GameObject per projectile
- no live Physics2D trigger hit path for projectiles
- proxy targets instead of live collider reads in simulation
- native queues/lists for hit, spawn, and VFX events
- `Active` enable/disable for reuse
- one single-threaded Burst apply job per reuse pool, so the discrete and
  continuous lanes each get one
- the lane split keeps corridor construction and candidate sorting off the
  discrete lane entirely, rather than branching per entity inside one job
- batched render submission
- continuous collision limits candidate work to `MaxContinuousHitsPerFrame` while retaining nearest
  candidates; it never clamps travel coverage

Revisit only with profiling:

- spatial hash cell sizing
- dead-slot scan cost in spawn apply, per lane
- continuous lane chunk occupancy when few continuous projectiles exist
- damage aggregation across many hits on few targets
- render batch collection cost

## Known Gaps

- Damage replay is grouped by target in `DamageDispatchBridge`, but damage is
  still represented as one replay event per qualifying hit before dispatch.
- Crit rolling currently happens on the managed bridge main thread.
- `CombatTargetElement` remains in common data but should be considered legacy
  for new projectile work.
- Dedicated projectile stress scenes and hard pass/fail thresholds are still
  limited.
