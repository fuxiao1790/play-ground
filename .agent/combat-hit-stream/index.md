# Combat Hit Pipeline → NativeStream

## Summary
Replace the single `NativeQueue<CombatHitEvent>` hit sink (owned by
`CombatApplyFinalizeSingleSystem`) with **three `NativeStream`s**, one per parallel
producer job (projectile collision, impact-AOE collision, lingering-AOE collision).
Producers write hit events into per-chunk stream lanes; the finalize Burst job reads
all three streams lane-by-lane, accumulates per target, applies damage/stacks, and
disposes the streams in bulk at the end of the frame.

Goal: eliminate the per-read churn the queue caused and read-once / free-the-whole-
thing-once, without a main-thread copy. This is a **refactor** — the queue sink is
removed, not kept alongside a new path.

## Rationale
- `NativeQueue.ToArray`/`TryDequeue` on the read side pokes the queue's block pool.
  A `NativeStream` is written per-lane in parallel and freed in one `Dispose()` — the
  "read through lanes once, free entire thing at end" shape the user asked for.
- Producers are already parallel jobs pushing into a shared sink and forwarding a
  producer `JobHandle` to the finalize system. That push pattern is reused verbatim;
  only the container type changes (`NativeQueue` → `NativeStream`).

## Constraints & invariants (must respect)
1. **Concurrency / producers.** Three `IJobEntity.ScheduleParallel` jobs write hits:
   `ProjectileCollisionSystem`, `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`
   (the AOE two via `AoeCollisionCore.RunCollision`/`EmitHit`). Each also writes the
   Vfx/Projectile/Aoe event `NativeQueue`s — those stay queues, untouched. Each pushes
   its collision handle into `hitApply.ProducerHandle` (combined). Source: the three
   collision systems + `AoeCollisionCore.cs`.
2. **Read barrier.** `CombatApplyFinalizeSingleSystem.OnUpdate` calls
   `ProducerHandle.Complete()` before touching the sink; runs one Burst `IJob`; hands
   results to `CombatApplyBridge`. Source: `CombatApplyFinalizeSingleSystem.cs`.
3. **NativeStream write discipline.** `Writer.Write<T>()` requires
   `BeginForEachIndex(i)`/`EndForEachIndex()` bracketing each lane exactly once. A
   per-entity `Execute` cannot bracket a lane, so each producer job must implement
   `IJobEntityChunkBeginEnd`: `OnChunkBegin` → `BeginForEachIndex(unfilteredChunkIndex)`,
   `OnChunkEnd` → `EndForEachIndex()`. Lane = chunk index. Source: Unity.Collections
   `NativeStream`, Unity.Entities `IJobEntityChunkBeginEnd`.
4. **Lane-count correctness.** Stream `foreachCount` must be ≥ the largest
   `unfilteredChunkIndex` the scheduled query produces. Use
   `query.CalculateChunkCountWithoutFiltering()` **on the same explicit query the job is
   scheduled with**. The AOE systems already schedule with an explicit query; the
   projectile system currently schedules with no explicit query and must switch to the
   explicit-query overload so the count and the iteration match.
5. **Burst.** Producers and finalize are `[BurstCompile]`. `NativeStream.Writer`/`Reader`
   are Burst-compatible. Finalize uses `.Schedule().Complete()` (Burst path — do not use
   `.Run()`; see [[reference_ijob_run_not_burst]]).
6. **Ownership / lifetime.** Sink lifetime moves from one Persistent queue to three
   per-frame `Allocator.TempJob` streams. Each producer allocates its stream that frame
   and pushes it to `hitApply`; finalize disposes all streams after the finalize job and
   resets the fields to `default`. A producer that early-outs (empty query) does not push,
   so the field stays `default` and finalize treats it as "no hits this producer".
7. **Multiple producers.** Lane index spaces of independent jobs overlap (each starts at
   0), so a single shared stream would need cross-system offset coordination (rejected as
   fragile). Three streams — one per producer — keep lane ownership local to each job.

