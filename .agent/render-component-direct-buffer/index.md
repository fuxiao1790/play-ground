---
name: render-component-direct-buffer
description: Merge render components into GPU-shaped struct, eliminate scatter+copy, write directly to buffer via LockForWrite
---

# Render Component Direct Buffer Write

## Summary

Collapse `CombatRenderComponent` (UV data) and `CombatRenderElement` (matrix) into a single struct that exactly mirrors the GPU buffer layout. This eliminates:
- The scatter loop that filters and builds a NativeList intermediate
- The SetData copy from NativeList to GPU buffer
- The artificial separation of "static UV" and "dynamic matrix" data
- Separate handling of projectiles vs AOEs in rendering

The new flow:
1. `CombatRenderPrepareSystem` writes the unified component every frame (matrix + UV data) for **all** entities (enabled and disabled)
2. Disabled entities get `renderTypeId = 0` (shader skips them, rendering a 0×0 quad)
3. `CombatBatchedRenderSystem` locks the GPU buffer and writes component data directly with zero intermediate copies

## Constraints & Invariants

### Data-flow and ownership (from code inspection)
- `CombatRenderComponent` is copied onto entities at spawn time by apply plumbing; its UV fields are static (computed once, never changed)
- `CombatRenderElement` is written every frame by `CombatRenderPrepareSystem` via `CombatRenderMatrixUtility.ElementFor(...)`
- `CombatBatchedRenderSystem` must write ALL entity data to GPU buffer every frame (no delta optimization; from user direction)
- Disabled entities are currently filtered out during scatter; new model must handle them via shader (renderTypeId = 0)

### Concurrency/threading (from code inspection)
- `CombatRenderPrepareSystem` runs OrderFirst, `PresentationSystemGroup` (main thread, Burst IJobChunk)
- `CombatBatchedRenderSystem` runs in `PresentationSystemGroup` after prep; calls `CompleteDependency()` before scatter
- After refactor: prep writes (W), batched reads (R) → batched must still `CompleteDependency()` before locking

### GPU buffer contract (from combat-render-system.md)
- Stride **96 bytes** for `Matrix4x4` (64) + `Vector4 uvOriginU` (16) + `Vector4 uvV` (16)
- Shader expects `_InstanceData[SV_InstanceID]` to have `objectToWorld`, `uvOriginU`, `uvV`
- Critical: `Material.SetBuffer` (not MPB), column-major matrix, correct stride assertion

### Entity count (from code inspection)
- Prep query currently filters to `CombatRenderActiveTag` enabled entities only
- Batched system counts active vs total separately (`LastActiveProjectileCount`, `LastActiveAoeCount` for telemetry)
- New model: write ALL entities (active + inactive), render-skip via renderTypeId; counts remain for telemetry

## Mechanisms Reused vs. Introduced

### Reused
- `CombatRenderMatrixUtility.ElementFor(...)` continues to compute the world matrix (no change to the math)
- `CombatRenderResourceRegistry` continues to compute UV basis at sprite registration (no change)
- `CombatRenderActiveTag` as enableable component (no change to activation logic)
- `CombatIndirectRenderFeature` and `DrawMeshInstancedIndirect` (no change to render feature)

### Changed
- **Component struct shape**: Merge `CombatRenderComponent` + `CombatRenderElement` into a single unified struct matching GPU layout
- **Prep system job**: Iterate ALL entities (not just enabled), set renderTypeId=0 for disabled
- **Batched system scatter**: Replace loop + NativeList + SetData with direct `LockForWrite` → sequential write → `UnlockBufferAfterWrite`

### Removed
- `CombatInstanceData` (the separate GPU struct; the component IS the GPU data now)
- `CombatRenderElement` (merged into unified component)
- The scatter loop and its filtering logic

## Design Validation

**Constraint: stride match (96 bytes)**
- New struct: `Matrix4x4 objectToWorld` (64) + `Vector4 uvOriginU` (16) + `Vector4 uvV` (16) = **96 bytes** ✓
- renderTypeId is NOT part of the GPU struct (not sent to GPU)
- Disabled entities handled by prep system: write a degenerate matrix (scale=0, results in 0×0 quad)

**Constraint: all entities must be written every frame**
- Prep system currently filters `CombatRenderActiveTag` in query; must change to include all entities but iterate all (not just enabled)
- Set `renderTypeId` in uvV.z based on `useEnabledMask` / manual enabled check
- Buffer grows as needed (no pre-count step); write at index 0..count-1 sequentially

**Constraint: concurrency**
- Prep writes component (W), batched reads component (R) then locks buffer → locks happen after `CompleteDependency()`
- No data race; no change needed

**Constraint: enableable filtering**
- Current: `activeMask[i]` in scatter loop; new: disabled entities written with scale=0 matrix
- GPU rasterizer naturally culls 0×0 quads (no special shader logic needed)
- Same effect (disabled don't render), cleaner mechanism (geometric culling, not semantic check)

## Minimal vs. Refactor Comparison

### Minimal/additive approach
- Add renderTypeId field to CombatInstanceData
- Modify scatter to write renderTypeId=0 for disabled
- Result: data flow unchanged, but filtering moved to shader
- Long-term cost: two component types still exist; two separate data paths (static UV on CombatRenderComponent, dynamic matrix on CombatRenderElement); scatter loop persists

### Refactor approach ✓ (chosen)
- Merge CombatRenderComponent + CombatRenderElement into unified struct
- Store renderTypeId in uvV.z (96-byte contract maintained)
- Replace scatter + NativeList + SetData with direct LockForWrite
- Prep system writes all entities (enabled/disabled) in one pass
- Result: single component type, single data source, zero intermediate copies
- Long-term benefit: simpler model, fewer allocations, better data locality, clearer ownership

**Decision**: Refactor. The unified struct removes a structural warning (two types for one concept), eliminates redundant copying, and clarifies that the component IS the GPU data. The prep system already has all the info; it should write it all directly.

## Pressure Test Summary

✓ Stride: renderTypeId in uvV.z, maintains 96 bytes  
✓ Concurrency: prep (W) → batched (R after CompleteDependency)  
✓ Enableable: renderTypeId=0 disables rendering (shader-side)  
✓ Entity count: all entities written; active count derived from prep output if needed  
✓ Projection scope: no change (unified struct local to this system)  

## Task List

1. [001-unified-render-component.md](001-unified-render-component.md) — Merge CombatRenderComponent + CombatRenderElement (exactly 96 bytes, GPU-shaped)
2. [002-prep-system-all-entities.md](002-prep-system-all-entities.md) — Write all entities; disabled get degenerate matrix (scale=0)
3. [003-batched-render-direct-buffer.md](003-batched-render-direct-buffer.md) — Replace scatter + SetData with LockForWrite direct write
4. [004-shader-rendertype-skip.md](004-shader-rendertype-skip.md) — (No changes; GPU naturally culls scale=0 quads)
5. [005-unify-projectile-aoe-query.md](005-unify-projectile-aoe-query.md) — Single query for projectile + AOE rendering

## Open Questions

- Should `LastActiveProjectileCount` / `LastActiveAoeCount` be preserved for telemetry? Optional (not critical path). Can be counted from enabled entities separately if needed.

## Dependencies

- No external dependencies; changes are contained to render system
- Shader change required (renderTypeId skip)
- Entity apply plumbing (spawn commands) must continue to populate UV fields; no change needed
