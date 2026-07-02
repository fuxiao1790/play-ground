# Rendering Rework — Decouple Render Batch Id from Spawn Pooling

## Summary

`CombatRenderBatchId` is an `ISharedComponentData` that is doing **two unrelated
jobs at once**:

1. **Render batching** — the filter `CombatBatchedRenderSystem` uses to group
   instanced draws by GPU resource (mesh/material).
2. **Spawn pool partitioning** — because a shared component forces chunk
   separation and its value cannot change without a structural move, the entity
   reuse pool is bucketed by batch id, and reuse only claims dead slots whose
   batch id already matches the command's `RenderTypeId`.

This dual role is the coupling that makes render changes break spawning (and vice
versa). This plan **removes the coupling** by converting `CombatRenderBatchId`
from `ISharedComponentData` to a plain `IComponentData` int, so:

- Spawn pooling no longer partitions by batch id; reuse claims *any* dead slot of
  the right archetype and **writes** the new batch id like any other field.
- Rendering no longer filters by shared component or calls `ToComponentDataArray`;
  it scatters matrices into per-batch buffers by reading the plain int.

Dependency background is in [context.md](./context.md).

## Scope & priorities (per user)

- **Priority: remove the coupling.** Structural decoupling first.
- **Render performance may regress temporarily.** The submit path is rewritten to
  a simple, obviously-correct **main-thread scatter**. The parallel
  count/prefix-sum/scatter optimization is explicitly deferred (see Follow-ups).
- Keep the change **small on the fragile spawn path** — do not also rip out the
  counting-sort bucketing in this pass, even where it becomes degenerate.

## Constraints & invariants the change must respect

- **Reuse writes must be Burst-safe.** Reuse jobs write components via
  `ComponentTypeHandle` with `[NativeDisableContainerSafetyRestriction]`
  ([BasicProjectileSpawnJob](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L467), [AoeSpawnJob](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L418)). The new batch-id write conforms to this same pattern.
- **Render read happens after simulation completes.** `CombatBatchedRenderSystem`
  calls `CompleteDependency()` before touching data on the main thread
  ([CombatBatchedRenderSystem.cs:45](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L45)). Preserved.
- **Batch id source of truth = `RenderTypeId`** minted by
  `CombatRenderResourceRegistry` ([CombatRenderComponents.cs:49](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L49)). After this change the per-entity
  `CombatRenderBatchId` is a *copy* of that value, written at spawn — the registry
  remains the source of truth for resources.
- **Batch id must never be stale on a reused slot.** Previously guaranteed by the
  shared filter; now must be guaranteed by writing it on **every** spawn path
  (cold create + all reuse jobs). This is the central new invariant.
- **Archetype distinctions other than batch id must be preserved.** AOE reuse
  still distinguishes impact / lingering / timed-lingering via the dead-slot query
  structure, not batch id.
- **No per-frame GC in submit.** Per-batch scatter buffers are reused scratch,
  cleared each frame, not reallocated.

## Mechanisms reused vs. introduced

- **Reused:** the counting-sort / bucketed reuse machinery
  ([BucketCommandsJob](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L311), AOE `_byKey` buckets). We conform to it — drop a key
  dimension and add a component write — rather than replacing it.
- **Introduced:** a render-side per-batch scatter buffer that **replaces** (does
  not parallel) the deleted shared-filter + `ToComponentDataArray` path. Net data
  paths: unchanged (one old path removed, one new added).

## Minimal/additive vs. refactor comparison

- **Minimal/additive** (keep shared `CombatRenderBatchId`, add a second plain-int
  render key alongside):
  - resulting data flow: two batch-id representations kept in sync.
  - new concepts/types: a second batch-id component.
  - copies/translations added: sync between the two on every spawn.
  - long-term cost: **two sources of truth for one concept** — the exact
    structural warning this rework exists to remove. Rejected.
