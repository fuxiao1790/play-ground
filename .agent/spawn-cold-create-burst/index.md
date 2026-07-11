# Spawn Cold-Create: Managed ECB Append → Burst Job

## Summary

The three spawn-apply systems (`ImpactAoeSpawnApplySystem`,
`LingeringAoeSpawnApplySystem`, `ProjectileSpawnApplySystem`) each split spawn
into two halves:

1. **Reuse** — a single-threaded Burst `IJob` claims disabled pool slots and
   writes their component arrays via chunk type-handles.
2. **Cold-create** — the unreused command suffix is recorded into an
   `EntityCommandBuffer` (`CreateEntity` + ~14 `SetComponent`/`SetComponentEnabled`
   per command) **on the main thread in managed C#**, then played back.

At game start the pool is empty, so *every* command is a cold create. The
managed recording loop is what actually costs ~28 ms in the profiler — and
because the AOE systems record inside the `ReuseJobMarker` scope with no nested
sub-marker (unlike projectiles, which wrap it in `ColdCreateMarker`), that cost
is mislabeled as "ReuseJob" even though reuse itself is empty. The separate
`EntityCommandBuffer.Playback` (~37 ms) is the second half.

**This task** moves the managed recording loop into the existing per-domain
Burst spawn job. `EntityCommandBuffer.CreateEntity` / `SetComponent` /
`SetComponentEnabled` / `AppendToBuffer` are all Burst-compatible for unmanaged
components, so the suffix loop can run Burst-compiled on a worker instead of as
managed main-thread calls. Playback stays on the main thread (unchanged); this
task targets only the recording half the user asked to convert.

## Scope

Fuse cold-create recording **into the existing reuse job** rather than adding a
second job. The reuse job already computes `commandIndex` == reuse count after
its chunk loop; the cold suffix is exactly `commands[commandIndex ..]`. Doing it
in the same job avoids a `NativeReference` round-trip and a second schedule, and
keeps one Burst pass per domain.

## Constraints & invariants the change must respect

- **ECB in a job must not use `Allocator.Temp`.** Temp allocations aren't valid
  across a job boundary. Switch each `createEcb` to `Allocator.TempJob`.
  *Source:* precedent — [CombatPoolCleanupSystem.cs:106](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L106)
  already runs an ECB (ParallelWriter) inside a Burst `IJobChunk` with TempJob.
- **Structural changes stay on the main thread.** The job only *records* into the
  ECB (deferred); `createEcb.Playback(EntityManager)` still runs on the main
  thread after `.Complete()`. No structural change happens inside a job.
  *Source:* ecs-notes.md "Structural Changes" (jobs may queue, not apply).
- **Burst-compatibility of the record call tree.** The whole reachable tree of a
  `[BurstCompile]` job must be Burst-legal. The record helpers
  (`AoeSpawnApplyUtility.RecordImpactReset` / `RecordLingeringReset`,
  `ProjectileSpawnApplySystem.RecordCommonProjectileReset` / `RecordTimedSpawnReset`)
  are all static and touch only unmanaged component structs and `math` — verified
  Burst-legal. They need no `[BurstCompile]` attribute themselves; Burst compiles
  them as callees of the job.
- **Determinism.** A single-threaded `IJob` records the suffix sequentially in
  command order; playback creates entities in that same order. Entity ids come
  from command fields (`AoeId` / `ProjectileId`), not creation order, so ordering
  is already id-independent. No determinism regression. Do **not** switch to
  `ParallelWriter` — that would reorder and is out of scope.
- **Reuse-count contract.** `Reuse + Cold == command count` per tick (profiling.md
  "Spawn Apply Counters"). The job still writes `ReuseCount.Value = commandIndex`;
  the system computes `cold = totalRequests - reuse`. Unchanged.
- **No data conflict.** Both halves read `commands` read-only; reuse writes
  existing chunks via handles, cold records into the ECB — disjoint. Same job,
  sequential, trivially safe.

## Mechanisms reused vs. introduced

- **Reused:** the existing per-domain reuse `IJob` and its type-handle plumbing;
  the existing static record helpers; the TempJob-ECB-in-Burst-job pattern from
  `CombatPoolCleanupSystem`.
- **Introduced:** two fields on each spawn job — `EntityCommandBuffer Ecb` and
  `EntityArchetype Archetype` — and a suffix loop at the tail of `Execute()`.
  No new type, no new system, no new data path.

## Minimal/additive vs. refactor comparison

- **Additive (separate cold-create Burst job):**
  - data flow: reuse job → read `NativeReference` on main thread → schedule a
    second Burst job chained on the first → complete → playback.
  - new concepts: a second job struct per domain, a NativeReference dependency.
  - copies added: none, but an extra schedule + handle round-trip per domain.
  - long-term cost: two jobs describing one operation (claim-or-create slots).
- **Refactor (fuse into existing reuse job — chosen):**
  - data flow: one Burst job does reuse-writes then cold-records → complete →
    playback.
  - concepts changed: reuse job gains `Ecb`/`Archetype` fields and a tail loop;
    the main-thread managed loop and (projectile) `CreateProjectileEntity`
    instance helper + `ColdCreateMarker` are deleted.
  - copies removed: the `NativeReference`-mediated main-thread hop between reuse
    and cold.
  - long-term benefit: one job = one "materialize N commands into pool slots"
    operation; the source of truth for a spawned entity's component values is one
    Burst pass.
- **Decision:** **refactor.** The additive split introduces a second data path
  for the same operation with no benefit. Fusion has fewer runtime paths and
  matches the existing "one reuse job per domain" shape.

## Design validation

- *ECB-not-Temp:* switched to TempJob → satisfied (matches cleanup system).
- *Structural-changes-main-thread:* playback unchanged, still post-`Complete()`
  on main thread → satisfied.
- *Burst-legal call tree:* record helpers are static + unmanaged → satisfied
  (confirm at build; this is the primary silent-failure risk to watch).
- *Determinism:* single-threaded IJob, command order preserved → satisfied.
- *Counter contract:* `ReuseCount.Value = commandIndex` retained → satisfied.

## Follow-up (out of scope, noted)

The playback half (~37 ms) is untouched. The larger lever is strategy #4/#5 in
ecs-notes.md: `EntityManager.CreateEntity(archetype, count, Allocator.Temp)` for
the whole cold batch in one structural change, then a Burst chunk-write over the
new slots — eliminating the ECB (record *and* playback) for cold creates. Track
separately; this task is the requested Burst-append conversion only.

## Tasks

- [001-impact-aoe-cold-create-burst.md](001-impact-aoe-cold-create-burst.md) —
  fuse cold-create into `ImpactAoeSpawnJob`.
- [002-lingering-aoe-cold-create-burst.md](002-lingering-aoe-cold-create-burst.md) —
  fuse cold-create into `LingeringAoeSpawnJob`.
- [003-projectile-cold-create-burst.md](003-projectile-cold-create-burst.md) —
  fuse cold-create into `ProjectileSpawnJob`; delete instance
  `CreateProjectileEntity` + `ColdCreateMarker`.

Tasks are independent (three parallel systems); each is separately PlayMode-
verifiable via its Reuse/Cold counters and the existing simulation tests.

## Open questions

- None blocking. The one thing that can only be confirmed at build/run: that the
  record helpers compile under Burst. If any helper trips Burst, the fallback is
  to inline the needed field writes into the job (as the reuse path already does
  for `WriteCommon`) rather than reverting.
