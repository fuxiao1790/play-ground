# Spawn Apply Parallel Reuse Plan

## Summary

Simplify and parallelize the spawn apply step for all three combat domains
(projectile, impact AOE, lingering AOE). Each domain's apply becomes a single
parallel job over its disabled-slot query: each worker reads its own command
stream partition and reuses the disabled-`Active` slots in its own chunk(s),
independently. Commands a worker cannot place (more commands than free slots in
its chunks) are collected as a remainder and cold-created via ECB after the job.

Uneven reuse is accepted by design: perfect slot packing is not a goal. The
tradeoff is a slightly larger resident pool and more cold-create structural
changes under imbalance, in exchange for cursor-free parallel apply.

Target pipeline:

    spawn event  --(NativeQueue, multi-producer)-->  expansion
    expansion    --(per-domain command stream)-->    apply
    apply        --parallel dead-slot reuse-->        reuse + ECB remainder

## Prerequisite

Depends on the three-archetype refactor in
`.agent/unify-combat-archetypes/`. That work gives each domain exactly one
reusable archetype and one disabled-slot query, which is what lets apply drop
per-variant bucketing and schedule a single parallel reuse job per domain. Do not
start this plan until projectile/impact/lingering each have one archetype.

## Problem With Current Apply

- Reuse is serial. Both projectile and AOE apply schedule the reuse job with
  `.Schedule` (not `ScheduleParallel`) and share a single
  `NativeReference<int> ClaimedCount` across the whole query, marked
  `[NativeDisableContainerSafetyRestriction]`. One worker walks all chunks and
  claims commands off one counter. Source:
  `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`,
  `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`.
- The shared claim cursor is the reason the job cannot be parallel: two workers
  incrementing the same command cursor would need atomics and still contend.
- This shows up in busy-scene frame cost. See
  `.agent/idle-combat-system-cost/`.

## Design

### Event hop stays a queue

Keep event collection as a multi-producer `NativeQueue<SpawnEvent>` (plus the
existing scope `DynamicBuffer` for authored events). Events are enqueued from many
systems at unpredictable counts; that is what a queue is for. Do not convert the
event hop to a `NativeStream` — stream foreach-indices are awkward to write from
arbitrary producers.

### Command hop is a multi-lane stream

The command→apply hop is a multi-lane `NativeStream`: **one lane per worker**.
Apply picks a worker count `W`; each worker owns exactly one lane and a disjoint
set of dead-slot chunks (1 lane : 1 worker : many chunks). This is the pinned
mapping.

### Apply is one parallel job per domain

- Capture the dead-slot chunks at apply time:
  `NativeArray<ArchetypeChunk> chunks = deadSlotQuery.ToArchetypeChunkArray(...)`,
  `M = chunks.Length`. No structural change happens before the ECB playback, so
  `chunks` stays valid through the job.
- Choose `W = clamp(targetWorkers, 1, M)` (target ≈ job worker threads). Partition
  `chunks` into `W` contiguous ranges; worker `w` owns `chunks[range_w]`.
- Build the command `NativeStream` with `W` lanes and distribute the drained
  commands across lanes (block or round-robin). Distribute command **indices**,
  not the large command payloads (matching the projectile index-permutation
  approach), so the big struct is copied once by the reuse/cold path.
- Schedule `IJobParallelFor` over `W`. Worker `w` reads lane `w` and sweeps its
  chunk range, greedily filling disabled-`Active` slots across all of its chunks
  from lane `w`. No shared claim counter; each worker has its own lane and its own
  chunks.
- Overflow — worker `w`'s entire chunk set is full before its lane is exhausted —
  pushes the remaining lane-`w` command indices to a parallel remainder container.
- After the job, the main thread cold-creates the remainder via ECB.
- Reuse count = total commands − remainder count; cold count = remainder count.

### Why worker-owns-chunk-set, not one lane per chunk

- A worker fills across every chunk it owns, so a command overflows only when that
  worker's whole set is full. Binding a lane to a single chunk would overflow a
  command the moment its one chunk filled, even with free slots next door — far
  more waste.
- `W` lanes (worker count), not one per chunk, means less stream/scatter overhead
  and a lane count independent of pool size.
- `IJobParallelFor` over `W` visits every worker, so no lane is dropped: a
  fully-packed worker overflows its whole lane to ECB rather than losing commands.
  The worker owns its chunk walk, so there is no chunk-skipping to worry about.

### Optional waste control (deferred)

If profiling shows excessive cold-create under imbalance, add a one-pass popcount
of each chunk's disabled-`Active` mask so apply can size each chunk's command
slice to its real free capacity — near-optimal packing, still parallel and
cursor-free. Not built up front.

## Constraints And Invariants

- Reuse writes go directly to chunk component arrays (fast path); only the
  remainder uses ECB. Do not route reuse through ECB.
- Each entity is written by exactly one worker (its owning chunk), so component
  handles need no `[NativeDisableContainerSafetyRestriction]`. The only shared
  writes are the remainder container (`ParallelWriter`) and post-job counters.
- Command payloads are copied at most once (index permutation, not payload sort).
- Apply still resets every optional/enableable state per the unify plan
  (`Active`, collision-active, render-active, tracking, lifetime, timed-spawn),
  regardless of reuse vs cold-create.
- Domain separation and pool disjointness from the unify plan are unchanged: each
  apply job runs against exactly one domain's disabled-slot query.

## Design Validation

- Parallelism: pass. Per-chunk partitions are independent; no shared cursor.
- Safety: pass, and simpler than today. No cross-chunk writes; remainder via
  `ParallelWriter`.
- Reuse correctness: pass if the chunk-local loop fills only disabled-`Active`
  slots and flips them active, identical to today's inner loop minus the shared
  counter.
- Lossiness: accepted. Resident pool converges to a larger steady-state size than
  perfect packing; overflow cold-creates fall as per-chunk capacity rises. Must be
  documented so it is not mistaken for a leak.
- Structural-change cost: bounded but pay-per-overflow. This is the main cost of
  lossy reuse; the optional popcount pass is the escape hatch if it bites.
- Determinism/order: needs verification (task 004). Component values are identical
  for reuse vs cold-create, so per-entity behavior is deterministic. Risk is only
  if a downstream consumer depends on entity/chunk processing order; confirm the
  hit-apply and VFX paths are order-independent.

## Task List

- [001 - Uniform parallel dead-slot apply mechanism](001-parallel-apply-mechanism.md)
- [002 - Projectile apply migration](002-projectile-apply-migration.md)
- [003 - AOE apply migration (impact and lingering)](003-aoe-apply-migration.md)
- [004 - Determinism and reuse/cold-create order safety](004-determinism-and-order.md)
- [005 - Tests and profiling](005-tests-and-profiling.md)
- [006 - Docs](006-docs.md)

## Resolved Decisions

- Command hop is a multi-lane `NativeStream`, **one lane per worker**, built at
  apply time. 1 lane : 1 worker : many chunks.
- Worker count `W = clamp(targetWorkers, 1, matchedChunkCount)`; each worker owns
  a disjoint contiguous range of the dead-slot chunk array and its own lane.
- Expansion emits a flat per-domain command list; **apply** builds the lane stream
  and the chunk array/ranges, because only apply knows the runtime chunk count.
- Iteration is `IJobParallelFor` over `W`. Each worker sweeps its chunk set,
  filling greedily; a command overflows only when the worker's whole set is full.
- Uneven remainder → ECB.

## Open Questions

- None blocking. The optional per-chunk free-slot popcount (near-optimal packing)
  stays deferred until profiling shows overflow cost matters.
