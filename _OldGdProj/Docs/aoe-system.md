# AOE System Current State

## Summary

The active area-of-effect runtime lives in
[`scripts_cs/System/Aoe`](../scripts_cs/System/Aoe).

It is a scoped, data-oriented runtime for AOE damage flows. It follows the
same ownership idea as the projectile runtime: scene-facing roots own Godot
interop and a plain runtime core owns collision and hit timing.

Primary uses:

- player AOEs hitting mobs
- mob AOEs hitting the player
- projectile impact explosions that spawn an AOE on projectile collision
- authored AOE attack scenes using baked collision and optional effect visuals

Non-goal:

- one global damage authority that discovers every target and effect in the
  scene tree

## Scoped Roots

Each [`AoeRoot.cs`](../scripts_cs/System/Aoe/AoeRoot.cs) instance owns one
target group.

Current main-scene roots:

- `AoeManager`: player AOEs targeting the `mob` group
- `MobAoeManager`: mob AOEs targeting the `player` group

Each root owns:

- one pure [`AoeWorld`](../scripts_cs/System/Aoe/AoeWorld.cs)
- one Godot adapter boundary
- AOE effect scene baking
- target hurtbox shape baking
- target discovery for its configured `target_group`
- listener maps for gameplay hit callbacks
- optional effect scene instances and despawn cleanup

The root discovers only its configured target group. Player AOE and mob AOE
flows stay separate for the same reason player projectiles and mob projectiles
stay separate.

## Main Pieces

- [`AoeRoot.cs`](../scripts_cs/System/Aoe/AoeRoot.cs): Godot adapter, target
  sync, AOE type registration, spawn API, event replay, and clear/reset API
- [`AoeWorld.cs`](../scripts_cs/System/Aoe/AoeWorld.cs): plain runtime state,
  pending spawn promotion, target snapshots, spatial-hash target queries,
  pulse hits, lingering tick intervals, exit/re-entry gates, despawns, and hit
  event buffers
- [`Boundary.cs`](../scripts_cs/System/Aoe/Boundary.cs): spawn requests,
  commands, hit context, hit events, and despawn events
- [`AoeTypeRegistry.cs`](../scripts_cs/System/Aoe/AoeTypeRegistry.cs):
  adapter-side AOE and target type caches backed by baked `HitShape` values
- [`AoeTargetSync.cs`](../scripts_cs/System/Aoe/AoeTargetSync.cs): live Godot
  target group sync into plain target snapshots
- [`AoeSpawnAdapter.cs`](../scripts_cs/System/Aoe/AoeSpawnAdapter.cs): public
  spawn request validation, effect visual instantiation, hit listener storage,
  event draining, and gameplay callback replay
- [`scripts_cs/Common/DamageSnapshot.cs`](../scripts_cs/Common/DamageSnapshot.cs):
  shared immutable damage payload used by projectile and AOE flows

The AOE runtime reuses projectile collision primitives:

- `HitShape`
- `HitShapeBaker`
- `HitShapeMath`
- `TargetSnapshot`
- `TargetStore`
- `EntityHandle`
- `EntityRegistry`
- `GameplayTarget`

## Runtime Boundary

`AoeRoot` and its adapter helpers may:

- read scene groups with `GetTree()`
- validate live Godot nodes
- bake AOE effect scenes and target hurtbox shapes
- instantiate optional visual effect scenes
- register hit listeners
- replay hits into gameplay callbacks
- remove effect visuals and registry handles when AOEs despawn

`AoeWorld` should stay plain-data oriented. It should not:

- call `GetTree()`
- touch live `Node` objects
- instantiate scenes
- inspect `PackedScene` contents
- apply gameplay damage directly
- call `DamageableState`

Input crosses into the world as:

- `AoeSpawnCommand`
- `TargetSnapshot`
- baked AOE and target shape definitions

Output crosses back to the adapter as:

- `AoeHitEvent`
- `AoeDespawnedEvent`

## Frame Flow

During `_PhysicsProcess()`:

1. `AoeRoot` syncs live targets from its configured `target_group`.
2. `AoeRoot` submits target snapshots to `AoeWorld`.
3. `AoeWorld.Step(...)` flushes pending despawns.
4. Pending AOEs are promoted into the active list.
5. Target bounds are refreshed and inserted into a spatial hash.
6. Each active AOE queries candidate target cells, runs baked shape
   narrow-phase checks, and emits hit events when allowed by pulse or
   lingering tick rules.
7. `AoeSpawnAdapter` drains hit and despawn events.

During `_Process()`:

1. `AoeSpawnAdapter` replays hit events through `AoeHitContext`.
2. Gameplay listeners such as `AoeAttack` or `ProjectileAttack` apply damage
   through `DamageableState.TryApplyDamage(...)`.

Hit reactions are deferred out of the physics runtime, matching the projectile
runtime boundary.

## Damage Rules

AOE damage is snapshotted before spawn with `DamageSnapshot`.

