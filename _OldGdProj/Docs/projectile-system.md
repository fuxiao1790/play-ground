# Projectile System Current State

## Summary

The active projectile runtime lives in [`scripts_cs/System/Projectile`](../scripts_cs/System/Projectile).

It is a scoped, data-oriented runtime for projectile attack flows. It is not a
single global projectile manager for every interaction in the game.

Primary uses:

- player projectiles hitting mobs
- mob projectiles hitting the player
- attack-authored projectile scenes using baked collision and render data

Non-goal:

- one world-wide projectile authority that owns every projectile, target, and
  callback in the scene tree

There is also an empty [`scripts_cs/Projectile`](../scripts_cs/Projectile)
folder. The active implementation is the `System/Projectile` package.

## Scoped Roots

Each [`Root.cs`](../scripts_cs/System/Projectile/Root.cs) instance is a scoped
projectile runtime for one attack flow or target set.

Examples:

- one root manages player-fired projectiles targeting the `mob` group
- another root manages mob-fired projectiles targeting the `player` group

Each root owns:

- one pure [`ProjectileWorld`](../scripts_cs/System/Projectile/ProjectileWorld.cs)
- one Godot-facing adapter boundary
- one renderer child named `MultiMeshInstance2D`
- type caches for projectile templates and target hurtbox shapes
- listener maps for hit callbacks
- target discovery for its configured `target_group`

The root discovers only the configured target group. It should not silently own
every projectile and every target in the scene.

## Main Pieces

- [`Root.cs`](../scripts_cs/System/Projectile/Root.cs): Godot adapter, target
  sync, scene baking, listener maps, event replay, and renderer coordination
- [`ProjectileWorld.cs`](../scripts_cs/System/Projectile/ProjectileWorld.cs):
  pure SoA runtime state, spawn promotion, target snapshots, range-based
  movement/tracking/collision stages, despawn queues, and event buffers
- [`Boundary.cs`](../scripts_cs/System/Projectile/Boundary.cs): spawn commands,
  target snapshots, damage snapshots, damage components, and runtime events
- [`Data.cs`](../scripts_cs/System/Projectile/Data.cs): simple SoA projectile
  and target stores used by the hot loops
- [`Movement/Movement.cs`](../scripts_cs/System/Projectile/Movement/Movement.cs):
  position and lifetime update
- [`Movement/Tracking.cs`](../scripts_cs/System/Projectile/Movement/Tracking.cs):
  optional target acquisition and steering
- [`Collision/Collision.cs`](../scripts_cs/System/Projectile/Collision/Collision.cs):
  broad-phase filters and shape-level hit detection
- [`Collision/HitShape.cs`](../scripts_cs/System/Projectile/Collision/HitShape.cs):
  scene collision baking, cached local bounds, and narrow-phase shape math
- [`Collision/EntityRegistry.cs`](../scripts_cs/System/Projectile/Collision/EntityRegistry.cs):
  handle-to-callback bridge for adapter replay
- [`Collision/ProjectileHitContext.cs`](../scripts_cs/System/Projectile/Collision/ProjectileHitContext.cs):
  gameplay hit callback payload
- [`Render/Renderer.cs`](../scripts_cs/System/Projectile/Render/Renderer.cs):
  per-type `MultiMesh` rendering batches
- [`Render/RenderDefinition.cs`](../scripts_cs/System/Projectile/Render/RenderDefinition.cs):
  render data baked from projectile scenes

## Runtime Boundary

`Root` is the Godot boundary adapter. It may:

- read scene groups with `GetTree()`
- validate live Godot nodes
- bake projectile scenes and hurtbox shapes into runtime definitions
- register source, hit listener, and target handles
- replay world events into gameplay callbacks
- update Godot rendering through `Renderer`

`ProjectileWorld` is the runtime core. It should stay plain-data oriented. It
should not:

- call `GetTree()`
- touch `Node` or other live scene objects
- call `GodotObject.IsInstanceValid(...)`
- bake `PackedScene` data
- call `RenderingServer`
- recalculate damage from live player, weapon, buff, or mob state

Input crosses into the world as:

- `ProjectileSpawnCommand`
- `TargetSnapshot`
- projectile and target type definitions

