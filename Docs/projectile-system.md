# Projectile System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

Projectiles are the data-runtime half of the hybrid architecture. Player, mobs,
and walls remain scene objects; projectile gameplay state does not. The Unity
runtime uses Entities/DOTS for projectile state and simulation, with `ProjectileRoot`
remaining as the scene-object bridge for target snapshots, hit replay, and
render submission.

Primary uses:

- player projectiles hitting mobs
- mob projectiles hitting player
- authored projectile prefabs with baked collision and render data
- impact AOE spawn on projectile hit
- stack-triggered hit effects
- very high projectile counts from scaled spells and attacks

Non-goal:

- one global projectile manager that owns every projectile, target, and callback in the scene
- using one GameObject and live trigger collider per projectile as the long-term
  high-scale path

## Scoped Roots

Each `ProjectileRoot` owns one attack flow or target set.

Examples:

- `ProjectileRoot_PlayerToMob`: player-fired projectiles targeting mob hurtboxes
- `ProjectileRoot_MobToPlayer`: mob-fired projectiles targeting player hurtboxes

Each root owns:

- one ECS scope entity for target snapshots and hit replay
- one Unity adapter boundary
- projectile template type cache
- target hurtbox shape cache
- listener maps for hit callbacks
- target registry or target query for its configured target set
- render/pool coordination
- profiling counters and stress-test visibility

## Boundary Rule

`ProjectileRoot` may:

- read Unity scene references
- validate live Unity objects
- bake prefab collider and render data
- register projectile templates and targets
- replay hit events into gameplay callbacks
- update pooled visuals or instanced rendering

Projectile ECS systems must not:

- touch GameObjects, Transforms, Components, or Colliders
- instantiate prefabs
- call Physics2D
- apply gameplay damage directly
- read live player, weapon, buff, or mob state

It may read only snapshot data produced by the scene-object side.

Input into world:

- projectile spawn command
- target snapshot
- projectile and target type definitions
- immutable damage snapshot

Output from world:

- projectile spawned event
- projectile despawned event
- target despawned event
- hit event
- raw hit payload data carried through projectile hit events

## Frame Flow

During fixed simulation:

1. root syncs live targets from explicit registry
2. root converts target objects into plain target snapshots
3. root submits target snapshots to world
4. root-submitted and ECS-submitted spawn requests are materialized by
   `ProjectileSpawnSystem`, reusing inactive entities before cold creation
5. world rebuilds broad-phase target data
6. ECS systems run focused stages in order:
   spawn materialization, tracking/reacquire, movement, child projectile request
   creation, lifetime expiry disable, contact-gate expiry, and
   collision/pierce hit output
7. root drains world events
8. root handles adapter side effects such as visual slots and registry cleanup

During normal update:

1. root replays hit events through a hit context
2. gameplay listeners apply damage or secondary effects
3. root updates visuals

Hit reactions stay outside the pure simulation step.

## Performance Baseline

Projectile runtime is a core scaling system.

The first projectile proof of concept should include the high-scale path:
Entities/DOTS projectile state, batched rendering by projectile type, and
Jobs/Burst for high-volume simulation work. Avoid designing around Unity trigger
callbacks or one scene node per projectile.

Required practices:

- register projectile prefab types once
- bake collider shape and visual data once
- use batched rendering for the projectile POC; pooled visual objects are only a
  low-count fallback
- keep spawn commands compact
- avoid per-projectile allocations
- avoid Transform access inside collision loops
- track active count, spawn count, deactivate/despawn count, simulation time,
  render time, and allocation spikes

Scene-object bridge:

- player and mobs expose hurtboxes through registries
- roots snapshot those hurtboxes once per simulation step
- projectile hits replay back into actor components after world step
- built-in Physics2D still handles player/mob/wall body collision separately

Stress tests should target about 50k projectiles on screen with 20 targets at
120 fps. They do not need fixed pass/fail thresholds before a baseline exists.

## Data Layout

Keep projectile runtime data plain and compact.

Projectile state should include:

- ECS entity
- projectile type id
- target mask or target set id
- immutable damage snapshot reference
- position and velocity
- lifetime
- tracking config and tracked target handle/index
- render slot or pooled visual id
- cached bounds
- hit state
- pierce/contact gate state
- enableable active state

Target state should include:

- entity handle
- target type id
- collision layer or target set id
- position
- cached bounds
- baked shape type, radius, half extents, and rotation

