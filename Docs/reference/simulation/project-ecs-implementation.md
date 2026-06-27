# Project ECS Implementation Details

This document tracks implementation decisions, patterns, and current architecture for the combat simulation system. Refer to [ecs-notes.md](./ecs-notes.md) for generic ECS performance principles.

---

## Projectile Pool Pattern

- Projectile entities carry `ProjectileTag` plus common
  `CombatKinematicsComponent`, `CombatCollisionComponent`,
  `CombatLifetimeComponent`, and generic `Active`. Projectile-only systems must
  query `ProjectileTag` or projectile identity data, never common combat
  components alone.
- Runtime despawn disables `Active`; it does not destroy projectile entities
  during normal churn.
- `CombatLifetimeSystem` and `ProjectileCollisionSystem` disable `Active` when a
  projectile leaves play.
- `ProjectileSpawnExpansionSystem` expands `ProjectileSpawnEvent` into
  one-entity `ProjectileSpawnCommand` values.
- `BasicProjectileSpawnApplySystem` and
  `ChildSpawnerProjectileSpawnApplySystem` group commands by faction/render
  type/slot kind, then query matching disabled chunks with
  `WithDisabled<Active>()` before materializing cold creates.
- Reuse key is faction, render type id, and slot kind. Slot kind is normal or
  child-spawner archetype.
- Root/external spawn events use a child-spawner slot only when the expanded
  command has child spawning enabled. Child-spawned children currently request
  normal slots with `HasChildSpawner = 0`.
- Child-spawner components are part of the entity archetype at creation time.
  Do not add/remove those components during reuse.

---

## AOE Pool Pattern

- Combat roots share `World.DefaultGameObjectInjectionWorld` and one
  ref-counted `CombatScope` entity. Faction is explicit through
  `CombatFaction`.
- `CombatScope` is shared across domains and factions, so it does not imply
  domain by itself. AOE entities carry `AoeTag` plus common
  `CombatKinematicsComponent`, `CombatCollisionComponent`,
  `CombatLifetimeComponent`, and generic `Active`. AOE systems must query
  `AoeTag`, never common combat components or scope membership alone.
- Runtime despawn disables `Active`.
- `AoeSpawnExpansionSystem` expands `AoeSpawnEvent` into one-entity
  `AoeSpawnCommand` values.
- `AoeSpawnApplySystem` groups commands by faction and AOE type id, then queries
  matching disabled chunks with `WithDisabled<Active>()` before materializing
  cold creates.
- AOE counters track active, spawned, despawned/reused, hit events, active
  visuals, and render batches.

---

## Spawn Reuse Scheduling

Projectile and AOE spawn reuse now avoid the old main-thread entity-slot slice
assignment. Spawn commands are stored in persistent native buckets keyed by the
same reuse identity already used for pooling: projectile faction/render
type/slot kind, or AOE faction/type id. Each bucket owns a cached disabled-slot
query with the matching filters, schedules one direct reuse `IJobChunk`, and
writes the same reset components that cold creation initializes. The apply
systems schedule all bucket reuse jobs first, complete one combined dependency,
then cold-create the unclaimed commands in the same frame.

### Progress

- The main thread no longer walks matching disabled entity slots just to assign
  worker slices.
- Worker time owns the disabled-slot scan and reset work.
- The current shape performs well when spawn load is spread across many
  buckets, archetypes, scopes, or type ids.
- Same-frame cold fallback remains unchanged, so underwarmed pools still work.

### Known Drawback

- Each bucket is currently one scheduled chunk job, not a parallel chunk job.
  If one bucket/archetype owns almost all matching chunks and other buckets have
  little or no work, reuse can still become effectively single-threaded.
- This is accepted for now because projectile and AOE spawn are not the current
  bottleneck, and avoiding unsafe atomic claim keeps the implementation simpler.

**Revisit if profiling shows** `Projectile.Spawn.ReuseJob`, `Aoe.Spawn.ReuseJob`,
or cold fallback dominates frame time. A likely next test is a hybrid path: keep
the current direct bucket job for normal many-bucket frames, but for very large
single-bucket frames count matching chunks on workers, build only a chunk prefix
on the main thread, and schedule parallel reset slices without unsafe atomics.

---

## Presentation Bridge

`CombatRoot` has no per-frame simulation loop. It is an authoring/registry
MonoBehaviour: it owns serialized references, registers templates/types, owns
render resources, owns the target registry, and submits managed spawn events.
Per-frame combat work is explicit in ECS systems and actor roots:

