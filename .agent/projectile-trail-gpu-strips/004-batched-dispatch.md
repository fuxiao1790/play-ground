# 004 — Batch and dispatch trail commands

Scope: extend existing VFX dispatch lane, root resource, and graph contract.
Complexity: high. Dependencies: 002, 003; use ordering result from 001.

Work:

1. Add `NativeQueue<ProjectileTrailVfxEvent>` plus grow-only command list,
   per-graph bucket offsets, sorted group-start/count lists to
   `CombatAoeVfxDispatchSingleton`. Allocate/dispose with the existing shape
   containers. Include this queue in the no-work/root-missing logic.
2. In presentation, complete shared `ProducerHandle` once. Count-sort trail
   events by decoded local VFX id, preserving one contiguous slice per graph.
   Within each slice, sort by full 64-bit key then sequence. Build one group
   range per key. Reject duplicate sequence or an `End` preceding `Begin` as
   malformed visual input; count it and drop that key group.
3. Send sorted `ProjectileTrailGpuCommand` values directly to one structured
   graphics buffer per graph. Keep command buffer, resolved indices, positions,
   and widths as grow-only request staging; never use them as persistent
   particle storage. Initial capacity follows the existing resource policy.
4. Add `ProjectileTrailVfxResources` to `CombatVfxRoot` ownership and
   `CombatAoeVfxDispatcher` upload/validation. Allocate persistent GPU hash,
   free-slot, strip-state, and counters once per graph from its definition.
   Graph exposes only `TrailPositions`, `TrailWidths`,
   `ResolvedStripIndices`, `PointLifetime`, and `SpawnCount`. Compute-only
   command/map buffers stay private to resource and are not graph properties.
5. Each presentation frame: tick GPU retirement for every registered trail
   graph first, even when no trail requests exist. For each nonempty graph
   bucket, upload commands/group ranges, clear output validity to invalid,
   dispatch resolver, bind graph output buffers, set `SpawnCount`, and send one
   `OnSpawn`. Respect ordering/fence result from task 001. Do not send one
   event per projectile.
6. Release every buffer and GPU state on graph resource disposal. If request
   buffer grows, preserve persistent allocator state while replacing only
   transient staging. Root reset disposes and clears all mappings together.

Acceptance:

- Arbitrary NativeQueue dequeue order gives the same per-key command order.
- Two projectile trail graphs have independent key maps and budgets.
- One graph shared by multiple skills still gets one instance and one
  `OnSpawn` batch; no later batch changes old points.
- GPU cleanup progresses with an empty CPU request queue.
- A graph with an unexpected extra exposed `GraphicsBuffer`, missing output
  buffer, or wrong `PointLifetime` type fails registration.
- Runtime hot path has no per-request managed allocation or CPU readback.

Tests: user runs **EditMode** `ProjectileTrailDispatchTests`
(`BucketAndSort_OrdersByGraphKeySequence`,
`MalformedGroup_DropsWithoutAffectingOtherGroups`) and **PlayMode**
`ProjectileTrailDispatchIntegrationTests`
(`SharedGraph_ReceivesOneBatch`, `EmptyQueue_StillRetiresStrips`). Export XML
under `Logs/`; agent reviews it.