Pulse AOE:

- `lifetimeSeconds <= 0`
- hits every overlapping valid target once in the first AOE step
- despawns after that step

Lingering AOE:

- `lifetimeSeconds > 0`
- hits a target immediately when the target first overlaps the AOE
- repeats against that same target only after the AOE's `tickIntervalSeconds`
  has elapsed for that target
- resets the target gate when the target fully exits
- hits immediately again after exit and re-entry
- despawns when lifetime reaches zero

There is no global tick. Repeat timing is owned per AOE and per target contact.

## Authoring

Direct player AOE attacks use
[`scripts_cs/Attack/AoeAttack.cs`](../scripts_cs/Attack/AoeAttack.cs).

Important exports:

- `aoe_effect_scene`: effect scene with a root `Node2D` and a
  `CollisionShape2D` at `CollisionShape2D`
- `aoe_damage`: damage amount captured into `DamageSnapshot`
- `aoe_lifetime_seconds`: `0` for pulse, greater than `0` for lingering
- `aoe_tick_interval_seconds`: repeat interval for lingering AOEs
- `aoe_count`: number of AOEs to spawn per attack perform
- `spawn_at_aim_position`: uses aim point instead of attack node position
- `spawn_at_owner_position`: uses the owning actor position instead of attack
  node position
- `aoe_burst_radius`: optional distribution radius for multiple AOEs
- `randomize_aoe_positions`: uses random positions inside `aoe_burst_radius`
  instead of the deterministic ring distribution

Benchmark scenes:

- [`scenes/attacks/basic_aoe_attack.tscn`](../scenes/attacks/basic_aoe_attack.tscn):
  representative player AOE benchmark that spawns random AOEs around the
  player. With the current four equipped copies in `player.tscn`, holding fire
  targets roughly 256 sustained live AOEs instead of a cap-saturating stress
  case.
- [`scenes/attacks/basic_aoe_effect.tscn`](../scenes/attacks/basic_aoe_effect.tscn):
  baked AOE collision and simple visual effect

The current player scene wires four projectile attacks and four AOE attacks
under `Player`; `max_attack_count` is set to `8` so all are equipped.

## Projectile Impact AOE

[`ProjectileAttack`](../scripts_cs/Attack/ProjectileAttack.cs) can spawn an AOE
on projectile hit or when projectile-applied mob stacks reach a threshold.

Important exports:

- `impact_aoe_effect_scene`
- `impact_aoe_damage`
- `impact_aoe_lifetime_seconds`
- `impact_aoe_tick_interval_seconds`
- `projectile_direct_damage_enabled`

When `impact_aoe_effect_scene` is assigned, each valid projectile hit spawns
one AOE at `ProjectileHitContext.Position`. Piercing projectiles spawn an AOE
on every allowed pierce hit and keep traveling until their existing pierce
rules despawn them.

Set `projectile_direct_damage_enabled = false` for explosion-only projectiles
that do not apply direct collision damage.

## Stack-Triggered Projectile AOE

`ProjectileStackExplosionEffect` is a child component under a
`ProjectileAttack`. Each valid projectile hit on a `Mob` adds
`stacks_per_projectile_hit` to the configured `stack_debuff_status` enum slot.
When `stack_explosion_threshold` is reached, the mob clears that status slot and
the effect spawns `stack_explosion_aoe_effect_scene` through the player
`AoeManager`.

The explosion defaults to the mob's `GlobalPosition`, so the blast is centered
on the target rather than the projectile edge contact point. Direct projectile
damage still uses the normal projectile damage path, and explosion damage still
uses the AOE hit replay path with a `DamageSnapshot`.

## Visuals

The first AOE visual path instantiates one optional effect scene per AOE. The
collision runtime does not depend on live `Area2D` overlap state; the visual
scene's collision nodes are disabled after instantiation.

There is no AOE `MultiMesh` renderer yet. If profiling shows AOE visuals are a
major cost, add batching as a separate renderer layer without moving collision
or damage into live scene nodes.

## Debug And Verification

[`scripts_cs/Debug/Debug.cs`](../scripts_cs/Debug/Debug.cs) shows:

- `AOEs`
- `Mob AOEs`

Focused smoke tests:

- [`tests/aoe_runtime_smoke.gd`](../tests/aoe_runtime_smoke.gd): pulse,
  lingering tick, exit/re-entry, and scoped target group behavior
- [`tests/projectile_impact_aoe_smoke.gd`](../tests/projectile_impact_aoe_smoke.gd):
  projectile impact explosion behavior, no-direct-damage projectile mode, and
  pierce-triggered explosions
- [`tests/stacking_projectile_aoe_attack_smoke.gd`](../tests/stacking_projectile_aoe_attack_smoke.gd):
  projectile-applied mob stacks, threshold explosion damage, stack clearing,
  and player AOE target-group isolation

Use explicit `--log-file` paths for headless Godot tests on this machine,
matching the existing smoke test convention.