## ECS Stage Ownership

Keep projectile simulation split by responsibility. `ProjectileSimulationSystem`
is only the start-of-frame scope/event-buffer coordinator. Feature work should
land in the narrow system that owns that behavior:

- `ProjectileTrackingSystem`: optional homing, target refresh, reacquire interval, and steering while preserving speed
- `ProjectileSpawnSystem`: scoped spawn request materialization, inactive
  projectile reuse by scope/render type/layout, and cold entity creation through
  ECB when the pool is empty
- `ProjectileMovementSystem`: position integration from velocity and delta time
- `ProjectileChildSpawnSystem`: timed child spawn request creation; on each
  interval tick, appends `ChildCountPerTick` spawn requests to the owning scope
  through `EntityCommandBuffer.ParallelWriter`, fully initialized from
  `ProjectileChildSpawnerComponent` (shape, raw hit payload, tracking, spawn pattern)
- `ProjectileLifetimeSystem`: lifetime countdown and disabling expired active state
- `ProjectileContactGateSystem`: repeat-hit gate cooldown expiry
- `ProjectileCollisionSystem`: spatial-hash broad phase, target AABB filtering, target mask filtering, shape hit checks, pierce count, contact gate creation, and ordered hit events
- `ProjectileRoot`: scoped Unity bridge, target/projectile bounds setup, event replay, render submission, and teardown-only destruction
- `ProjectileCollisionMath`: pure bounds and narrow-phase shape math

Do not merge these stages back into one large projectile system. Shared data
belongs in `ProjectileEcsComponents`; small cross-stage constants or ordering
helpers belong in tiny helper files.

## Collision

Bake supported 2D shapes from prefab data from the first projectile POC:

- circle
- rectangle/box
- capsule

Current Unity target snapshots bake collider-derived shape data through
`ProjectileTargetShapeUtility`. Circle, box, and capsule hurtboxes are converted
to plain ECS target elements before simulation. Projectile spawn commands also
carry shape type, radius, half extents, and rotation.

Broad phase target options:

- simple list for low counts
- spatial hash for many targets
- AABB tree only if later profiling justifies it

Narrow phase should cover:

- circle-circle
- circle-rectangle
- circle-capsule
- rectangle-rectangle
- rectangle-capsule
- capsule-capsule

`ProjectileCollisionSystem` builds a per-scope spatial hash from target bounds,
rejects projectiles that overlap no occupied target cells, applies target AABB
checks, then delegates narrow-phase checks to `ProjectileCollisionMath`. It does
not read Unity colliders or call Physics2D.

## Damage

Damage is snapshotted before spawn.

Rules:

- stat, weapon, buff, and attack scaling happen before spawn
- active projectiles only carry damage snapshot data
- target-specific resist/weakness interpretation happens during hit replay
- projectile world forwards damage data but does not understand it
- crit and secondary damage payloads are resolved into the snapshot before spawn

Damage should stay typed. Do not collapse to integer-only damage just because the
first content is simple.

## Tracking

Tracking is optional per spawn.

When enabled:

- maintain stable speed while steering
- reacquire targets at configured interval
- filter by range and target set
- allow spawn-query jitter to avoid all projectiles retargeting in same frame

## Pierce And Contact Gates

Piercing projectiles should be able to hit the same target again after an
authored repeat-hit cooldown. This differs from a strict one-hit-per-projectile
model and should be represented in the contact gate state.

Current implementation stores contact gates as per-projectile ECS buffers.
`ProjectileCollisionSystem` creates or refreshes gates when a hit happens, and
`ProjectileContactGateSystem` expires them before the next collision stage.

## Environment

Projectiles currently do not interact with walls. Keep environment collision out
of the first projectile path, but leave the design configurable enough to add
wall behavior later.

## Rendering

Start with a batched renderer by projectile type. Pooled prefab visuals may
exist as a low-count fallback or debug path, but they are not enough for the
projectile proof of concept and must not be the default path.

