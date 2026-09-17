# 005 — Implement GPU key-to-strip allocator

Scope: `ProjectileTrailAllocator.compute` and per-graph persistent GPU state.
Complexity: high. Dependencies: 001, 004.

Work:

1. Create a fixed-size, power-of-two open-addressed table with full 64-bit
   key comparison. Entry states: Empty, Reserved, Active, Closing, Tombstone.
   Claim via atomic compare-exchange; publish key and slot before Active state.
   A key group owns all commands for that key in one dispatch. Probe limit is
   table capacity; no two full keys alias because their hashes match.
2. Initialize free-slot stack `0..MaxStrips-1` and count once at resource
   creation. Pop/push with bounded atomic compare-exchange; no wraparound or
   occupied-slot overwrite. Store per-slot key/map entry, final sequence,
   closing flag, last point visual time, and release time.
3. Run a retirement kernel on every presentation frame. Release Closing slot
   after graph point lifetime plus verified initialization margin. Mark its
   map entry Tombstone, return slot to free stack, clear state. Reuse
   tombstones on insertion. Track probe length; rebuild/compact table on GPU
   during calm period if tombstone accumulation degrades lookup cost.
4. Run one thread per sorted key group; process its commands in ascending
   sequence. `Begin` inserts only if absent; `Append`/`End` require an Active
   mapping. Duplicate Begin, stale sequence, missing key, and End-after-End
   produce invalid outputs. `End` can output one final point before marking
   Closing. A new trail with no free slot produces invalid output for its
   entire lifecycle; no later Append allocates it mid-flight.
5. Write `ResolvedStripIndices[request]`, `TrailPositions[request]`, and
   `TrailWidths[request]` in current-batch order. Default resolved indices to
   `uint.MaxValue` before each dispatch so skipped/malformed commands cannot
   accidentally target strip 0. Keep command and result staging private to
   this batch; keep hash/free/strip state persistent across batches.
6. Add GPU counters for failed Begin, unknown Append/End, malformed command,
   free-slot high-water mark, and maximum probe length. Read back only through
   throttled asynchronous diagnostics outside hot path; no blocking readback.

Acceptance:

- Same key resolves to same strip across frames; different live keys never
  share a strip.
- Ending trail keeps its slot until every possible point has died under the
  graph's fixed point lifetime contract; next Begin may reuse slot afterward.
- Allocator keeps retirement time aligned with VFX simulation while culled,
  paused, and during frame hitches.
- Full table and zero free slots degrade by dropping visuals, with no memory
  overwrite or CPU stall.
- Long churn does not make missing-key lookups unbounded in practice; probe
  diagnostics establish a threshold for GPU rebuild.

Tests: user runs **PlayMode** `ProjectileTrailGpuAllocatorTests`
(`ConcurrentKeys_GetDistinctStrips`, `End_KeepsSlotUntilTailDies`,
`CapacityExhaustion_DropsNewTrail`, `TombstoneChurn_PreservesLookup`,
`CulledGraph_DoesNotReuseLiveStrip`). Export XML under `Logs/`; agent reviews
it. Visual and profiler inspection also required for the GPU path.