## Mechanisms reused vs introduced
- **Reused:** producer→finalize push (`hitApply.ProducerHandle` combine; new
  `hitApply.<X>HitStream` set), single Burst finalize `IJob`, `CombatApplyBridge` handoff,
  the existing per-target accumulation (`NativeHashMap` + `NativeList<TargetAccum>`).
- **Introduced:** `NativeStream` (standard Collections type) in place of `NativeQueue`;
  `IJobEntityChunkBeginEnd` on the three collision jobs. No bespoke container.

## Minimal/additive vs refactor comparison
- **Additive** (keep queue, add streams / adapter):
  - data flow: two sink types; a copy/translation shim to feed the finalize job.
  - new concepts: parallel queue+stream paths that must stay in sync.
  - copies added: queue→stream or stream→array translation.
  - long-term cost: two sources of truth for "the frame's hits". Structural warning.
- **Refactor** (replace queue with streams end-to-end) — CHOSEN:
  - data flow: producers write stream lanes → finalize reads lanes → dispose. One path.
  - types changed: `HitWriter` field type on 3 jobs; `AoeCollisionCore` hit-writer param;
    finalize sink fields; test injection helper.
  - copies removed: no `ToArray`/`TryDequeue`; no main-thread flatten.
  - long-term benefit: one sink concept, bulk free, zero per-element churn, no copy.
- **Decision:** refactor. **Reason:** the queue and stream describe the same concept
  (this frame's hit events); one source of truth, fewer copies, better free behavior.

## Default decision rule
The queue and the stream are the same domain concept (per-frame hits). Collapse to the
single stream-based representation; do not keep the queue as a compatibility path.

## Design validation (against invariants)
- (1)(2) Producer push + `ProducerHandle.Complete()` barrier unchanged; streams are only
  read after producers complete. ✔
- (3) `IJobEntityChunkBeginEnd` supplies the per-lane bracket `Execute` can't. ✔
- (4) Schedule each job on the explicit query whose `CalculateChunkCountWithoutFiltering()`
  sizes its stream — projectile switches to the explicit-query overload. ✔ (verify the
  index bound holds with enableable components — see Open questions.)
- (5) Finalize stays `.Schedule().Complete()` Burst. ✔
- (6) TempJob streams disposed each frame after the job; fields reset; early-out producers
  leave `default`. ✔
- (7) One stream per producer; no shared lane space. ✔

## Open questions / risks
- **0-lane placeholder validity.** For a producer that didn't push a stream this frame,
  the finalize job still needs a valid `NativeStream.Reader`. Plan: allocate a
  `new NativeStream(0, Allocator.TempJob)` placeholder and read it (0 lanes). Verify a
  0-`foreachCount` stream is legal in this Collections version; fallback is a per-stream
  `bool valid` guard with the reader field left constructed from a cached 1-lane empty
  stream.
- **unfilteredChunkIndex bound.** Confirm `CalculateChunkCountWithoutFiltering()` bounds
  `unfilteredChunkIndex` for a `ScheduleParallel(query, …)` over a query with enableable
  components. Expected yes; validate in-editor (an out-of-range `BeginForEachIndex`
  asserts).
- **Test injection.** `AoeSimulationTests` reflects the `HitQueue` field and Enqueues
  synthetic hits (`QueueStackHit`/`QueueDirectHit`). Replace with an internal
  `InjectTestHits(NativeArray<CombatHitEvent>)` on the finalize system that builds a
  1-lane stream and sets one stream field; update the two helpers.

## Tasks
- `001-finalize-stream-sink.md` — finalize system owns 3 streams, reads lanes, disposes;
  add `InjectTestHits`; define producer-facing API. (Do first — defines the contract.)
- `002-projectile-producer.md` — projectile collision job → stream writer +
  `IJobEntityChunkBeginEnd`, explicit-query schedule, push stream.
- `003-aoe-producers.md` — `AoeCollisionCore` hit-writer param → `NativeStream.Writer`;
  impact + lingering jobs → stream writer + `IJobEntityChunkBeginEnd`, push streams.
- `004-tests.md` — repoint `QueueStackHit`/`QueueDirectHit` onto `InjectTestHits`.

## Dependencies
001 → (002, 003) → 004. 002 and 003 are independent of each other.
