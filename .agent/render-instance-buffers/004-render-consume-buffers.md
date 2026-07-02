# 004 — Render consumes the buffers (zero-copy submit)

**File:** `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Changes

1. **OnCreate**: keep creating the `CombatRenderResourceRegistry` singleton.
   Delete `projectileRenderQuery` / `aoeRenderQuery` (no longer needed). Cache the
   prepare system:
   `prepareSystem = World.GetExistingSystemManaged<CombatRenderPrepareSystem>();`
2. **OnUpdate**:
   - `prepareSystem.PendingHandle.Complete(); prepareSystem.PendingHandle = default;`
   - `CompleteDependency();` (still fine)
   - reset `LastActiveProjectileCount = 0; LastActiveAoeCount = 0;`
   - for each `KeyValuePair<int, CombatRenderResourceEntry> pair in registry.Entries`:
     - look up the prepare system's `BatchState` for `pair.Key`; if absent or the
       list is empty, continue.
     - `NativeArray<Matrix4x4> matrices = state.Matrices.AsArray();`
     - add `matrices.Length` to `LastActiveProjectileCount` or `LastActiveAoeCount`
       based on `pair.Value.Kind`.
     - `SubmitAll(matrices, pair.Value);`
3. **SubmitAll**: change the parameter to `NativeArray<Matrix4x4> elements` and
   pass it straight to `Graphics.RenderMeshInstanced(rp, resources.Mesh, 0,
   elements, count, start)` in the existing 1023-slice loop. Delete the
   `ToComponentDataArray` calls entirely.

Expose the prepare `BatchState` lookup: add an `internal bool TryGetMatrices(int
batchId, out NativeList<Matrix4x4> matrices)` (or expose the dictionary) on
`CombatRenderPrepareSystem` so render does not reach into private fields.

## Acceptance criteria

- No `ToComponentDataArray` and no `CombatRenderElement` in this file.
- Rendered output (position, velocity-align, scale, z-order) is identical to
  before for both projectiles and AOEs.
- `LastActiveProjectileCount` / `LastActiveAoeCount` match per-batch buffer
  lengths and the old `CalculateEntityCount` values.

## Dependencies

Depends on 003 (`PendingHandle`, `BatchState`/`TryGetMatrices`) and 001 (`Kind`).
