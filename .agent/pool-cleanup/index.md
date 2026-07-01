# Combat Pool Cleanup (Disabled-Entity Trimmer)

## Summary
Add a background garbage-trimmer for the disable-in-place combat entity pool.
Expired projectiles/AOEs are already disabled (not destroyed) and reused by later
spawns. Nothing ever deletes them, so the world permanently retains every entity
the busiest scene ever produced. That retained population is what makes idle
frames after a heavy scene cost ~1ms more than a fresh start (see the profiling
investigation: `CombatBatchedRenderSystem` self-time 0.05ms -> 1.17ms, plus small
0->nonzero bumps across every projectile/AOE job — all driven by retained chunks).

The trimmer runs at the very end of the simulation each frame and deletes a small,
bounded batch of disabled entities **only** when the frame has headroom and the
pool is genuinely oversized. During combat spikes it does nothing; when the scene
calms down it slowly drains the excess over many frames. It never deletes a large
number in one frame.

## Behavior contract
Per frame, at end of simulation:
1. **Headroom gate (both):** proceed only if the smoothed recent frame time is
   under budget AND this frame's elapsed wall-clock so far is under budget.
2. **Per-batch decision:** for each reuse key (`CombatRenderBatchId`), trim only if
   `disabled > RetentionTarget` AND `disabled > active * PoolRatioMultiplier`.
3. **Bounded delete:** delete down toward a floor, capped per-batch and per-frame.
   Floor = `max(RetentionTarget, active * PoolRatioMultiplier)` so a single frame
   can never drop the pool below the retention/ratio line — draining is inherently
   gradual and hysteretic.

Outcome:
- Combat spike -> gates fail -> keep everything for reuse, no cleanup.
- Scene calms -> gates pass -> excess drains a bounded batch per frame.
- Never -> thousands deleted in one frame (hard per-frame cap).

## Decisions locked (user)
- **Headroom signal = both gates:** smoothed frame-time EMA under budget AND
  current-frame elapsed under budget. Most conservative against spikes.
- **Trim scope = per reuse-key (`CombatRenderBatchId`):** active/disabled ratio and
  retention are evaluated per batch, mirroring the existing spawn-reuse keying, so a
  large idle pool of one visual type is trimmed even while another type is busy.

## Constraints & invariants the change must respect
- **Pool occupancy flag is `Active` (enableable).** Disabled `Active` == free pool
  slot; enabled == live. Source: `Active` in
  [CombatEcsComponents.cs:189](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L189);
  disable-on-expiry in
  [CombatLifetimeSystem.cs:78-79](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L78-L79),
  [:115-117](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L115-L117).
- **Reuse claims `WithDisabled<Active>()` slots, filtered by `CombatRenderBatchId`.**
  The trimmer must not compete for or delete a slot the same frame's spawn wants —
  it therefore runs **after** all spawn-apply systems. Source: dead-slot queries in
  [ProjectileSpawnApplySystem.cs:418-424](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L418-L424),
  [AoeSpawnApplySystem.cs:255-281](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L255-L281).
- **Delete-safety: no cross-frame `Entity` handles to pooled combat entities.** All
  cached `Entity` references (`TargetEntities`, `TargetProxy`) point at target
  proxies, gathered transiently per frame via `ToEntityArray(Temp/TempJob)`.
  Targeting/hits key off `TargetId` (int) and `TemplateKey` (Hash128), never a
  projectile/AOE `Entity`. Verified by grep across `System/`. So permanent deletion
  (which bumps the entity version) is safe.
- **Structural change = sync point.** `EntityManager.DestroyEntity` completes running
  jobs. Running last in the simulation (after combat jobs, before presentation) keeps
  that sync where a sync would happen anyway. The per-frame cap keeps it cheap.
- **Reusable archetypes covered:** basic projectile, child-spawner projectile
  (`TimedSpawnTag`), lingering AOE, timed lingering AOE, impact AOE. All share
  `Active` + `CombatRenderBatchId` + a domain tag, so one batch-keyed query spans all
  five. Source: archetypes in
  [ProjectileSpawnApplySystem.cs:398-412](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L398-L412),
  [AoeSpawnApplySystem.cs:47-94](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L47-L94).
- **World bootstrap is automatic.** Systems are discovered by
  `DefaultWorldInitialization.GetAllSystems` at
  [CombatEcsComponents.cs:22-23](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L22-L23);
  a new `[UpdateInGroup]` system needs no manual registration.
