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
- keeps each target gate ticking after exit
- re-entry before cooldown expiry does not hit; re-entry after cooldown expiry hits
- despawns when lifetime reaches zero

There is no global AOE tick. Repeat timing belongs to each AOE and target contact pair.

## Authoring

Current basic AOE content uses three authored pieces:

- an AOE template prefab, such as
  `Assets/Prefabs/Effects/BasicAoePulse.prefab`
- an `AoeConfig` ScriptableObject, such as
  `Assets/ScriptableObjects/Attacks/BasicAoeConfig.asset`, that maps a numeric
  type id to that template prefab and owns AOE gameplay values
- an equipped `AoeAttack` prefab or scene child under `Player/Attacks`

AOE template prefab requirements:

- root GameObject has `BasicAoePrefab`
- child GameObject named `Visual` has the `SpriteRenderer`
- child GameObject named `Hurtbox` has one supported `Collider2D` that defines
  hit shape
- collision logic uses the scaled `Hurtbox` shape multiplied by
  `AoeConfig.sizeMultiplier`
- batched rendering uses the sprite's scaled Transform multiplied by the same
  `AoeConfig.sizeMultiplier`
- prefab Transform scale is an authoring control; `sizeMultiplier` is the
  additional data-driven multiplier applied at runtime
- no required live trigger damage behavior

Circle AOEs are enough for the first implementation, but the baking boundary
should allow box and capsule support later without rewriting the runtime shape.

Basic pulse authoring steps:

1. Create an AOE template prefab under `Assets/Prefabs/Effects/`.
2. Add `BasicAoePrefab` to the root GameObject.
3. Add a child named `Visual` with a `SpriteRenderer`.
4. Add a child named `Hurtbox` with one supported `Collider2D`. A circle
   collider is the default path for simple pulses.
5. Create an AOE config asset with `Assets > Create > PlayGround > Attack >
   AOE Config`.
6. Assign these config fields:
   - `typeId`: the id used by attacks, for example `0`
   - `basicPrefab`: the AOE template prefab's `BasicAoePrefab`
   - `sizeMultiplier`: uniform scale applied after prefab Transform scale to
     both the batched visual and the baked hurtbox shape
   - `damage`: damage payload for each hit
   - `lifetimeSeconds`: `0` for current pulse AOEs
   - `tickIntervalSeconds`: unused by current pulse AOEs
   - `count`: number of AOEs spawned per cast
   - `spawnAtAimPosition`: spawn at mouse/world aim position; when false, spawn
     at the attack Transform position
   - `targetMask`: leave as `1` to use the owning root mask
7. Add or select an `AoeRoot` scene object for the targeting direction.
8. Set the root `targetMask` to the intended hurtbox layer mask. Player AOEs
   targeting mobs use `MobHurtbox`; mob AOEs targeting player use
   `PlayerHurtbox`.
9. Create an attack prefab under `Assets/Prefabs/Attacks/` with `AoeAttack`.
10. Assign `AoeAttack.aoeRoot`, assign the AOE config, and tune only
   attack-instance fields such as recovery and sound on the component.
11. Put the attack prefab under `Player/Attacks` and enable the component on the
   scene instance.

`AoeAttack` registers its config's AOE type definition with the assigned root
during setup. Scene roots can still carry hand-authored `aoeTypes` entries for
shared content, but equipped attacks should prefer config-driven registration so
AOE authoring stays symmetrical with projectile authoring.

Pulse content uses `lifetimeSeconds = 0`. Lingering content uses
`lifetimeSeconds > 0` and `tickIntervalSeconds` for per-target repeat gates.
Per-target gate entries live on the AOE, tick independently, and remain active
through target exit until their cooldown expires.

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
  combat components (including `CombatHitComponent` with `CritChance` and
  `CritMultiplier` written at spawn time from `AoeSpawnRequestElement`), render
  data, and hit-spawn snapshot data (`AoeHitSpawnComponent` holding
  `AoeProjectileBurstSnapshot`).
- AOE systems require `AoeTag` or `AoeScope`; common combat components alone do
  not make an entity eligible for AOE simulation.
- `AoeSpawnSystem` drains scoped spawn/recycle buffers and reuses inactive AOE
  entities by scope/type. Crit values flow from `AoeSpawnCommand` →
  `AoeSpawnRequestElement` → `CombatHitComponent` on the entity; no per-AOE
  dictionary lookup is needed at replay time.
- `AoeCollisionSystem` runs target-mask filtering and shape collision against
  `CombatTargetElement` snapshots, reads crit directly from `CombatHitComponent`,
  emits `CombatPendingHit` (shared with the projectile pipeline), flushes into the
  scoped `CombatHitElement` buffer, recycles pulse AOEs, and adds per-target hit
  gates for lingering AOEs.
- `AoeContactGateSystem` decrements lingering repeat-hit gates and compacts
  expired entries.
- `AoeSimulationSystem` clears scoped `CombatHitElement` hit buffers and expires lingering AOEs.
- `CombatRenderPrepareSystem` writes render matrices for active AOEs, and
  `AoeRoot` submits GPU-instanced batches.
- Trigger-link snapshot type `AoeProjectileBurstSnapshot` lives in
  `PlayGround.System.Common` (alongside `ProjectileImpactAoeSnapshot`,
  `ProjectileImpactProjectileSnapshot`, `ProjectileTrackingConfig`) so both
  systems share a single `CombatHitElement` buffer element type.

## Tests To Port

PlayMode tests should cover:

- pulse hits each overlapping target once
- lingering hits immediately
- lingering repeats after per-target tick interval
- exit and re-entry before cooldown expiry does not hit
- player AOE root ignores player targets
- mob AOE root ignores mob targets
- projectile impact AOE works
- pierce-triggered impact AOE works
- stack threshold explosion clears stack and damages valid targets
