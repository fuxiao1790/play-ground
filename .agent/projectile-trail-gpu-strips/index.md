# GPU-owned projectile trail strips

## Summary

Add `VfxDataShape.ProjectileTrail = 3` for stateful projectile ribbons. Keep
`LineSegment = 2` for targeted chain links. Migrate projectile trail authoring and
the two existing projectile trail graphs to the new shape; do not maintain two
projectile trail emission paths. ECS sends `Begin`, `Append`, and `End` commands
with a unique logical `TrailKey`. One compute allocator per registered graph
resolves each key to a physical VFX `stripIndex`. The VFX Graph receives one
batched `OnSpawn` per graph and samples a compute-written index during
`Initialize Particle Strip`. CPU never allocates or recycles strip slots.

This plan covers runtime integration and editor authoring. No graph or code has
been changed by this plan.

## Architecture decisions

1. **Identity:** allocate a nonzero, monotonically increasing `ulong TrailKey`
   in the single-threaded `ProjectileSpawnExpansionJob`, only for commands with
   a `ProjectileTrail` VFX id. Store next key in the expansion singleton as a
   persistent native reference. Copy key through `ProjectileSpawnCommand` into
   `ProjectileTrailVfxComponent` on every cold create and pool reuse. Reject
   counter wrap. `ProjectileId` is unsuitable: root counters are independent and
   child IDs can be hashes (`ProjectileSpawnExpansionSystem.ProjectileIdFor`).
2. **One visual command path:** existing `ProjectileTrailVfxComponent` owns key,
   next sequence, started flag, width, step distance, and last emitted position.
   `ProjectileMovementSystem` emits `Begin` at spawn position on first unarmed
   movement, then distance-gated `Append`. Projectile lifetime and both collision
   death paths emit one final `End` before disabling `Active`. No separate
   `LineSegment` projectile branch remains. A projectile dying before movement
   emits no commands. `End` includes final position, so trail reaches death
   location even below step distance.
3. **Per-graph authored contract:** introduce `ProjectileTrailVfxDefinition`
   containing graph asset, `MaxStrips`, `ParticlesPerStrip`, and
   `PointLifetimeSeconds`. `BasicAttackPrefab` references this definition in its
   trail slot; remove its projectile trail shape selector. `SkillDriver`
   registers it through a shape-specific overload. Root rejects two definitions
   using the same graph with different settings. The common compute shader is
   assigned once on `CombatVfxRoot`. Graph static strip counts must match the
   definition; authoring validation checks this when graph inspection is
   available. `PointLifetimeSeconds` is set on the graph by registration and is
   the sole point lifetime input; graph must not extend particle lifetime.
4. **Transport:** add one native queue for `ProjectileTrailVfxEvent` and reuse
   the existing `ProducerHandle`. Bucket by encoded VFX id, then sort each graph
   slice by `(TrailKey, Sequence)`. Keep CPU event as `{ VfxId, GpuCommand }`;
   `GpuCommand` has a 32-byte structured-buffer stride, divisible by 16, with
   key low/high, sequence, kind, position, width, padding. The bucket job copies
   its nested `GpuCommand` into a grow-only sorted list. This is the only CPU
   translation. Build key-group ranges in the same sorted pass. One upload per
   graph, one compute resolution, one `OnSpawn`.
5. **GPU ownership:** persistent per-graph compute buffers hold a full-key hash
   table, free-slot stack, and strip state. One thread processes each key group
   sequentially; different key groups can run in parallel. `Begin` claims a
   free slot, `Append` looks it up, and `End` marks it closing. Compute writes
   `ResolvedStripIndices`, `TrailPositions`, and `TrailWidths` for the current
   batch. Invalid resolution writes `uint.MaxValue`. VFX Graph 17.4 init source
   returns before reserving a strip point when `stripIndex >= STRIP_COUNT`.
6. **Safe recycling:** compute cleanup runs every presentation frame, including
   zero-command frames. It releases a closing slot only after its final point's
   `PointLifetimeSeconds` has elapsed in the same visual time domain **and** a
   verified VFX initialization latency margin has passed. Require graph to
   simulate while culled, or pause allocator time with it. This is a strict
   upper-bound lifetime contract, not a CPU prediction or live-particle query.
   Stock VFX Graph does not expose its internal strip alive counter to this
   external allocator. Changing graph lifetime without changing the definition
   is a contract violation. A failed `Begin` drops that trail until its `End`;
   no occupied slot is stolen.

## Constraints and invariants

