# 001 Uniform Parallel Dead-Slot Apply Mechanism

## Goal

Define the shared parallel apply mechanism used by all three domains: partition
commands per chunk, reuse disabled-`Active` slots in a parallel `IJobChunk`, and
cold-create the remainder via ECB. Establish it once so 002/003 are thin
per-domain applications.

## Scope

- Command handoff: expansion emits a flat per-domain `NativeList<Command>` (or
  keeps the current AOE `NativeStream` drained to a flat array). Apply owns
  laning.
- Chunk capture: at apply time,
  `NativeArray<ArchetypeChunk> chunks = deadSlotQuery.ToArchetypeChunkArray(Allocator.TempJob)`.
  `M = chunks.Length`. No structural change occurs before the post-job ECB
  playback, so `chunks` stays valid through the reuse job. Dispose after.
- Worker count and chunk sets: `W = clamp(targetWorkers, 1, M)` (target ≈ job
  worker threads). Partition `chunks` into `W` contiguous ranges; worker `w` owns
  `chunks[range_w]`. Pass the ranges (offset+count per worker) to the job.
- Lane build: create a command `NativeStream` with `W` lanes and scatter the
  drained command **indices** across lanes (block or round-robin). Never copy the
  large command payload here; the reuse/cold path reads `commands[index]` once.
- Parallel reuse job (`IJobParallelFor` over `W`):
  - `Execute(int w)`: read lane `w` from the stream reader; get worker `w`'s chunk
    range.
  - For each chunk in the range, walk entities via `chunk.GetEnabledMask<Active>`
    + this-chunk type handles; for each disabled-`Active` slot, consume the next
    command index from lane `w`, write components, flip `Active` (and other
    enableable state) on. Move to the next chunk in the range when the current one
    is full; stop when the lane is exhausted.
  - After sweeping all its chunks, push any unconsumed lane-`w` command indices to
    a remainder container.
- Remainder container: a `NativeList<int>.ParallelWriter` (capacity = command
  count) or `NativeQueue<int>.ParallelWriter` of overflow command indices.
- Cold-create: after the job, main thread iterates the remainder and
  `ecb.CreateEntity(archetype)` + reset per command; play back once if non-empty.
- Counters: reuse = totalCommands − remainderCount; cold = remainderCount. Keep
  the existing profiler counter names per domain.

## Mapping Detail (pinned)

- 1 lane : 1 worker : many chunks. Lane count = `W` = worker count, independent of
  pool size. Each worker owns a disjoint contiguous range of `chunks`.
- Drive iteration by worker index with `IJobParallelFor` over `W`. The worker owns
  its own chunk walk, so full chunks (all `Active` enabled) are just skipped over
  to the next chunk in its range — no chunk is dropped, and a command overflows
  only when the worker's entire range is full.
- Each worker accesses its chunks' components with `ComponentTypeHandle<T>` via
  `chunk.GetNativeArray(ref handle)` and `chunk.GetEnabledMask(ref handle)`, same
  as an `IJobChunk` body.

## Safety Notes

- Workers own disjoint chunk ranges, so no two workers write the same chunk; type
  handles need no `[NativeDisableContainerSafetyRestriction]`. Remove the
  attribute usage that existed only for the shared claim counter.
- Type handles are read-shared across workers but each addresses disjoint chunks;
  this is the standard parallel-chunk-write pattern and is safe.
- Only shared writes are the remainder `ParallelWriter` and post-job counter math.

## Acceptance Criteria

- A reusable, documented pattern (helper or clearly duplicated structure) exists
  for: partition table build, parallel chunk reuse, remainder collection, ECB
  cold-create.
- No shared claim cursor; job is `ScheduleParallel`.
- Command payload copied at most once.

## Dependencies

Depends on `.agent/unify-combat-archetypes/` (one archetype per domain).

## Complexity

Medium.
