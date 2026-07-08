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
- projectile hit VFX
- batched projectile rendering

Non-goals:

- a global projectile manager that owns every target and callback
- live trigger callbacks as the authoritative high-count hit path
- managed target access from simulation jobs

## Main Files

- `Assets/Scripts/System/Common/CombatRoot.cs`: per-faction scene bridge,
  template registration, spawn submission, render resources, target registry,
  ECS world/scope acquire and release.
- `Assets/Scripts/System/Common/CombatScope.cs`: shared scope tag and
  `CombatFaction`.
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`: common kinematics,
  collision, hit payload, generic `Active`, common lifetime, and legacy target
  element type.
- `Assets/Scripts/System/Common/CombatTargetProxy.cs`: ECS target proxy entity
  lifecycle and target shape push.
- `Assets/Scripts/System/Common/DamageDispatchBridge.cs`: native damage queue
  finalize plus managed replay into `ICombatTarget.ReceiveHits`.
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`:
  `ProjectileSpawnEvent`, `ProjectileSpawnCommand`, and helpers for impact and
  burst events.
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`: drains
  spawn events and expands volley data into one ordered command list.
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`: applies
  basic and child-spawner projectile commands by reusing disabled entities or
  cold-creating overflow entities.
- `Assets/Scripts/System/Projectile/TimedProjectileSpawnSystem.cs`: emits child
  projectile spawn events from active child-spawner projectiles.
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`: homing target
  refresh, acquisition, and steering.
- `Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs`: position
  integration and bounds refresh.
- `Assets/Scripts/System/Projectile/ProjectileContactGateSystem.cs`: repeat-hit
  cooldown expiry.
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`: spatial hash
  target broad phase, narrow-phase collision, pierce/contact state, damage
  event emission, impact spawn event emission, VFX event emission, and source
  deactivation.
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry using `CombatLifetimeComponent` and `Active`.
- `Assets/Scripts/System/Rendering/CombatRenderComponents.cs`: shared render data
  and render matrix preparation.
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`: presentation
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
- render and VFX request buffers

The current scope model is important: there is one shared `CombatScope` entity,
not one scope entity per faction. Faction is carried explicitly on:

- `ProjectileSpawnEvent`
- `ProjectileSpawnCommand`
- `ProjectileIdentityComponent`
- `TargetFaction`
- `DamageReplayEvent` source metadata through identity values
- `VfxPendingSpawn`
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
4. Expansion writes commands into one projectile command list.
5. `ProjectileSpawnApplySystem` consumes the command list.
6. The apply system captures disabled projectile chunks with
   `WithAll<ProjectileTag>()` and `WithDisabled<Active>()`.
7. Apply schedules one single-threaded Burst reuse job that walks disabled
   chunks with one command cursor and resets disabled slots.
8. Any commands not satisfied by reuse are cold-created through an
   `EntityCommandBuffer`.

The apply systems do not interpret volley patterns. Expansion owns spawn math.

## Entity Archetypes And Reuse

Projectiles carry:

- `ProjectileTag`
- `ProjectileIdentityComponent`
- generic `Active`
- `CombatLifetimeComponent`
- `CombatKinematicsComponent`
- `CombatCollisionComponent`
- `ProjectileHitComponent`
- `ProjectileTrackingComponent`
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

There is one projectile archetype. Timed child spawning is selected by enabled
`TimedSpawnComponent`; non-timed projectiles still carry the component, but it
is disabled. This removes the former basic vs timed projectile archetype
split at a small chunk-width cost.

Runtime despawn disables `Active` through the shared `CombatDeathUtility.Kill`
helper; sprite visibility derives from `Active`, so there is no separate render
gate to clear. It does not destroy the entity during normal churn. Cold-created
entities are kept until their owning `CombatRoot` is destroyed.

Projectile reuse is single-cursor and deterministic. The reuse job scans
disabled chunks in query order, consumes commands in command-list order, and
returns the reused prefix length. Cold creation handles the remaining suffix, so
cold count indicates true pool shortage for the projectile archetype.

## Timed Child Spawns

`TimedSpawnSystem` replaces the old child request buffer path. It queries active
finite-lifetime entities with enabled `TimedSpawnComponent`, ticks their child
spawn cooldown using variable `deltaTime`, catches up missed intervals, and
enqueues `ProjectileSpawnEvent` or `AOE variant spawn event` values directly into the
matching expansion queue.

Child projectiles use the same projectile pool. They may carry damage, stack
effect, impact AOE, and impact projectile snapshots. They only emit interval
children when `TimedSpawnComponent` is enabled.

## Tracking And Movement

Tracking uses target proxy entities. `ProjectileTrackingSystem` builds target
lookup data from `TargetProxyTag`, `TargetPosition`, and `TargetFaction`, then
runs acquisition and steering jobs. It filters by projectile faction so a
player-faction projectile sees only targets registered to the player-faction
combat root.

Movement is simple data math. `ProjectileMovementSystem` integrates position by
velocity and recomputes world bounds through `ProjectileCollisionMath`.

## Collision And Consequences

`ProjectileCollisionSystem` owns hit qualification and source projectile state.
It may:

- query target proxy data
- build a spatial hash keyed by `TargetFaction`
- perform target bounds and narrow-phase checks
- check and refresh per-projectile contact gates
- decrement pierce count
- disable `Active` (via `CombatDeathUtility.Kill`) when the source projectile is
  consumed
- emit plain data events for damage, impact projectiles, impact AOEs, and VFX

It may not:

- call managed target callbacks
- spawn entities directly
- instantiate GameObjects
- read `TargetCompanion`

Accepted hits can produce:

- `DamageReplayEvent` into `DamageDispatchBridge.DamageQueue`
- `ProjectileSpawnEvent` into projectile expansion for impact projectile bursts
- `AOE variant spawn event` into AOE expansion for impact AOEs
- `VfxPendingSpawn` into the shared VFX scope buffer through a flush job

Damage is finalized by `DamageFinalizeSystem` before spawn expansion. Managed
replay runs later in `DamageDispatchBridge` during `PresentationSystemGroup`.

## Lifetime

Projectile lifetime uses the shared `CombatLifetimeSystem`. The projectile job
ticks `CombatLifetimeComponent.Remaining`, then calls `CombatDeathUtility.Kill`
to disable `Active` and emit expire VFX when time reaches zero.

Projectile collision can also deactivate a projectile immediately when a valid
hit consumes the source.

## Rendering And VFX

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

Gameplay VFX requests are plain ECS/native data until presentation. Projectile
collision and lifetime produce `VfxPendingSpawn`; flush jobs append
`VfxSpawnRequestElement` to the shared scope buffer; `CombatVfxDispatchSystem`
dispatches through `CombatVfxRoot`.

## Current Frame Order

Important simulation ordering:

1. `ProjectileSimulationSystem` starts the projectile phase and exposes active
   counts.
2. `CombatLifetimeSystem` expires projectile and AOE lifetime.
3. `TimedProjectileSpawnSystem` emits child spawn events.
4. `ProjectileTrackingSystem` updates homing data.
5. `ProjectileMovementSystem` moves projectiles and refreshes bounds.
6. `ProjectileContactGateSystem` expires projectile contact gates.
7. `ProjectileCollisionSystem` emits damage, spawn, and VFX events.
8. AOE collision systems run after projectile collision.
9. `DamageFinalizeSystem` freezes the native damage queue.
10. `ProjectileSpawnExpansionSystem` and `AOE spawn expansion systems` drain events
    and produce commands.
11. Projectile and AOE apply systems reuse disabled slots and cold-create
    overflow.
12. `CombatRenderPrepareSystem` prepares render matrices.
13. Presentation systems dispatch damage, VFX, and render batches.

Spawned/reused projectiles do not move, collide, or emit their own timed spawns
until the next simulation update because apply runs after movement/collision.

## Authoring Notes

Projectile authoring flows through skills and spawn requests:

- `ProjectileSpawnRequest` is the managed request passed to `CombatRoot.Spawn`.
- `ProjectileConfig` and runtime skill definitions provide shape, speed,
  lifetime, damage, count, spread, pierce, tracking, impact AOE/projectile, and
  child-spawn data.
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
- one single-threaded Burst apply job per domain reuse pool
- batched render submission

Revisit only with profiling:

- spatial hash cell sizing
- dead-slot scan cost in spawn apply
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