- `PlayerRoot.Update` and `MobRoot.Update` push target proxy position and shape
  into ECS before simulation; their `LateUpdate` deletes queued dead proxies.
- `CombatBatchedRenderSystem` (`PresentationSystemGroup`) resolves the owning
  `CombatRoot` by `CombatFaction` and submits batched render instances through
  tag-scoped queries (`ProjectileTag`/`AoeTag`), pulling render resources from
  the root's registry.
- `CombatApplyFinalizeSystem` (`SimulationSystemGroup`) applies target-bucketed
  `CombatHitEvent` values to ECS-owned `TargetHealth`, accrues status stacks,
  and freezes one `CombatTickResult` per hit target after collision and before
  spawn expansion.
- `CombatApplyBridge` (`PresentationSystemGroup`) is the only reader of managed
  `TargetCompanion` references and pushes one
  `ICombatTarget.ReceiveCombatTick` per target with the aggregate damage result
  and changed status snapshots.
- `CombatVfxDispatchSystem` (`PresentationSystemGroup`) resolves each scope's
  `CombatVfxRoot` by static int key and drains/dispatches VFX requests.

**Design**: ECS owns simulation state, event buffers, and render/VFX request
data; MonoBehaviours stay authoring/lifetime owners only; presentation timing is
explicit system-group ordering instead of scene-object callback timing; GPU
resource ownership stays with whichever root/dispatcher created the
`GraphicsBuffer`/`Material`/`Mesh`.

Plain damage now crosses the managed bridge as one condensed per-target result.
Per-hit count and crit count are preserved on `CombatTickResult` for feedback.

---

## Collision Event Dispatch

Current projectile and AOE collision systems write one `CombatHitEvent` per hit
into a target-bucketed native map. The bucket key is the target proxy entity, so
per-target grouping is available without sorting. This removed the old
single-threaded drain/sort bottleneck, but it intentionally does not condense
multiple hits into one damage aggregate yet.

### Implemented Shape

- Collision jobs enqueue hits through the unbounded `CombatApplyFinalizeSystem`
  hit queue. Finalize buckets those hits by target proxy with
  `NativeParallelMultiHashMap` and `GetUniqueKeyArray`; it does not sort before
  applying damage.
- `CombatApplyFinalizeSystem` completes producers, iterates unique target keys
  in a parallel job, rolls crits with deterministic `Unity.Mathematics.Random`,
  sums damage per target, subtracts from `TargetHealth.Current`, and freezes one
  `CombatTickResult` for the presentation bridge.
- Stack accrual happens in the same finalize job against each target proxy's
  `TargetStackEntry` buffer. Each target key is owned by one job index, so buffer
  writes do not alias.
- `StatusProcessSystem` runs after finalize and before spawn expansion. It
  processes target stack buffers in parallel, decays/fizzles entries, and queues
  threshold AOE or projectile detonations for same-frame expansion.
- `CombatApplyBridge` keeps the managed push on the main thread. It resolves
  `TargetCompanion` and calls `ICombatTarget.ReceiveCombatTick` once per target
  that received direct damage or changed status.
- ECS owns target HP in `TargetHealth`, seeded once at proxy creation from
  `ICombatTarget.CombatMaxHealth`. ECS subtracts damage and may push negative HP;
  actor roots mirror that pushed value, clamp for local health display, decide
  death, and own GameObject lifetime.

### Intentional Tradeoffs

- Managed direct-damage replay no longer preserves one `CombatHitData` per hit.
  A frame with N qualifying hits on one target produces one `CombatTickResult`
  with `HitCount == N`, `CritCount`, `DamageTaken`, and `Health`.
- Per-hit authored side effects that need hit identity must stay in ECS producer
  or status/spawn paths rather than relying on managed damage replay.
- The managed boundary cost is reduced to one `ReceiveCombatTick` per hit target
  per tick.
- Debug counters still need clearer separation between raw collision hits,
  aggregate combat ticks, status changes, detonation spawns, and VFX requests.

### Future Target Direction

- Keep side-effect and VFX paths split from direct health aggregation.
- Route future DoT damage through `CombatHitEvent` so the same ECS-owned HP,
  status, and per-tick dispatch pipeline handles it.
- Preserve the hybrid boundary: ECS owns scalable HP/status simulation, while
  actor roots own authored feedback, death decisions, and GameObject lifetime.