Output crosses back to the adapter as:

- `ProjectileSpawnedEvent`
- `ProjectileDespawnedEvent`
- `TargetDespawnedEvent`
- `HitEvent`
- `ChildProjectileSpawnRequestedEvent`

## Frame Flow

During `_PhysicsProcess()`:

1. `Root` syncs live targets from its configured `target_group`.
2. `Root` converts those live nodes into `TargetSnapshot` values.
3. `Root` submits the snapshots to `ProjectileWorld`.
4. `ProjectileWorld.Step(...)` flushes queued despawns.
5. Pending spawns are promoted from the pending `ProjectileStore` into the
   active `ProjectileStore`.
6. Target broad-phase and narrow-phase structures are rebuilt from the latest
   target snapshot.
7. Active projectile ranges run tracking, movement, expiry marking, collision,
   hit resolution, and child-spawn request generation.
8. The world queues plain runtime events in projectile-index order.
9. `Root` drains those events and handles adapter side effects such as renderer
   slot assignment, child projectile spawning, and registry cleanup.

During `_Process()`:

1. `Root` replays hit events through `ProjectileHitContext`.
2. Source and target callbacks apply gameplay effects such as damage.
3. `Root` syncs active projectile ranges into the `MultiMesh` renderer.

Hit reactions are deferred out of the physics simulation. This keeps gameplay
callbacks and Godot node mutation on the adapter side.

## Data Layout

`ProjectileStore` stores hot-loop projectile state in simple parallel arrays:

- entity handle
- projectile type id
- target mask
- immutable damage snapshot reference
- position and velocity
- lifetime
- tracking config and tracked target handle/index
- render slot, cached render rotation, and cached render velocity
- cached world bounds
- hit state and hit target index
- despawn, pierce, and child-spawn state

`TargetStore` stores target snapshot state in simple parallel arrays:

- entity handle
- target type id
- collision layer
- position
- cached world bounds

`ProjectileSnapshot` is used when adapter code needs a stable copy after SoA
rows may compact or move. Projectile and target stores do not store live nodes.

Stores intentionally use simple arrays instead of nested stream wrapper types.
The column groups are kept visible in `Data.cs` by comments:

- identity/type/damage
- transform
- tracking
- collision
- render
- child spawn

Simulation systems process `[startIndex, endIndex)` ranges. `endIndex` is
exclusive. When projectile threading is enabled on the root and the active
count meets the configured threshold, `ProjectileWorld` splits those ranges
into worker chunks.

## System Ranges And Parallelism

The current implementation can start worker threads for pure runtime work when
the root's `projectile_threading_enabled` flag is set. The default stays off so
the serial path remains the baseline.

- `Tracking.Apply(projectiles, targets, startIndex, endIndex, delta)` reads
  target arrays plus projectile position, velocity, and target mask; it writes
  projectile velocity and tracking columns by projectile range.
- `Movement.Apply(projectiles, startIndex, endIndex, delta)` reads velocity and
  writes position, lifetime, and the existing world-bounds position by
  projectile range.
- `MarkExpiredProjectilesRange(...)` writes `PendingDespawn` by projectile range
  and merges the despawn flag on the world thread.
- `Collision.UpdateProjectileBounds(...)` updates projectile world bounds by
  projectile range.
- `Collision.ReleaseExitedContactGates(...)` stays serial and mutates
  contact-gate release state.
- `Collision.Apply(...)` reads projectile/target columns and contact gates, then
  writes only `Hit` and `HitTargetIndex` by projectile range.
- `ProjectileWorld` collects hit, pierce, despawn, contact-gate, and child-spawn
  results into range-local buffers, then merges those buffers by ascending range
  start index to preserve serial event order.
- `Renderer.Apply(...)` runs on the adapter side. If
  `projectile_render_threading_enabled` is set, workers compute plain transform
  commands by range and the main thread applies those commands to batch buffers
  before `RenderingServer` upload.

Godot object access, renderer server upload, scene lookup, and gameplay
callbacks stay outside simulation systems, so they are not threading candidates.

Root-level threading controls are:

