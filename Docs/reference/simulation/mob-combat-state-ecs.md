# Mob Combat State ECS Redesign

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

This is the detailed combat-state ECS redesign doc. Use [index.md](./index.md)
for the simulation overview and aspect map.

Status: proposed redesign of the current mob damage, projectile damage replay,
and AOE damage replay systems

## Summary

This is a redesign of the current systems, not a new parallel combat feature.
The goal is to replace the current plain-damage path where projectile and AOE
roots drain individual hit events and call back into GameObject-owned health.

Mob GameObjects should continue to own authored Unity presentation and movement:

- `Transform`
- `Rigidbody2D` movement
- body and hurtbox colliders
- sprite and animation state
- behavior FSM presentation
- death presentation and object lifetime

Mob health and status should move to ECS-owned combat data:

- current and max health
- damage accumulation
- status stacks
- status tick state
- death flags or death transition requests
- compact main-thread sync records for actor roots

In late update, ECS health and status results are synced back to mob roots on the
main thread. Mob roots then update animation, hurt presentation, debug widgets,
target unregistration, and soft-death object handling.

This keeps the hybrid architecture intact: low-count actors stay as GameObjects
for movement and presentation, while scalable combat state and hit processing
stay in ECS.

## Current System Being Redesigned

Current ownership shape:

- `MobRoot` owns health, status-like local combat reactions, and death cleanup.
- The projectile collision systems (discrete and continuous) and
  `AoeCollisionSystem` emit hit events for each contact.
- `DamageFinalizeSystem` freezes native damage events, and
  `DamageDispatchBridge` replays those hits on the managed side.
- Plain damage, status effects, and semantic hit effects share the same replay
  stream.
- GameObjects handle individual hits even when the only required result is a
  numeric health or status change.

This is acceptable while hit counts are low. It becomes the wrong scale boundary
once projectile and AOE counts become much larger than mob count.

The redesigned system keeps the existing ECS collision work, target proxy
bridge, combat roots, and GameObject presentation model. It changes where combat
state lives and what crosses the ECS/GameObject boundary.

## Redesign Goals

- Move authoritative mob health and status state out of `MobRoot` and into ECS.
- Keep mob transform, movement, animation, and death presentation on the
  GameObject.
- Convert plain projectile/AOE hits into ECS damage and status aggregates.
- Sync compact final health/status results to mobs in late update on the main
  thread.
- Keep per-hit managed replay only for semantic effects that need hit identity.
- Make hit draining refactorable into ECS systems and jobs that can run in
  parallel.
- Make the expensive work scale with projectile/AOE count inside ECS, then cross
  back to GameObjects at mob-count scale.

## Non-Goals

- Do not move mob movement or wall/body collision into ECS.
- Do not make projectiles, AOEs, or beams into GameObjects.
- Do not remove scoped projectile/AOE roots as authoring, lifetime, and
  presentation bridges.
- Do not make a second damage model beside the existing typed damage snapshots.
  Existing damage data should feed the ECS aggregate path.
- Do not remove semantic hit events for impact AOE, projectile burst, VFX, or
  authored effects that truly need per-hit data.

## Rationale

The project assumes projectile count is much greater than mob count. A frame may
contain tens of thousands of projectiles or many overlapping AOEs, but only a
small number of player and mob targets.

The current hit replay shape scales managed work with hit count. That is the
wrong boundary for plain damage and status:

- one callback per projectile hit scales with projectile count
- one callback per AOE hit scales with AOE overlap count
- most plain hits only need numeric health/status changes
- mob count is the smaller side of the relationship

Aggregating damage and status in ECS changes the boundary cost from roughly
`O(hit events)` to roughly `O(hit targets)` for plain combat results. Per-hit
callbacks remain useful only for effects that truly need per-hit semantics.

The main redesign is therefore not "ECS for mobs" broadly. It is narrower:
health and status move to ECS so projectile/AOE hit output can be reduced before
GameObject code sees it.

## Ownership

### GameObject Owned

`MobRoot` owns Unity object state and authored presentation:

- setup validation
- target registry registration and unregistration
- movement intent application to `Rigidbody2D`
- body collision through Unity Physics2D
- behavior and animation drivers
- hurt flash or hit animation requests from synced combat results
- death presentation after ECS reports death
- pooling or delayed destruction after replay/sync safety is guaranteed

`MobRoot` should not be the authoritative owner of current health or status
stacks once this design is implemented.

### ECS Owned

ECS owns combat state for each target:

- stable target id
- current health
- max health
- alive/dead combat flag
- status stack slots
- pending damage aggregates
- pending status deltas
- death transition record
- late sync record data

Projectile, AOE, and future beam systems write to ECS-owned combat data. They do
not call `MobRoot.TakeDamage(...)` or a similar managed callback for every plain
hit.

## Frame Flow

Target registration:

1. Mob root registers its hurtbox and stable target id with target registries.
2. A matching ECS combat-state entity or buffer record exists for that target.
3. Player and mob roots push target proxy position and shape data as they do
   today.

Simulation:

1. Projectile and AOE collision jobs detect raw hits from snapshots.
2. Raw hits are converted into pending damage/status contributions.
3. ECS systems aggregate contributions by target id, combat scope, and status
   type.
