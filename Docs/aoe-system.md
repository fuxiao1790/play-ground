# AOE System

All docs in `Docs/` are preliminary. They describe the current port intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

The Unity AOE runtime should mirror the projectile runtime shape:

- scoped roots own Unity interop
- plain `AoeWorld` owns collision and timing
- gameplay callbacks replay after simulation

AOEs are part of the data-runtime half of the hybrid architecture. They query
snapshots of player and mob hurtboxes instead of depending on one live trigger
GameObject per gameplay AOE.

Old implementation reference:

- `_OldGdProj/Script_Cs/System/Aoe/`
- `_OldGdProj/Script_Cs/Attack/AoeAttack.cs`
- `_OldGdProj/Script_Cs/Attack/ProjectileStackExplosionEffect.cs`
- `_OldGdProj/Scenes/attacks/basic_aoe_attack.tscn`
- `_OldGdProj/Scenes/attacks/basic_aoe_effect.tscn`

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

Target roots:

- `AoeRoot_PlayerToMob`: player AOEs targeting mob hurtboxes
- `AoeRoot_MobToPlayer`: mob AOEs targeting player hurtboxes

Each root owns:

- one plain `AoeWorld`
- one Unity adapter boundary
- AOE effect template cache
- target hurtbox shape cache
- listener maps for gameplay callbacks
- optional effect prefab instances and cleanup
- profiling counters and visual budget ownership

## Boundary Rule

`AoeRoot` may:

- read target registries
- validate live Unity objects
- bake AOE effect collider shapes
- instantiate optional visual prefabs
- register hit listeners
- replay hits into gameplay callbacks
- remove visuals when AOEs despawn

`AoeWorld` must not:

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
- AOE despawned events

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

Stack effect flow:

1. projectile hits valid mob
2. hit effect adds stacks to mob status slot
3. threshold clears that slot
4. effect spawns AOE through player AOE root
5. AOE damage replays through normal AOE hit callback path

Default explosion position is mob position, not projectile edge contact.

Stack-triggered AOE is a special attack used to prove that projectile hit
effects, generic status stacks, and AOE spawn callbacks compose cleanly. It
should not be hard-coded as the only status-stack behavior.

Status stacks should be generic so later effects such as poison, burning, shock,
or volatile explosions can share the same runtime concept.

## Visuals

Start with one optional visual prefab per AOE for correctness.

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
- track active AOEs, hit events, simulation time, visual count, and allocations

Scene-object bridge:

- actor hurtboxes are registered from player and mob GameObjects
- `AoeRoot` snapshots target state before stepping `AoeWorld`
- `AoeWorld` emits plain hit events
- root replays hits to actor components after simulation
- Physics2D remains responsible for player/mob/wall body collision

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