- **Wall-clock timing is main-thread / not Burst.** `realtimeSinceStartupAsDouble`
  and the gating logic run in a managed `SystemBase`. The optional count pass may be
  Burst, but decision + `DestroyEntity` are main-thread.

## Mechanisms reused vs introduced
- **Reused:** `Active` enableable free-list, `CombatRenderBatchId` reuse key,
  `WithDisabled<Active>()` dead-slot queries (same shape the trimmer counts/gathers
  with), automatic system discovery, `LateSimulationSystemGroup` (built-in end-of-sim
  slot).
- **Introduced:**
  - `CombatFrameClock` singleton + a tiny OrderFirst stamp system (supplies both
    gates: frame-start timestamp and smoothed frame-time EMA).
  - `CombatPoolCleanupConfig` singleton (tunables, with sane constant defaults).
  - `CombatPoolCleanupSystem` in `LateSimulationSystemGroup` (the trimmer).
  No new representation of pool state — it reads the existing `Active`/batch data.

## Design validation vs invariants
- *Runs after reuse* — `LateSimulationSystemGroup` updates last within
  `SimulationSystemGroup`, after every spawn-apply and `CombatLifetimeSystem`. ✔
- *No same-frame slot contention* — spawn reuse for frame N already ran; trimmer only
  removes slots above the per-batch floor, which reuse did not consume. ✔
- *Delete-safe* — confirmed no cross-frame handles. ✔
- *Bounded* — `min(PerBatchDeleteCap, per-frame remaining)` + optional wall-clock
  slice; floor formula prevents dropping below retention/ratio in one frame. ✔
- *Spike-suppressed* — both gates must pass; EMA reacts to sustained load, elapsed
  gate catches the current frame. ✔

## Minimal/additive vs refactor comparison
- **Minimal/additive (chosen):**
  - resulting data flow: trimmer reads existing `Active`/`CombatRenderBatchId` state,
    deletes a capped set. No change to spawn/lifetime paths.
  - new concepts/types: 2 singletons (frame clock, config) + 2 systems (stamp,
    trimmer). No new pool representation.
  - copies/translations added: none on the hot spawn/lifetime paths; the trimmer's
    own gather is Temp-allocated and bounded.
  - long-term cost: one more end-of-sim system; tunables to maintain.
- **Refactor alternative:** replace disable-in-place pooling with immediate destroy +
  an explicit free-list, or fold trimming into the spawn-apply systems.
  - resulting data flow: removes the "retained forever" property at the source.
  - concepts changed: rewrites the reuse claim path and lifetime disable path.
  - copies removed: none meaningful.
  - long-term benefit: fewer moving parts, but discards the pooling perf win the
    reuse path was built for, and touches hot, already-optimized Burst code.
- **Decision:** choose additive. Reason: the reuse pool is a deliberate,
  performance-critical mechanism; the only missing behavior is bounded reclamation,
  which is a clean orthogonal add. It introduces no second representation of pool
  state (single source of truth stays the `Active` flag), so it does not trip the
  structural-warning rules.

## Task list
- [001-frame-clock.md](001-frame-clock.md) — `CombatFrameClock` singleton + OrderFirst
  stamp system (frame-start timestamp + smoothed frame-time EMA).
- [002-cleanup-config.md](002-cleanup-config.md) — `CombatPoolCleanupConfig` singleton
  with default tunables (budget, EMA alpha, retention, ratio, caps).
- [003-cleanup-system.md](003-cleanup-system.md) — `CombatPoolCleanupSystem` in
  `LateSimulationSystemGroup`: both-gate check, per-batch decision, bounded delete.
- [004-tests.md](004-tests.md) — PlayMode tests: drains when idle, suppressed under
  load, respects per-frame cap and retention floor.
- [005-docs-and-memory.md](005-docs-and-memory.md) — simulation/perf docs + memory note.

## Open questions / out of scope
- **Placement alternative:** `LateSimulationSystemGroup` (chosen) vs last in
  `PresentationSystemGroup` (after render submit). LateSimulation is idiomatic and
  runs after reuse; revisit only if profiling shows the end-of-sim sync is visible.
- **Render per-entry overhead is separate.** Trimming frees chunks, shrinking the
  filtered-scan portion of `CombatBatchedRenderSystem`, but its per-registered-type
  fixed loop overhead ([CombatBatchedRenderSystem.cs:90-114](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L90-L114))
  remains. A zero-active early-out there is a complementary follow-up, not part of this
  task.
- **Tunable home:** defaults ship as constants in the config singleton; a `CombatRoot`
  inspector override can be added later if designers need per-scene tuning.
