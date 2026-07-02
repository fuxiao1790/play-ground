# 003 — Prepare matrices into per-batch native buffers

**File:** `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs` (full rewrite)

## Target shape

Per-batch state owned by the prepare system:

```
private struct BatchState
{
    public EntityQuery Query;             // filtered to one batch id, never re-filtered
    public NativeList<Matrix4x4> Matrices;// Allocator.Persistent
}
private readonly Dictionary<int, BatchState> batches = new();
internal JobHandle PendingHandle;         // consumed by CombatBatchedRenderSystem
```

Cache RO handles for `CombatKinematicsComponent` and `CombatRenderComponent`
(update each frame).

## OnUpdate (main thread, cheap — then return without completing)

1. Resolve `CombatRenderResourceRegistry` (managed singleton).
2. For each `batchId` in `registry.Entries` not yet in `batches`, lazily create a
   `BatchState`:
   - Query = `new EntityQueryBuilder(Allocator.Temp)`
     `.WithAll<CombatKinematicsComponent>().WithAll<CombatRenderComponent>()`
     `.WithAll<CombatRenderActiveTag>().WithAll<CombatRenderBatchId>().Build(this)`,
     then `Query.SetSharedComponentFilter(new CombatRenderBatchId { Value = batchId })`
     **once** (never mutate afterward — this is what makes concurrent deferred
     jobs safe).
   - `Matrices = new NativeList<Matrix4x4>(0, Allocator.Persistent)`.
   - Note: no `ProjectileTag`/`AoeTag` — the batch id already implies the kind
     and the exact visual.
3. Update the RO handles.
4. For each `BatchState`:
   - `int count = state.Query.CalculateEntityCount();` (enabled count)
   - `state.Matrices.Clear(); if (state.Matrices.Capacity < count) state.Matrices.SetCapacity(count);`
   - if `count == 0` skip scheduling.
   - schedule `RenderPrepareJob` with `state.Matrices.AsParallelWriter()` via
     `ScheduleParallel(state.Query, Dependency)`; collect the handle.
5. `JobHandle combined = JobHandle.CombineDependencies(handles);`
   `Dependency = combined; PendingHandle = combined;` — **do not Complete**.
   (Assigning to `Dependency` chains the RO reads; `PendingHandle` guards the
   non-ECS native lists for the consumer, mirroring
   `ProjectileSpawnExpansionSystem.PendingHandle`.)

Use a reusable `NativeList<JobHandle>` (Temp) or a small stack list for the
handles; combine into `combined`.

## RenderPrepareJob (IJobChunk, BurstCompile)

```
[ReadOnly] ComponentTypeHandle<CombatKinematicsComponent> Kinematics;
[ReadOnly] ComponentTypeHandle<CombatRenderComponent> RenderComponents;
NativeList<Matrix4x4>.ParallelWriter Matrices;

Execute(chunk, ..., useEnabledMask, chunkEnabledMask):
    kin  = chunk.GetNativeArray(ref Kinematics);
    rend = chunk.GetNativeArray(ref RenderComponents);
    var e = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
    while (e.NextEntityIndex(out int i))
        Matrices.AddNoResize(CombatRenderMatrixUtility.MatrixFor(kin[i], rend[i]));
```

## Lifecycle

- `OnDestroy`: `PendingHandle.Complete();` then dispose every
  `BatchState.Matrices` (queries are owned by the system and released with it).
- Add an `ECS Lifecycle:` comment: prepare owns per-batch persistent matrix
  buffers; produced each frame, guarded by `PendingHandle`, disposed in
  `OnDestroy`.

## Acceptance criteria

- Only one `RenderPrepareJob`; it references no `CombatRenderElement`.
- Buffers contain exactly the enabled entities per batch (== `CalculateEntityCount`).
- Prepare returns without completing the job (overlap preserved).
- No leak-detector / job-safety errors on domain reload.

## Dependencies

Depends on 001 (`MatrixFor`). Consumed by 004.