| Invariant | Source | Design check |
|---|---|---|
| VFX requests are visual-only; simulation jobs do not call managed VFX objects. | [VFX request contract](../../Docs/contracts/vfx-requests.md), [presentation layer](../../Docs/layers/presentation-and-feedback.md) | Producers write native command queue; presentation owns compute and graph. |
| Shared graph has one `VisualEffect`; one `OnSpawn` batch per graph per frame. | [VFX system](../../Docs/reference/simulation/vfx-system.md), [root](../../Assets/Scripts/System/Vfx/CombatVfxRoot.cs) | Resolver processes full graph bucket, then sends one event. |
| Upload buffers are transient; old particles must use copied attributes. | [shared-buffer incident](../../Docs/reference/simulation/vfx-shared-graph-area-size-corruption.md) | Only Initialize samples position, width, resolved index; Update/Output use particle attributes. |
| NativeQueue producer order is unspecified; jobs share one producer handle. | [ECS notes](../../Docs/reference/simulation/ecs-notes.md), [dispatch system](../../Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs) | Explicit per-key sequence and sort; all handles complete before bucket drain. |
| Projectile pool reuse rewrites component state, and spawn apply follows movement/collision. | [phase order](../../Docs/architecture/phase-order.md), [spawn apply](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs) | New key and unstarted state copied on every activation; first `Begin` next movement tick. |
| Lifetime can kill before movement; collision has separate discrete and continuous death calls. | [lifetime system](../../Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs), [collision systems](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs) and [continuous collision](../../Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs) | Both paths call one trail-end helper before `Active` is disabled. |
| VFX Graph strip count and per-strip point count are fixed; invalid strip index exits before point reservation. | Installed `com.unity.visualeffectgraph@17.4.0` `VFXBasicInitialize.cs`, `VFXInit.template`, `UpdateStrips.compute` | Definition/graph capacity validation and `uint.MaxValue` overflow sentinel. |
| High-count combat paths avoid per-frame managed allocation and need visual budgets. | [coding standards](../../Docs/coding-standards.md), [performance](../../Docs/performance.md) | Grow-only native/GPU staging; fixed strip budget; drop visual trail on overflow; profile sort, upload, compute, strip capacity. |

## Mechanisms reused versus introduced

Reuse encoded `VfxId`, `VfxDataShapeTable`, the shared native VFX queue/handle,
counting-sort graph buckets, one root-owned resource per graph, dispatcher
registration validation, and transient buffer-to-particle-attribute copying.
Introduce one shape because stateful keyed points cannot fit `LineSegment`'s
start/end/width contract. Introduce one compute allocator because VFX Graph
does not expose a safe persistent key map or free-slot stack. Definition asset
provides one owner for per-graph lifetime and capacity settings.

## Additive versus refactor comparison

| | Minimal/additive | Chosen refactor |
|---|---|---|
| Resulting flow | Existing projectile `LineSegment` path and new keyed strip path coexist. | Every projectile trail uses keyed `ProjectileTrail`; `LineSegment` serves targeted links. |
| Concepts/types | New shape, GPU allocator, plus old trail shape selector and old projectile segment producer. | New shape, definition, allocator; remove projectile selector and segment producer. |
| Copies/translations | Two producer routes and two authored contracts must stay aligned. | One command queue; bucketing directly produces GPU command layout. |
| Long-term cost/benefit | Ambiguous authoring, duplicated validation and preview behavior. | Clear owner and one projectile trail lifecycle; chain links retain simple shape. |

Decision: **refactor**. New type describes different lifetime semantics, while
the old projectile `LineSegment` pathway would duplicate the same domain
concept. No automatic fallback to `LineSegment` on allocator failure.

## Design validation and gates

- **GPU index input:** installed VFX Graph source exposes dynamic `stripIndex`
  on Initialize; `VFXInit.template` rejects out-of-range index before point
  reservation. Validate a real authored graph through AgentVFX when editor
  bridge becomes reachable. Bridge currently reports no reachable server.
- **Scheduling:** verify compute writes to graph sample buffers become visible
  before the same frame's `OnSpawn` Initialize, on target graphics APIs. Do not
  ship until a PlayMode visual probe proves this. Keep batch buffers alive long
  enough for graph initialization; use a graphics fence or buffered staging if
  same-frame ordering is not guaranteed.
- **Lifetime:** `PointLifetimeSeconds` must bound every point, including alpha
  animation. VFX Graph must simulate while culled, or allocator and graph must
  share pause/advance behavior. Include at least one completed VFX init/update
  cycle after final point before reuse. Verify slot reuse visually after End.
- **Capacity:** `MaxStrips` must not exceed graph strip count. Size
  `ParticlesPerStrip` for maximum accepted points during point lifetime; stock
  strip init rejects a point when its strip is full. Budget overload drops
  whole new trail; partial point loss requires a counter and profiling.
- **Hash table:** full 64-bit key comparison, collision-safe atomic claim,
  tombstone reuse, bounded probe count, and no eviction of active keys. Sweep
  tombstones or rebuild on GPU during calm periods if probe cost grows.
- **No-request frames:** cleanup still ticks when request queue is empty. Root
  dispose/reset releases buffers and invalidates mappings together.

## Tasks

1. [001-verify-vfx-strip-bridge.md](001-verify-vfx-strip-bridge.md) — prove GPU-resolved strip index and dispatch ordering.
2. [002-shape-and-authoring.md](002-shape-and-authoring.md) — add shape contract and single per-graph definition.
3. [003-projectile-command-lifecycle.md](003-projectile-command-lifecycle.md) — allocate unique keys and emit complete Begin/Append/End lifecycle.
4. [004-batched-dispatch.md](004-batched-dispatch.md) — bucket/sort/upload one trail command batch per graph.
5. [005-gpu-allocator.md](005-gpu-allocator.md) — implement GPU key map, slot budget, retirement, and counters.
6. [006-graph-and-preview.md](006-graph-and-preview.md) — author strip graphs and use dispatcher in preview.
7. [007-validation-and-docs.md](007-validation-and-docs.md) — regression tests, performance checks, contract docs.

## Open dependencies

- AgentVFX bridge unavailable during planning; graph-specific node/settings
  inspection awaits a reachable Unity editor. Never inspect `.vfx` text.
- Same-frame compute-to-VFX ordering and initialization latency need the
  bounded validation in task 001 before implementation relies on them.
- Exact `MaxStrips`, `ParticlesPerStrip`, `PointLifetimeSeconds`, and visual
  overload target are content/performance tuning values; plan defines their
  ownership, not arbitrary production numbers.