- `projectile_threading_enabled`
- `minimum_parallel_projectile_count`
- `projectile_parallel_chunk_size`
- `projectile_render_threading_enabled`
- `minimum_parallel_render_count`
- `projectile_render_parallel_chunk_size`

The root also exposes profiling counters for the most recent projectile step
and render-preparation pass, including whether threading was used, elapsed
microseconds, worker count, and chunk count.

### What Runs On Workers

When `projectile_threading_enabled` is checked and active projectile count is at
least `minimum_parallel_projectile_count`, these `ProjectileWorld` stages run
over worker ranges:

- tracking: target refresh/acquisition and steering velocity writes
- movement: position, lifetime, and world-bounds position writes
- lifetime expiry marking: `PendingDespawn` writes plus a merged despawn flag
- collision query: broad-phase filter checks and narrow-phase shape checks,
  writing only `Hit` and `HitTargetIndex`
- event collection: range-local hit events, pierce/despawn state, contact-gate
  requests, and child-spawn requests

The same flag does not move scene access to workers. These pieces stay serial on
the world/main thread:

- pending spawn promotion and despawn compaction
- target snapshot submission and collision target structure builds
- contact-gate release cleanup before collision queries
- merge of range-local events into world event lists
- mutation of the shared `_contactGates` map

When `projectile_render_threading_enabled` is checked and active projectile
count is at least `minimum_parallel_render_count`, workers compute CPU-side
render transform commands from projectile position, velocity, render slot, and
baked render definitions.

Render workers do not touch Godot render objects. These pieces stay on the main
thread:

- applying prepared transform commands to renderer batch buffers
- `RenderingServer.MultimeshSetBuffer(...)`
- `MultiMesh.InstanceCount` and `VisibleInstanceCount`
- `MultiMeshInstance2D` node creation and visibility rect updates
- hit callback replay and gameplay damage
- scene tree group reads, target node validation, and scene/resource baking

## Registration And Baking

Projectile type registration starts from a scene template and collision-shape
path. `Root.RegisterProjectileType(...)` caches by template resource path or
instance id plus the collision path.

Registration bakes two separate definitions:

- simulation definition in `ProjectileWorld`, using `HitShape`
- render definition in `Renderer`, using texture, local origin, local rotation,
  local scale, modulation, and cached local rotation sine/cosine

Target type registration is adapter-owned. `Root` reads each target node's
`Hurtbox/HurtboxShape`, caches by shape rid plus local position and rotation,
and registers the baked shape with `ProjectileWorld`.

This keeps scene inspection out of hot spawn and frame paths after the type is
cached.

## Collision

Collision uses baked `HitShape` definitions for projectiles and targets.

Supported shape kinds:

- circle
- rectangle
- capsule

Broad-phase filters currently include:

- `Collision.AABBTreeFilter`
- `Collision.SpatialHashFilter`

The root currently creates the world with `SpatialHashFilter`.

Narrow-phase checks cover:

- circle-circle
- circle-rectangle
- circle-capsule
- rectangle-rectangle
- rectangle-capsule
- capsule-capsule

Projectile target filtering also applies `targetMask` against each target's
collision layer.

Current attack-layer mapping:

- player-fired projectiles target mob hitboxes on layer `12` (`2048`)
- mob-fired projectiles target player hitboxes on layer `2` (`2`)
- projectile visual scenes are tagged on layer `3` for player shots and layer
  `13` for mob shots for scene clarity, but runtime hit detection uses baked
  shape data instead of live `Area2D` overlap

Piercing is authored on attack scenes with `projectile_pierce_count`. The value
means extra hits after the first hit, so `0` keeps the default one-hit despawn
behavior and `1` allows two total hit events. A piercing projectile gates a
target after hitting it while the projectile remains inside that hurtbox. Once
the projectile fully exits that target hitbox, the same target can be hit again
and that new hit consumes another pierce count.

## Damage

Damage is snapshotted before spawn.

`DamageSnapshot` carries one or more `DamageComponent` values. Each
component has:

- amount
- `DamageTypeRef`

The projectile world only stores and forwards the snapshot. It does not
calculate stat scaling and it does not interpret target resistances or weakness.

Current gameplay callers use `DamageSnapshot.Single(...)` and apply
the hit with `DamageableState.ApplyDamage(...)` during callback replay. The
snapshot shape is ready for richer typed damage later.

