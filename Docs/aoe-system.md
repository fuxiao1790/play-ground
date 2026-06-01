# AOE System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

The AOE runtime mirrors the projectile runtime shape:

- scoped roots own Unity interop
- DOTS AOE systems own spawn materialization, collision, recycling, and batched rendering
- gameplay callbacks replay after simulation

AOEs are part of the data-runtime half of the hybrid architecture. They query
snapshots of player and mob hurtboxes instead of depending on one live trigger
GameObject per gameplay AOE.

Primary uses:

- player AOEs hitting mobs
- mob AOEs hitting player
- projectile impact explosions
- stack-triggered mob-centered explosions
- authored pulse and lingering AOE attack prefabs
- large overlapping AOE fields from scaled spell builds

Non-goal:

- one global damage authority that discovers every target and effect in the scene

## Scoped Roots

One `AoeRoot` class serves both targeting directions. The scene contains two
instances: one configured for player AOEs targeting mob hurtboxes, one for mob
AOEs targeting player hurtboxes. The class itself is not split.

Each root instance owns:

- one DOTS scope entity in `World.DefaultGameObjectInjectionWorld`
- one Unity adapter boundary
- AOE effect template cache
- target hurtbox shape cache
- listener maps for gameplay callbacks
- batched AOE render resources
- profiling counters and visual budget ownership

## Boundary Rule

`AoeRoot` may:

- read target registries
- validate live Unity objects
- bake AOE effect collider shapes
- register hit listeners
- replay hits into gameplay callbacks
- submit batched AOE visuals

AOE ECS systems must not:

- touch GameObjects, Transforms, Components, or Colliders
- instantiate effects
- apply gameplay damage
- call Physics2D

Input into world:

- AOE spawn command
- target snapshots
- baked AOE and target shape definitions
- immutable damage snapshot

Output from world:

- AOE hit events
- AOE recycle events

## Damage Rules

Pulse AOE:

- lifetime is `0` or less
- hits every overlapping valid target once in first simulation step
- despawns after that step

Lingering AOE:

- lifetime is greater than `0`
- hits target immediately on first overlap
- repeats against same target after tick interval
- resets target gate when target exits fully
- hits immediately again after exit and re-entry
- despawns when lifetime reaches zero

There is no global AOE tick. Repeat timing belongs to each AOE and target contact pair.

## Authoring

`AoeAttack` should expose:

- AOE effect prefab
- damage amount or damage definition
- lifetime seconds
- tick interval seconds
- AOE count
- spawn at aim position
- spawn at owner position
- burst radius
- randomized positions toggle

AOE effect prefab should include:

- visual component or child visual
- one Collider2D that defines hit shape
- no required live trigger damage behavior

Circle AOEs are enough for the first implementation, but the baking boundary
should allow box and capsule support later without rewriting the runtime shape.

## Projectile Impact AOE

`ProjectileAttack` can spawn AOE on hit.

Required fields:

- impact AOE effect prefab
- impact AOE damage
- impact AOE lifetime seconds
- impact AOE tick interval seconds
- direct projectile damage enabled

When direct damage is disabled, projectile collision only creates explosion damage.

Piercing projectiles spawn impact AOE on each allowed pierce hit.

## Stack-Triggered AOE

Stack-triggered AOE is an attack-layer concern, not part of the AOE system
itself. The AOE system only spawns and resolves hits. What triggers a spawn is
decided above it.

The intended flow lives in the attack layer:

1. projectile hits valid mob
2. hit effect adds stacks to mob status slot
3. threshold clears that slot
4. effect spawns AOE through player AOE root
5. AOE damage replays through normal AOE hit callback path

Default explosion position is mob position, not projectile edge contact.

This flow is a proof that projectile hit effects, generic status stacks, and AOE
spawn callbacks compose cleanly. It should not be hard-coded as the only
status-stack behavior.

Status stacks should be generic so later effects such as poison, burning, shock,
or volatile explosions can share the same runtime concept.

## Visuals

AOE visuals use batched GPU-instanced render submission by AOE type. Live visual
objects do not own gameplay state.

Because scaled builds may create many overlapping AOEs, do not let this become
the only rendering path. Plan for:

- pooled effect prefabs for low and medium counts
- batched ring/sprite/mesh visuals for high counts
- particle emission caps by effect priority
- gameplay AOE count independent from visual particle count

Do not make live visual objects responsible for damage logic.

## Performance Baseline

AOE runtime must support many active areas at once.

Required practices:

- bake AOE shapes once per effect prefab
- use spatial hash or equivalent broad phase for target queries
- keep per-target tick gates allocation-light
- avoid one coroutine per AOE
- avoid live trigger callbacks as the authoritative damage path
- track active AOEs, spawned AOEs, despawned/reused AOEs, hit events, render
  batches, simulation time, visual count, and allocations

Scene-object bridge:

- actor hurtboxes are registered from player and mob GameObjects
- `AoeRoot` snapshots target state into its `AoeScope` entity
- AOE ECS systems emit plain hit events into scoped buffers
- root replays hits to actor components after simulation
- Physics2D remains responsible for player/mob/wall body collision

## DOTS Runtime Status

The current implementation uses Entities/DOTS in the shared default world:

- `AoeRoot` creates an `AoeScope` entity with target, spawn, hit, and recycle buffers.
- AOE entities carry `AoeTag`, `AoeActiveTag`, `AoeIdentityComponent`, common
  combat components, render data, and hit-spawn snapshot data.
- AOE systems require `AoeTag` or `AoeScope`; common combat components alone do
  not make an entity eligible for AOE simulation.
- `AoeSpawnSystem` drains scoped spawn/recycle buffers and reuses inactive AOE
  entities by scope/type.
- `AoeCollisionSystem` runs target-mask filtering and shape collision against
  `CombatTargetElement` snapshots, emits `AoeHitElement`, and recycles pulse AOEs.
- `AoeRenderPrepareSystem` writes render matrices for active AOEs, and
  `AoeRoot` submits GPU-instanced batches.

## Tests To Port

PlayMode tests should cover:

- pulse hits each overlapping target once
- lingering hits immediately
- lingering repeats after per-target tick interval
- full exit and re-entry hits immediately again
- player AOE root ignores player targets
- mob AOE root ignores mob targets
- projectile impact AOE works
- pierce-triggered impact AOE works
- stack threshold explosion clears stack and damages valid targets