- **Refactor** (convert the single `CombatRenderBatchId` to plain `IComponentData`):
  - resulting data flow: one per-entity int; spawn writes it, render reads it.
  - existing types changed: `CombatRenderBatchId` kind; spawn reuse/cold paths;
    render submit.
  - copies/translations removed: the `AddSharedComponent` structural op at spawn,
    the shared-component chunk fragmentation, and the `ToComponentDataArray` gather.
  - long-term benefit: single source of truth; spawn and render independently
    changeable; better chunk packing.
- **Decision: refactor.** The concept (batch id = RenderTypeId) has one meaning;
  it must have one representation. The user has explicitly prioritized decoupling.

## Design validation against invariants

- *No stale batch id* → satisfied by adding an unconditional batch-id write to
  `RecordCommonProjectileReset`, `RecordAoeReset`, `BasicProjectileSpawnJob`,
  `ChildSpawnerProjectileSpawnJob`, `AoeSpawnJob`. Task 005 asserts it in tests.
- *Cross-batch reuse is semantically safe* → the only per-entity state keyed on
  batch id is the render resource lookup, which reads the freshly-written value;
  `CombatRenderComponent` visuals are reset from `cmd.Render`. No other component
  keys off batch id. Validated by inspection.
- *AOE archetype variants preserved* → dead-slot queries keep their
  lifetime/timed distinctions; only `SetSharedComponentFilter` is removed.
- *Render correctness* → every active renderable is scattered into the buffer for
  its current batch id; each registry entry draws its buffer. Enabled mask on
  `CombatRenderActiveTag` still gates.
- *Better chunk packing, no regression* → removing the shared component only
  merges chunks; no system relies on batch-id chunk partitioning except the two
  being rewritten.

## Default decision rule applied

Two representations of "batch id = RenderTypeId" (shared component vs. the
registry key) collapse toward **one** per-entity data representation sourced from
the registry. No compatibility/migration reason to keep the shared component.

## Task list

| # | Task | Depends on |
|---|------|------------|
| [001](./001-batchid-to-component.md) | Convert `CombatRenderBatchId` to `IComponentData` | — |
| [002](./002-projectile-spawn-decouple.md) | Projectile spawn: archetype + write batch id + drop shared filter | 001 |
| [003](./003-aoe-spawn-decouple.md) | AOE spawn: archetypes + write batch id + drop shared filter | 001 |
| [004](./004-render-submit-scatter.md) | Rewrite render submit to plain-int scatter | 001 |
| [005](./005-tests-migration.md) | Migrate tests off `AddSharedComponent`/`SetSharedComponentFilter` | 002-004 |
| [006](./006-docs-update.md) | Update render-batch-data contract + context notes | 002-004 |

Tasks 002–006 are individually reviewable but must land together (the codebase
does not compile/run correctly in a half-applied state). Treat as one PR.

## Open questions / considerations

- **Projectile bucketing becomes degenerate.** With `ProjectileSpawnKey` reduced
  to nothing, projectile reuse has a single bucket, so the counting-sort adds no
  value there. **Decision:** keep the machinery this pass (minimize spawn-path
  risk); flag counting-sort removal as a follow-up. Documented in task 002.
- **Two `CombatApplyBridge` classes** still exist
  ([CombatApplyFinalizeSingleSystem.cs:382](../../Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs#L382), [CombatApplyFinalizeSystem.cs:415](../../Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs#L415)). Unrelated to batch-id coupling but
  worth resolving separately; out of scope here.

## Follow-ups (explicitly deferred, not in this plan)

1. **Parallel render compaction** — replace the main-thread scatter with a Burst
   count → prefix-sum → parallel scatter into one packed `NativeArray<Matrix4x4>`
   with per-batch offsets; zero-copy `RenderMeshInstanced(..., start, count)`.
2. **Drop `CombatRenderElement`** — fold matrix math into the scatter so the
   per-entity matrix component (and its archetype membership) disappears entirely.
3. **Remove degenerate projectile counting-sort** now that batch id is not a key.
4. **Centralize the render component set** so archetype membership is declared
   once instead of in 5 builders (prevents future drift).