Important rule:

- do stat, weapon, buff, and attack scaling before `SpawnProjectile(...)`
- do target-specific interpretation during hit replay
- do not make active projectiles recalculate damage from live stat objects

## Tracking

Tracking is optional per spawn through `ProjectileTrackingConfig`.

When enabled, the runtime:

- keeps speed stable while steering velocity
- reacquires targets at the configured query interval
- can jitter the first query through root-level spawn jitter
- filters targets by range and `targetMask`
- avoids always-on timing instrumentation unless
  `Root.SetTrackingTimingEnabled(true)` is called

## Rendering

Rendering is owned by [`Renderer.cs`](../scripts_cs/System/Projectile/Render/Renderer.cs).

The renderer uses one `MultiMesh` batch per projectile type:

- the first batch uses the renderer node itself
- later projectile types get sibling `MultiMeshInstance2D` batch nodes
- slots are allocated on spawn and freed on despawn
- inactive slots are hidden offscreen with transparent color
- upload size follows the highest live slot, not the total maximum capacity
- uploads use reusable power-of-two buffers
- a fixed visibility rect is enabled only while the batch has live instances
- render rotation is recomputed only when projectile velocity changes
- optional threaded render preparation computes CPU transform commands without
  touching `RenderingServer`, `MultiMesh`, or scene nodes from workers

Known remaining render cost:

- moving many visible projectiles still requires main-thread batch-buffer writes,
  `Array.Copy` into the upload buffer, and `RenderingServer.MultimeshSetBuffer`

If profiles still show rendering as the main cost, investigate render-slot
compaction, smaller dirty windows, or another upload strategy.

## Recent Profiling Notes

Recent threaded projectile traces showed these safe optimizations helped without
changing runtime contracts:

- compacting projectile despawns by survivor ranges instead of copying one SoA
  row at a time
- skipping contact-gate dictionary lookups when no piercing contact gates are
  active
- using packed integer spatial-hash cell keys instead of struct keys that call
  `HashCode.Combine(...)`
- moving projectile world-bounds updates into worker ranges, while keeping
  contact-gate release serial on the world thread

Do not reapply the sparse active-gate cleanup experiment that walked only
`_contactGates` entries. It regressed real gameplay performance even though it
looked attractive in isolation.

Follow-up traces after restoring the safe changes:

- [`godot-game-20260515-162200.speedscope.json`](../.profiles/godot-game-20260515-162200.speedscope.json)
  captured the bad sparse-gate experiment. `Collision.ReleaseExitedContactGates`
  spiked badly and is the reason that experiment stayed reverted.
- [`godot-game-20260515-162520.speedscope.json`](../.profiles/godot-game-20260515-162520.speedscope.json)
  showed the safe path back near the prior behavior, with `ProjectileWorld.Step`
  around the same per-sample range as the earlier good traces.
- [`godot-game-20260515-163036.speedscope.json`](../.profiles/godot-game-20260515-163036.speedscope.json)
  shows heavier load: worker collision/tracking dominate total CPU, while
  `Collision.ReleaseExitedContactGates` remains the clearest serial simulation
  cost. Any next attempt should avoid per-gate full projectile scans and should
  be validated against real gameplay traces before keeping it.

## Current Integration

Player projectile firing:

- [`scripts_cs/Player/PlayerAttack.cs`](../scripts_cs/Player/PlayerAttack.cs)
  equips projectile and AOE attack scenes as player children and performs every
  ready attack while `primary_attack` is held
- [`scripts_cs/Attack/ProjectileAttack.cs`](../scripts_cs/Attack/ProjectileAttack.cs) registers a
  projectile template, builds volley spawn requests, submits
  `SpawnProjectile(...)`, and applies damage on hit
- projectile attacks can also author an `impact_aoe_effect_scene`; on each
  projectile hit, including pierce hits, the attack spawns an AOE at the hit
  event position and can skip direct projectile damage with
  `projectile_direct_damage_enabled = false`
- projectile attacks can own child `ProjectileHitEffect` components; the
  current stack explosion effect adds enum-indexed status stacks through the
  mob root on valid mob hits, then spawns an AOE through `AoeRoot` when its
  threshold is reached