Current rendering keeps Unity object access outside ECS simulation. Projectile
prefabs remain the authoring source for `SpriteRenderer`, material, and collider
setup, and `ProjectileRoot` bakes those prefab values into runtime render
resources and ECS metadata. Each projectile entity carries a structural
render-type tag component and a `ProjectileRenderElement` component holding one
`Matrix4x4 objectToWorld`. A `ProjectileRenderScope` shared component (holding
the owning root's scope entity) partitions projectile chunks by root at the ECS
chunk level.

The late-simulation `ProjectileRenderPrepareSystem` queries each render type
independently and writes the `objectToWorld` matrix directly into
`ProjectileRenderElement` on each active projectile entity. Render queries
include the enableable active tag so disabled projectiles are skipped. The job
is Burst-compiled and scheduled in parallel per render type, then completed
before `LateUpdate`.

`ProjectileRoot.SubmitProjectiles()` iterates each registered render type,
applies a `ProjectileRenderScope` shared-component filter so only that root's
chunks are visible, then calls `query.ToComponentDataArray<ProjectileRenderElement>`
to collect active-entity matrices. The result is copied into a pre-allocated
1023-element submit buffer and submitted via `Graphics.RenderMeshInstanced`,
split into chunks of at most 1023 instances. No separate batch entities,
dynamic buffers, or trim jobs are required.

Keep rendering outside projectile simulation systems.

Do not move Unity object access to worker threads. Jobs/Burst workers operate on
plain data, then merge before callbacks or rendering.

## Future Work

Port in this order:

1. Add impact AOE and stack hit effect data on top of raw projectile hit payloads
2. Add projectile authoring fields for tracking, pierce, lifetime, speed, child spawns, and type ids, including separate child-specific overrides for lifetime, speed, pierce count, and tracking parameters
3. Add player-to-mob and mob-to-player smoke tests for the expanded runtime
4. Add projectile stress scene and counters to measure high-scale batches
5. Tune broad-phase cell sizing and add an AABB tree only if profiling shows the current spatial-hash path is the bottleneck
6. Keep adding Burst-compatible jobs for hot projectile stages where managed merge steps are not required
7. Add pooled/debug visual adapter only if useful for authoring or low-count cases

## Implementation status (as of 2026-05-30)

This section documents how the current Unity implementation aligns with this design doc and notes small, actionable differences found in the codebase.

- **Core match:** The overall scoped, data-oriented design is implemented. `ProjectileRoot` owns a scope entity, target registry, template/type maps, and runtime counters (see `Assets/Scripts/System/Projectile/ProjectileRoot.cs`).
- **Boundary rule:** The implementation follows the bridge pattern: the root reads scene objects and snapshots targets, ECS systems run on plain data and do not touch GameObjects or call `Physics2D` (see `ProjectileRoot.cs` and the simulation systems under `Assets/Scripts/System/Projectile/`).
- **Frame flow & systems:** Systems implement the staged pipeline described in this doc: `ProjectileSimulationSystem`, `ProjectileSpawnSystem`, `ProjectileTrackingSystem`, `ProjectileMovementSystem`, `ProjectileChildSpawnSystem`, `ProjectileLifetimeSystem`, `ProjectileContactGateSystem`, `ProjectileCollisionSystem`, and `ProjectileRenderPrepareSystem` (see the corresponding source files in `Assets/Scripts/System/Projectile/`).
- **Data layout & events:** ECS components and buffer elements (`ProjectileComponent`, `ProjectileSpawnRequestElement`, `ProjectileTargetElement`, hit/render buffers) match the documented layout. Hits are written into the scope hit buffer and replayed by `ProjectileRoot` via `ProjectileHit`. Hit payloads are raw unmanaged data copied through ECS and dispatched to source/target scene actors (`Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs`, `ProjectileRuntimeEvents.cs`, `ProjectileSpawnCommand.cs`).
- **Collision shapes & math:** Circle, rectangle (box), and capsule shapes are supported. Projectile and target AABB bounds are cached in ECS data, spatial-hash broad phase runs in `ProjectileCollisionSystem.cs`, and bounds/narrow-phase math is implemented in `ProjectileCollisionMath.cs`.
- **Pierce & contact gates:** Contact gates are per-projectile buffer elements and are added/refreshed by the collision system and expired by `ProjectileContactGateSystem`.
- **Tracking & steering:** Full tracking support exists with query intervals, reacquire logic, and steering that preserves projectile speed (`ProjectileTrackingSystem.cs`). Child projectiles inherit tracking config stored in `ProjectileChildSpawnerComponent`.
- **Rendering:** Batched instanced rendering is implemented: `ProjectileRenderPrepareSystem` writes matrices into per-scope/per-type render batch buffers and `ProjectileRoot` submits via `Graphics.RenderMeshInstanced` using built `ProjectileRenderResources`.

### Minor differences / implementation notes

- **Prefab "baking":** The doc describes baking prefab collider and render data. The implementation performs template registration and builds render resources at runtime via `RegisterTemplate` / `BuildRenderResources` inside `ProjectileRoot` rather than a separate offline/bake pipeline. This achieves the intent but is runtime-driven.
- **Damage snapshot shape:** The runtime carries a `DamageSnapshot` value recorded on spawn; current buffer fields pass a float `DamageAmount`. If you intended a richer typed snapshot, inspect `PlayGround.Common.DamageSnapshot` and extend the buffer payloads accordingly.
- **Broad-phase acceleration:** The implementation uses per-scope target buffers plus a fixed-size spatial hash over target AABBs. An AABB tree is not present; add it only if profiling shows the hash plus bounds filter is insufficient.
- **Impact AOE / hit effects:** AOE and complex hit reactions should be added as explicit payload data, then interpreted by scene actor hit handlers after `ProjectileRoot` drains hit events. The collision and child spawn systems stay free of managed callbacks.
- **Child spawn / request materialization:** Child projectiles are requested by `ProjectileChildSpawnSystem` via `EntityCommandBuffer.ParallelWriter.AppendToBuffer` on the owning scope. `ProjectileSpawnSystem` materializes those requests on the next simulation pass, reusing inactive child entities before cold creation. Children copy the parent's source node id and use child-specific damage/direct-damage payload data; no managed child-spawn ownership event is emitted.
- **Child spawn pattern:** `ProjectileChildSpawnBehavior` (attached to `ProjectileChildSpawnConfig`) holds `Count`, `PatternType` (`ProjectileChildSpawnPatternType`: `SideSpray` or `Forward`), and `SpreadDegrees`. `SideSpray` fans `(count+1)/2` shots left and `count/2` shots right, each side spread evenly across `+/-SpreadDegrees/2` around the perpendicular, matching the behavior of `ProjectileSideSpraySpawnPattern`. These values are copied into `ProjectileChildSpawnerComponent` at spawn time and consumed entirely inside the Burst job; adding a new pattern requires only a new enum case and a velocity branch in `ComputeChildVelocity`.
- **Attack authoring - `ProjectileConfig`:** `ProjectileAttack` no longer holds inline serialized projectile simulation fields. All projectile data (prefab, speed, lifetime, damage, count, spread, tracking, pierce, impact AOE, etc.) lives in a `ProjectileConfig` ScriptableObject assigned via the Inspector. `ProjectileAttack` retains only behavior fields: `projectileRoot`, `recoverySeconds`, `performSound`, and `audioManager`. `ProjectileConfig` also exposes `GetTrackingConfig()` so the SO is the single authoring source for a projectile type.
- **Child-specific authoring:** `ProjectileChildSpawnConfig` carries full child projectile definition (shape, damage, speed, lifetime, pierce, visual) plus `ProjectileTrackingConfig` and `ProjectileChildSpawnBehavior`. `ChildSpawningProjectileAttack` holds a serialized reference to a `ProjectileAttack` (`parentAttack`) and a `ProjectileConfig` (`childConfig`) SO; no child `ProjectileAttack` component or child GameObject needed. The built `ProjectileChildSpawnConfig` is owned by `ChildSpawningProjectileAttack` and passed per-call via `parentAttack.TryFire(aimDirection, childConfig)`; `ProjectileAttack` has no `activeChildConfig` state or `SetChildConfig` method. Prefab structure: root GameObject carries `ChildSpawningProjectileAttack`; a child GameObject "ParentProjectile" carries `ProjectileAttack` with its own `ProjectileConfig`. No execution-order attribute or runtime deactivation required.
- **Stress tests:** Runtime counters are present (`ProjectileRoot.Counters`) but no dedicated stress-test scene was found in this directory; adding a stress scene and counters visualization remains a future task.

### Files referenced while verifying

- `Assets/Scripts/System/Projectile/ProjectileRoot.cs`
- `Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Projectile/ProjectileSimulationSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileChildSpawnSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileLifetimeSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileContactGateSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionMath.cs`
- `Assets/Scripts/System/Projectile/ProjectileRenderPrepareSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileTargetShapeUtility.cs`
