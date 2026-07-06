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
- End-of-simulation cleanup may later destroy bounded excess disabled slots
  when frame headroom exists and the projectile pool is above its retention and
  active-ratio floor.
- `CombatLifetimeSystem` and `ProjectileCollisionSystem` disable `Active` when a
  projectile leaves play.
- `ProjectileSpawnExpansionSystem` expands `ProjectileSpawnEvent` into
  one-entity `ProjectileSpawnCommand` values.
- `ProjectileSpawnApplySystem` queries disabled projectile chunks with
  `WithAll<ProjectileTag>()` and `WithDisabled<Active>()` before materializing
  cold creates.
- There is one projectile archetype. `TimedSpawnComponent` and
  `TimedSpawnStateComponent` are present on all projectile slots; the component
  enabled bit selects whether interval children emit.
- Root/external spawn events set `HasTimedSpawner` on the expanded command when
  interval children should emit. Reuse can cross between timed and non-timed
  projectiles because timed spawn is reset as enableable state.

---

## AOE Pool Pattern

- Combat roots share `World.DefaultGameObjectInjectionWorld` and one
  ref-counted `CombatScope` entity. Faction is explicit through
  `CombatFaction`.
- `CombatScope` is shared across domains and factions, so it does not imply
  domain by itself. AOE entities carry `AoeTag` plus common
  `CombatKinematicsComponent`, `CombatCollisionComponent`, and generic
  `Active`; lingering AOEs also carry `LingeringAoeTag` and
  `CombatLifetimeComponent`. AOE systems must query `AoeTag`, never common
  combat components or scope membership alone.
- Runtime despawn disables `Active`.
- End-of-simulation cleanup may later destroy bounded excess disabled slots
  when frame headroom exists. Impact and lingering AOEs are evaluated as
  separate reuse pools because their archetypes differ.
- `AOE spawn expansion systems` expands `AOE variant spawn event` into one-entity
  `AoeSpawnCommand` values.
- `ImpactAoeSpawnApplySystem` queries `WithAll<AoeTag>()`,
  `WithDisabled<Active>()`, and `WithNone<LingeringAoeTag>()`.
- `LingeringAoeSpawnApplySystem` queries `WithAll<AoeTag>()`,
  `WithDisabled<Active>()`, and `WithAll<LingeringAoeTag>()`.
- Impact AOE stays lifetime/timed-spawn absent. Lingering AOE carries
  `LingeringAoeTag`, `CombatLifetimeComponent`,
  `TimedSpawnComponent` and `TimedSpawnStateComponent`; timed vs non-timed
  lingering reuse crosses through the enableable timed-spawn bit.
- AOE counters track active, spawned, despawned/reused, hit events, active
  visuals, and render batches.

---

## Spawn Reuse Scheduling

Projectile and AOE spawn reuse use one sequential command cursor per reuse
pool. Spawn commands are stored in one `NativeList` per reuse pool: projectile,
impact AOE, and lingering AOE. Each apply system owns one disabled-slot query,
captures matching chunks, and schedules one single-threaded Burst job that
walks chunks in order, resets disabled slots, and writes the number of commands
reused. The apply systems then cold-create the unreused suffix in the same
frame.

### Progress

- The main thread no longer builds worker slices or command-index lanes.
- The apply path has one deterministic command cursor per reuse pool.
- Burst job time owns the disabled-slot scan and reset work.
- The current shape keeps query/category selection in ECS queries rather than
  per-entity branch filters.
- Same-frame cold fallback remains unchanged, so underwarmed pools still work.
- Reuse packs disabled slots before cold-creating overflow.

### Known Drawback

- Reuse no longer parallelizes across worker lanes. This avoids lane imbalance
  and native stream overhead at the cost of one Burst job doing the dead-slot
  scan for each reuse pool.
- Cold creation now represents true pool shortage for the queried archetype,
  not worker-range mismatch.

**Revisit if profiling shows** `ProjectileSpawnApplySystem.ReuseJob`,
`ImpactAoeSpawnApplySystem.ReuseJob`, `LingeringAoeSpawnApplySystem.ReuseJob`,
or cold fallback dominates frame time. The next lever should preserve the
single source of truth for command order and disabled-slot ownership; do not
reintroduce worker-lane command streams without a measured benefit.

---

## Pool Cleanup Scheduling

`CombatPoolCleanupSystem` runs in `LateSimulationSystemGroup`, after the spawn
apply systems have already claimed same-frame reusable slots. A Burst-compiled
`IJobChunk` scheduled with `ScheduleParallel` sweeps every reuse pool through one
`IgnoreComponentEnabledState` query (`ProjectileTag`/`AoeTag` + `Active`), so each
chunk carries its active and disabled entities together.

Per chunk the job counts enabled `Active` entities. If that count is below
`ChunkActiveThreshold`, the chunk is treated as sparse and every disabled entity
in it is recorded for destruction on an `EntityCommandBuffer.ParallelWriter`.
Chunks at or above the threshold keep their disabled entities as a warm reuse
buffer whose size tracks current combat load. The system then completes the job,
plays the command buffer back on the main thread at end of simulation (sync point
isolated from the hot spawn/movement/collision/render-prep jobs), and disposes it.

There is no frame-time gate (wall-clock frame time includes vsync/GPU sleep, so a
calm scene falsely reads as over-budget) and no per-pool retention/ratio floor or
per-frame delete cap; trimming begins as soon as a chunk drops below the active
threshold.

---

## Presentation Bridge

`CombatRoot` has no per-frame simulation loop. It is an authoring/registry
MonoBehaviour: it owns serialized references, registers templates/types, owns
render resources, owns the target registry, and submits managed spawn events.
Per-frame combat work is explicit in ECS systems and actor roots:

- `PlayerRoot.Update` and `MobRoot.Update` push target proxy position and shape
  into ECS before simulation; their `LateUpdate` deletes queued dead proxies.
- `CombatBatchedRenderSystem` (`PresentationSystemGroup`) submits render
  instances through tag-scoped queries (`ProjectileTag`/`AoeTag`), drawing
  every registered kind together via the registry's shared atlas
  mesh/material instead of one batch per kind.
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