AOE damage:

- [`scripts_cs/System/Aoe/AoeRoot.cs`](../scripts_cs/System/Aoe/AoeRoot.cs)
  mirrors projectile roots with scoped target groups such as `mob` and `player`
- [`scripts_cs/System/Aoe/AoeWorld.cs`](../scripts_cs/System/Aoe/AoeWorld.cs)
  keeps collision in plain runtime data using baked hit shapes and a spatial
  hash over target bounds
- pulse AOEs hit all overlapping valid targets once, while lingering AOEs hit
  immediately on target entry, repeat per target after the authored tick
  interval, and hit immediately after full exit and re-entry
- [`scripts_cs/Attack/AoeAttack.cs`](../scripts_cs/Attack/AoeAttack.cs)
  provides direct authored AOE attacks through the player attack loadout
- [`scenes/attacks/basic_aoe_attack.tscn`](../scenes/attacks/basic_aoe_attack.tscn)
  is a benchmark-style AOE attack comparable to the basic projectile attack:
  it spawns 100 short-lived lingering AOEs at the aim point using
  [`basic_aoe_effect.tscn`](../scenes/attacks/basic_aoe_effect.tscn)

Mob projectile firing:

- [`scripts_cs/Mob/MobProjectileAttack.cs`](../scripts_cs/Mob/MobProjectileAttack.cs)
  registers a projectile template, submits shots toward the current target, and
  applies damage on hit

Attack scene firing:

- [`scripts_cs/Attack/ProjectileAttack.cs`](../scripts_cs/Attack/ProjectileAttack.cs) uses the same
  runtime path for direct scene performs and player-equipped attacks

The shared design is reusable. The actual root instances stay scoped.

## Verification

Recent SoA rewrite verification used:

- `dotnet build`
- Godot headless C# build
- [`tests/projectile_rendering_multimesh_smoke.gd`](../tests/projectile_rendering_multimesh_smoke.gd)
- [`tests/projectile_root_loop_combine_smoke.gd`](../tests/projectile_root_loop_combine_smoke.gd)
- [`tests/projectile_main_scene_player_to_mob_smoke.gd`](../tests/projectile_main_scene_player_to_mob_smoke.gd)
- [`tests/projectile_tracking_smoke.gd`](../tests/projectile_tracking_smoke.gd)
- [`tests/projectile_piercing_smoke.gd`](../tests/projectile_piercing_smoke.gd)
- [`tests/travel_spawn_projectile_attack_smoke.gd`](../tests/travel_spawn_projectile_attack_smoke.gd)

Additional projectile-focused coverage exists in:

- [`tests/player_tracking_attack_smoke.gd`](../tests/player_tracking_attack_smoke.gd)
- [`tests/stacking_projectile_aoe_attack_smoke.gd`](../tests/stacking_projectile_aoe_attack_smoke.gd)

Some older smoke tests still preload removed `scripts_cs/Projectile/*` classes
instead of the active `scripts_cs/System/Projectile` runtime. Those tests need a
separate migration before they can verify the current package:

- [`tests/projectile_packed_fast_path_smoke.gd`](../tests/projectile_packed_fast_path_smoke.gd)
- [`tests/projectile_stress_smoke.gd`](../tests/projectile_stress_smoke.gd)
- [`tests/projectile_target_math_smoke.gd`](../tests/projectile_target_math_smoke.gd)
- [`tests/projectile_bounds_collision_skip_smoke.gd`](../tests/projectile_bounds_collision_skip_smoke.gd)

## Future Work

Keep future changes aligned with the scoped-root model:

1. Keep roots local to one attack flow or target set.
2. Keep Godot object access, callbacks, and rendering-server calls in `Root` or
   `Renderer`.
3. Keep `ProjectileWorld` plain-data oriented.
4. Add pure runtime tests around movement, expiry, tracking, collision, event
   order, and damage snapshot forwarding.
5. Add adapter tests around target group churn, stale-node cleanup, target type
   caching, and hit replay.
6. Keep threaded work limited to plain runtime data and CPU-side render
   preparation; merge results before gameplay callbacks or render upload.
7. Keep render batch tests separate from simulation tests.
