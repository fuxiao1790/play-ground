# 003 — CombatPoolCleanupSystem (the trimmer)

## Goal
At the very end of simulation, delete a small bounded batch of disabled pooled
entities, per reuse key, only when the frame has headroom and a batch is oversized.

## Placement
- New file `Assets/Scripts/System/Common/CombatPoolCleanupSystem.cs`.
- Managed `SystemBase` (wall-clock read + `DestroyEntity` are main-thread).
- `[UpdateInGroup(typeof(LateSimulationSystemGroup))]` — runs after every spawn-apply
  and `CombatLifetimeSystem`, so it never removes a slot this frame's reuse wanted.

## OnCreate
- Create `CombatPoolCleanupConfig` singleton from `Default` if absent (002).
- Build the queries:
  - `allPooledQuery` = `WithAll<CombatRenderBatchId>()` matching pooled combat entities
    (add `.WithAny<ProjectileTag, AoeTag>()` to be explicit). Used to enumerate chunks
    and classify active vs disabled per batch. Because `CombatRenderBatchId` is a
    shared component, every chunk belongs to exactly one batch id.
  - `disabledByBatchQuery` = `WithAll<CombatRenderBatchId>()` + `WithDisabled<Active>()`
    (mirrors the spawn dead-slot query shape) for the targeted gather of deletable
    entities per over-target batch.

## OnUpdate
1. **Both gates.**
   - `cfg = GetSingleton<CombatPoolCleanupConfig>()`, `clock = GetSingleton<CombatFrameClock>()`.
   - Gate A (EMA): if `clock.SmoothedFrameMs > 0 && clock.SmoothedFrameMs >= cfg.BudgetMs` → return.
     (While EMA is still warming up at `<= 0`, treat as "no data" and let gate B decide.)
   - Gate B (current frame elapsed):
     `elapsedMs = (Time.realtimeSinceStartupAsDouble - clock.FrameStartTime) * 1000`.
     If `elapsedMs >= cfg.BudgetMs` → return.
2. **Per-batch counts (single pass).** Iterate `allPooledQuery` chunks:
   - `batchId = chunk.GetSharedComponent(sharedHandle).Value`
   - `total = chunk.Count`; `active = popcount(chunk.GetEnabledMask(Active))`;
     `disabled = total - active`.
   - Accumulate into `NativeHashMap<int, int2>` batchId -> (activeSum, disabledSum).
   - This may run as a Burst `IJobChunk` writing the map, or inline main-thread; the
     population is small, so inline is acceptable for v1.
3. **Decide + delete (bounded).** `remaining = cfg.MaxDeletesPerFrame`;
   `sliceStart = now` (for optional `SliceMs` stop).
   For each batch in the map (see fairness note):
   - if `remaining <= 0` break.
   - `floor = max(cfg.RetentionTarget, ceil(active * cfg.PoolRatioMultiplier))`.
   - trim only if `disabled > cfg.RetentionTarget && disabled > active * cfg.PoolRatioMultiplier`.
   - `toDelete = clamp(disabled - floor, 0, min(cfg.PerBatchDeleteCap, remaining))`.
   - if `toDelete > 0`:
     - `disabledByBatchQuery.SetSharedComponentFilter(new CombatRenderBatchId { Value = batchId })`
     - `entities = disabledByBatchQuery.ToEntityArray(Allocator.Temp)`
     - `EntityManager.DestroyEntity(entities.GetSubArray(0, toDelete))`
     - `disabledByBatchQuery.ResetFilter()`; `remaining -= toDelete`.
   - if `SliceMs > 0 && (now - sliceStart)*1000 >= cfg.SliceMs` break.

The `floor` formula guarantees a single frame can never push a batch below the
retention/ratio line, so draining is gradual and hysteretic (no thrash between
delete and re-spawn).

## Fairness (optional, recommended)
Keep a rotating start offset (`_nextBatchStart` field) into the batch-id set so
successive frames trim different batches first when the per-frame cap is hit. Without
it, always-first ordering could starve later batches. Low priority; note in code.

## Efficiency note
The single count pass is O(pooled chunks) and only proceeds when the frame is calm.
The per-batch filtered gather runs only for the (few) over-target batches actually
being trimmed, not every registered type — so the trimmer does not reintroduce the
per-type scan cost the investigation flagged in the render system.

## Acceptance criteria
- With gates failing (high `SmoothedFrameMs` or high `elapsedMs`), zero deletions.
- With gates passing and an oversized batch, deletes `<= PerBatchDeleteCap` for that
  batch and `<= MaxDeletesPerFrame` total, never below `floor`.
- Batches within retention/ratio are untouched.
- Deleting the last entities of a batch frees its chunks (verify pooled chunk count
  drops in the Entities window / via `CalculateChunkCount`).

## Dependencies
- 001 (`CombatFrameClock`), 002 (`CombatPoolCleanupConfig`).

## Scope
Medium — one system (~120 lines), gating + one chunk pass + bounded delete.
