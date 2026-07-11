# Spawn Pool Top-Up: delete the ECB cold path

## Summary

The three spawn-apply systems (`ImpactAoeSpawnApplySystem`,
`LingeringAoeSpawnApplySystem`, `ProjectileSpawnApplySystem`) each do:

1. **Reuse** — a single-threaded Burst `IJob` claims disabled pool slots
   (`WithDisabled<Active>`) and writes their components via chunk type-handles.
2. **Cold-create** — the unreused command suffix is recorded into an
   `EntityCommandBuffer` on the main thread (managed `CreateEntity` + ~14
   `SetComponent` per command) and played back.

At game start the pool is empty, so every command is a cold create. Two costs
show up: the managed record loop (~28 ms, mislabeled inside the AOE
`ReuseJobMarker`) and `EntityCommandBuffer.Playback` (~37 ms). Both are cold-path
cost, and both write the same component data **twice** (into the ECB command
stream, then out during playback).

**This change deletes the cold path entirely.** Because the required slot count
is known before the fill runs (`= command count`), we top the pool up to demand
*before* reuse, then the existing reuse job fills everything in one pass:

```
totalRequests = commands.Length
have          = _deadSlotQuery.CalculateEntityCount()   // disabled slots present
deficit       = max(0, totalRequests - have)

if (deficit > 0)
{
    var newSlots = EntityManager.CreateEntity(archetype, deficit, Temp); // 1 structural change
    // new entities default to Active ENABLED; make them pool slots:
    disable Active on newSlots                                            // non-structural
}

reuse job fills ALL commands   // existing job, UNCHANGED; reuseCount == totalRequests
```

The reuse job is untouched — it simply never runs dry. No ECB, no record loop,
no playback, no suffix. Steady state (pool already warm, `deficit == 0`) does
**zero** structural changes per spawn tick — pure enable-bit reuse.

## Rationale

- The ECB cold path is a *second data path* for an operation the reuse job
  already performs ("materialize a command into a pool slot"). Growing the pool
  and letting the one writer fill it collapses that to a single path.
- The slot count is exact and known up front, so pre-creating is safe with no
  estimation and no leftover suffix — the objection that motivated the ECB
  ("we don't know how many until we've walked the slots") does not hold: demand
  is the command count, independent of how many slots already exist.
- Structural work is unchanged in the worst case (still one `CreateEntity` batch
  per lane), but the double data-write and the managed record loop are gone, and
  a warm pool drops to zero structural changes.

## Constraints & invariants the change must respect

