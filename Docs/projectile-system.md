# Projectile System

All docs in `Docs/` are preliminary. They describe the current port intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

The Unity port should keep the old scoped, data-oriented projectile design.

Projectiles are the data-runtime half of the hybrid architecture. Player, mobs,
and walls remain scene objects; projectile gameplay state does not. The Unity
runtime uses Entities/DOTS for projectile state and simulation, with `ProjectileRoot`
remaining as the scene-object bridge for target snapshots, hit replay, and
render submission.

Old implementation reference:

- `_OldGdProj/Script_Cs/System/Projectile/`
- `_OldGdProj/Script_Cs/Attack/ProjectileAttack.cs`
- `_OldGdProj/Script_Cs/Attack/ProjectileVolleyBuilder.cs`
- `_OldGdProj/Scenes/projectiles/`
- `_OldGdProj/Scenes/attacks/basic_projectile_attack.tscn`

Use the old implementation only for behavior and tuning reference. Do not mirror
its halfway custom-ECS/object-oriented structure in Unity. The Unity version
should use idiomatic Entities data, systems, buffers, and scene bridges.

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
- child projectile or AOE spawn request event if needed

## Frame Flow

During fixed simulation:

1. root syncs live targets from explicit registry
2. root converts target objects into plain target snapshots
3. root submits target snapshots to world
4. world promotes pending spawns
5. world rebuilds broad-phase target data
6. ECS systems run focused stages in order:
   tracking/reacquire, movement, child spawn requests, lifetime expiry disable,
   contact-gate expiry, and collision/pierce hit output
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

Stress tests should cover the old release-build class of scale: about 50k
projectiles on screen with 20 targets at 120 fps. They do not need fixed
pass/fail thresholds before a Unity baseline exists.

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
- `ProjectileMovementSystem`: position integration from velocity and delta time
- `ProjectileChildSpawnSystem`: timed child projectile spawn request events
- `ProjectileLifetimeSystem`: lifetime countdown and disabling expired active state
- `ProjectileContactGateSystem`: repeat-hit gate cooldown expiry
- `ProjectileCollisionSystem`: target mask filtering, shape hit checks, pierce count, contact gate creation, and ordered hit events
- `ProjectileRoot`: scoped Unity bridge, event replay, render submission, and teardown-only destruction
- `ProjectileCollisionMath`: pure narrow-phase shape math only

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

`ProjectileCollisionSystem` performs mask and gate filtering, then delegates
narrow-phase checks to `ProjectileCollisionMath`. It does not read Unity
colliders or call Physics2D.

## Damage

Damage is snapshotted before spawn.

Rules:

- stat, weapon, buff, and attack scaling happen before spawn
- active projectiles only carry damage snapshot data
- target-specific resist/weakness interpretation happens during hit replay
- projectile world forwards damage data but does not understand it
- crit and secondary damage payloads are resolved into the snapshot before spawn

Damage should stay typed, matching the old project. Do not collapse the port to
integer-only damage just because the first content is simple.

## Tracking

Tracking is optional per spawn.

When enabled:

- maintain stable speed while steering
- reacquire targets at configured interval
- filter by range and target set
- allow spawn-query jitter to avoid all projectiles retargeting in same frame

The Unity port should support the old tracking behavior early. Old reference:
`_OldGdProj/Docs/projectile-system.md`, section `Tracking`.

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

Current rendering keeps Unity object access outside ECS simulation. The
late-simulation `ProjectileRenderPrepareSystem` scans active projectile
entities, builds plain render matrices, and writes type-grouped instance data to
the scope render buffer component. `ProjectileRoot` submits those already grouped
instance buffers in `LateUpdate` through `Graphics.RenderMeshInstanced`, avoiding
per-instance managed matrix copies. Render type definitions can point a
projectile type id at a sprite and visual scale.

Keep rendering outside projectile simulation systems.

Do not move Unity object access to worker threads. Jobs/Burst workers operate on
plain data, then merge before callbacks or rendering.

## Future Work

Port in this order:

1. Add impact AOE and stack hit effect adapters on top of child spawn/hit events
2. Add projectile authoring fields for tracking, pierce, child spawns, and type ids
3. Add player-to-mob and mob-to-player smoke tests for the expanded runtime
4. Add projectile stress scene and counters to measure high-scale batches
5. Add broad-phase acceleration only when profiling shows the simple target loop is the bottleneck
6. Keep adding Burst-compatible jobs for hot projectile stages where managed merge steps are not required
7. Add pooled/debug visual adapter only if useful for authoring or low-count cases