4. ECS applies aggregated health and status changes to target combat state.
5. ECS emits compact sync records for targets whose visible combat state changed.
6. ECS emits separate side-effect events only for authored effects that need hit
   identity.

Late main-thread sync:

1. A presentation bridge drains compact target sync records.
2. The bridge maps target ids back to live actor roots.
3. Mob roots receive the final health/status view for the frame.
4. Mob roots update animation requests, hurt presentation, debug display, and
   death object state.

## Replacement Shape

The current path:

1. Collision system emits one hit event per contact.
2. Root drains every hit event.
3. Root maps hit target id to a managed mob.
4. Managed mob callback applies damage/status.

The redesigned path:

1. Collision system emits raw hit data into ECS-owned intermediate data.
2. ECS jobs aggregate direct damage and status by target.
3. ECS applies health/status changes to authoritative combat state.
4. ECS emits one compact sync record for each changed target.
5. Late main-thread sync maps changed target ids to mob roots.
6. Mob roots update presentation from final state.

Semantic effects still take a side path:

1. Collision system or aggregate system emits semantic side-effect events for
   authored effects that need per-hit identity.
2. Roots or a presentation bridge drain those lower-volume events.
3. Managed routing spawns impact AOEs, projectile bursts, VFX, or other authored
   follow-up gameplay.

## Buffer Split

Do not use one hit buffer for every meaning. Split data by purpose:

- raw collision hits: high-volume ECS-only intermediate data
- damage aggregates: target-level direct health changes
- status aggregates: target/status-level stack and tick changes
- side-effect hit events: low-volume semantic events such as impact AOE or
  projectile burst spawn
- VFX requests: visual-only spawn requests and impact positions
- presentation sync records: compact final state copied back to actor roots

Plain damage and status should not require managed per-hit replay. Side-effect
events can still cross the managed boundary when they represent authored gameplay
that cannot yet be aggregated.

## Root Drain Refactor

`DamageDispatchBridge` replay can be refactored once plain damage/status no
longer needs individual GameObject callbacks.

Target shape:

- Projectile and AOE collision systems still produce raw hit data.
- A shared ECS aggregation path consumes raw hits from both domains.
- Aggregation systems/jobs run in parallel where data dependencies allow.
- Root drain methods stop applying plain damage one hit at a time.
- Roots drain only semantic side-effect events, VFX requests, and compact
  presentation data.

This makes projectile and AOE hit processing parallelizable data work instead
of serialized managed callback work.

Current drain responsibilities should split into:

- ECS aggregate systems: direct damage, status deltas, crit results if needed,
  death transition detection
- semantic event drain: impact AOE, projectile burst, source-node callbacks,
  non-aggregated authored effects
- VFX drain: visual-only impact or status feedback requests
- presentation sync: final health/status view per changed actor

The redesigned root drain should not be a place where every raw projectile hit
turns into a managed call.

## Aggregation Rules

Direct damage:

- aggregate by target id and combat scope
- preserve damage type information
- resolve crits in ECS if crit results are aggregated before managed replay
- do not depend on `UnityEngine.Random` inside high-volume managed hit loops

Status:

- aggregate stack deltas by target id and status type
- keep authoritative stack counts in ECS
- resolve threshold triggers in ECS when practical
- emit semantic side-effect events only for threshold results that spawn or
  route authored gameplay

Death:

- ECS sets the authoritative combat dead flag when health reaches zero
- late sync tells `MobRoot` to enter death presentation
- mob root disables body/hurtbox colliders and unregisters from target registries
- object cleanup waits until target id sync/replay safety is guaranteed

## Migration Plan

1. Identify current direct-damage replay responsibilities in
   `DamageDispatchBridge`.
2. Add ECS combat-state data for registered mobs while keeping existing
   `MobRoot` health path as compatibility.
3. Add compact health/status sync records and a main-thread presentation bridge.
4. Route direct projectile damage into ECS aggregates before calling managed
   damage callbacks.
5. Route direct AOE damage into the same aggregate path.
6. Split semantic hit events from direct damage/status aggregate data.
7. Move status stack storage and threshold checks into ECS.
8. Reduce `DamageDispatchBridge` replay to the bridge work still needed after
   aggregation, or replace it with compact presentation sync.
9. Remove or narrow managed per-hit damage callbacks after tests prove aggregate
   behavior matches old gameplay.

## Acceptance Shape

- A frame with many plain projectile hits against a small mob set produces at
  most one direct-damage application per target per relevant scope.
- A frame with many AOE overlaps produces compact target/status aggregates, not
  one managed callback per overlap.
- Main-thread health/status sync cost scales with live target count, not
  projectile count.
- `CombatRoot` no longer needs to handle every individual plain hit.
- Per-hit semantic effects still work for impact AOEs, projectile bursts, and
  authored trigger effects that require hit identity.
- Debug counters distinguish raw hits, aggregated damage applications,
  aggregated status applications, semantic side-effect events, VFX requests, and
  presentation sync records.

## Open Questions

- Should the combat-state entity be one entity per mob target, or should target
  combat state live in scope-owned buffers keyed by target id?
- Should player health/status use the same ECS data path immediately, or should
  mobs move first and player support follow?
- Should the late sync bridge be a managed ECS system, a `GameRoot` service, or a
  small dedicated `CombatPresentationRoot`?
- Which status effects need ECS-side threshold resolution first: poison,
  burning, shock, or volatile explosion stacks?