- **Reuse discovers slots by `Active` only.** The fill loop skips a slot solely
  on `if (activeMask[i]) continue;` — [AoeSpawnApplySystem.cs:204](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L204),
  [ProjectileSpawnApplySystem.cs:393](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs#L393).
  → Fresh slots must have **`Active` disabled** to be filled; other gate bits are
  overwritten by the fill and don't matter pre-fill.
- **`CreateEntity(archetype, count)` returns enableable components ENABLED.**
  → A disable step on the new entities is mandatory before the fill; this is the
  one load-bearing gotcha. *Source:* Unity Entities enableable-default behavior.
- **Structural changes are main-thread and cause a sync point.** The
  `CreateEntity` batch is one structural change per lane, run before the fill;
  disabling `Active` is non-structural. *Source:* [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md)
  "Structural Changes & Sync Points".
- **Structural change invalidates chunk arrays and type handles.** The top-up
  (`CreateEntity`) must run **before** `_deadSlotQuery.ToArchetypeChunkArray` and
  `GetComponentTypeHandle` are fetched, or the fill job reads stale handles.
- **Count must reflect enable-state.** Use `EntityQuery.CalculateEntityCount()`
  (enableable-aware), **not** `CalculateEntityCountWithoutFiltering`.
- **Pool cleanup owns shrink.** `CombatPoolCleanupSystem` (LateSimulation) trims
  disabled slots in sparse chunks. Top-up grows to demand; cleanup trims the
  overshoot later. Same pool, intended warm/trim cycle — no conflict.
  *Source:* [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md)
  "Project Combat Pool Cleanup".
- **Reuse/Cold counter contract.** `Reuse + Cold == command count` per tick
  (profiling.md). After this change `Cold` is always 0 and `Reuse ==
  totalRequests`; keep both counters emitting so the invariant still reads true
  and dashboards don't break. A new `TopUp` (deficit) counter replaces the
  meaning `Cold` used to carry.
- **Determinism.** Ids come from command fields, not creation order; the fill is
  sequential. No determinism change.

## Mechanisms reused vs. introduced

- **Reused (unchanged):** the reuse `IJob` per lane and its type-handle plumbing;
  the `WithDisabled<Active>` pool query; the disable-in-place pooling model and
  `CombatPoolCleanupSystem` shrink.
- **Introduced:** a small shared helper `SpawnPoolTopUp.EnsureDisabledSlots(
  EntityManager, EntityArchetype, EntityQuery, int demand)` that computes
  `deficit`, batch-creates, and disables `Active` on the new entities. One
  helper, three call sites. No new component, no new system, no new data path.
- **Removed:** per-lane `EntityCommandBuffer` create/playback, the managed
  record loop, `AoeSpawnApplyUtility.RecordImpactReset` / `RecordLingeringReset`,
  `ProjectileSpawnApplySystem.CreateProjectileEntity` /
  `RecordCommonProjectileReset` / `RecordTimedSpawnReset`, and the projectile
  `ColdCreateMarker`.

## Design validation

- *Active-only discovery:* new slots created then `Active`-disabled → matched and
  filled by the unchanged job. ✓
- *Enableable default:* disable step is explicit and mandatory in the helper. ✓
- *Handle invalidation:* helper runs before chunks/handles are fetched. ✓ (call
  ordering is an acceptance criterion in each subtask).
- *Sync point:* one `CreateEntity` per lane when `deficit > 0`; zero when warm —
  no worse than the ECB playback it replaces, better in steady state. ✓
- *Counter contract:* `Reuse == totalRequests`, `Cold == 0`, new `TopUp` counter
  = deficit. ✓
- *Cleanup interplay:* growth by top-up, shrink by cleanup — same mechanism. ✓

## Minimal/additive vs. refactor comparison

- **Additive (keep ECB, Burst the record loop — the earlier plan):**
  - data flow: reuse fills some slots → managed/Burst record suffix into ECB →
    playback creates + copies the suffix.
  - new concepts/types: none, but the ECB path persists as a parallel
    materialization route.
  - copies/translations added: keeps the double data-write (record + playback).
  - long-term cost: two ways to materialize a command; playback (~37 ms) remains.
- **Refactor (top-up then fill — chosen):**
  - data flow: top pool up to demand → one reuse pass fills everything.
  - concepts changed/removed: the entire ECB cold path and its record helpers
    deleted; reuse job unchanged; one shared top-up helper added.
  - copies/translations removed: the ECB command-stream copy and the playback
    copy both gone; data written once, directly into the chunk.
  - long-term benefit: single materialization path; warm pool = zero structural
    changes per tick; ~28 ms record + ~37 ms playback both eliminated.
- **Decision:** **refactor.** The additive route preserves a second data path
  and the playback cost with no offsetting benefit. Top-up has one data path,
  fewer copies, and clearer ownership (pool size owned by demand; fill owned by
  the one reuse job).

## Default decision rule

"Reuse a disabled slot" and "cold-create a new entity" are the same domain
operation (materialize a command into a pool slot) expressed as two data paths.
Collapse to one source of truth — the reuse fill — unless a concrete blocker
appears. None found.

## Tasks

- [001-spawn-pool-topup-helper.md](001-spawn-pool-topup-helper.md) — shared
  `SpawnPoolTopUp.EnsureDisabledSlots` helper + `TopUp` counter convention.
- [002-impact-aoe-topup.md](002-impact-aoe-topup.md) — wire top-up into
  `ImpactAoeSpawnApplySystem`; delete its ECB path + `RecordImpactReset`.
- [003-lingering-aoe-topup.md](003-lingering-aoe-topup.md) — same for
  `LingeringAoeSpawnApplySystem`; delete `RecordLingeringReset`.
- [004-projectile-topup.md](004-projectile-topup.md) — same for
  `ProjectileSpawnApplySystem`; delete `CreateProjectileEntity`,
  `RecordCommonProjectileReset`, `RecordTimedSpawnReset`, `ColdCreateMarker`.

001 is a prerequisite for 002–004. 002–004 are independent of each other and
each separately PlayMode-verifiable via its Reuse/TopUp counters and the existing
simulation tests.

## Open questions / considerations

- **Disable-step mechanism.** Chosen: after `CreateEntity`, disable `Active` on
  the returned `NativeArray<Entity>` via a **main-thread loop** of
  `EntityManager.SetComponentEnabled<Active>(e, false)` — non-structural, bounded
  by `deficit`, and only runs during pool growth (never in warm steady state).
  *Escalation if it profiles hot:* switch the lane to
  `EntityManager.Instantiate(disabledPrefab, deficit, Temp)` from a
  `Prefab`-tagged template built once in `OnCreate` with `Active` pre-disabled —
  one structural op, no disable pass, at the cost of a maintained prefab entity
  per archetype. Not adopted up front because it adds persistent state for a
  cost that only exists during warm-up.
- **First-frame spike.** Top-up still cold-creates the whole demand on the very
  first busy frame (one `CreateEntity(N)` batch). That batch is far cheaper than
  N ECB creates + playback, but it is still one structural change of N entities.
  Optional future warm-up (pre-grow pools at scene load) would remove even that;
  out of scope here.
