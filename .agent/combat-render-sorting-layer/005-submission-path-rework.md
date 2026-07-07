# 005 - Submission Path Rework

## Scope

`Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`.

## Change

**`OnCreate`** ([CombatBatchedRenderSystem.cs:31-58](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L31-L58)):
- Remove the `SystemInfo.supportsIndirectArgumentsBuffer` guard
  (lines 38-43) — nothing issues an indirect draw anymore.
- Remove `_argsBuffer` creation (lines 45-48).
- Keep `_instanceData` `NativeList`, `renderQuery`, the 32-byte `Assert`.

**`OnDestroy`** ([CombatBatchedRenderSystem.cs:60-66](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L60-L66)):
- Remove `CombatIndirectRenderData.Clear()` and `_argsBuffer?.Dispose()`.

**`OnUpdate`** ([CombatBatchedRenderSystem.cs:68-104](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L68-L104)):
- Keep the early-outs, but replace `CombatIndirectRenderData.Clear()` calls
  (lines 75, 82) with telling the registry's baked mesh to draw zero quads
  (`SetSubMesh` with `indexCount = 0`) instead of clearing a static handoff —
  there is no handoff anymore; the `MeshRenderer` always exists once
  registered, so "nothing active" means "submesh index count 0", not "don't
  render".
- Keep `EnsureInstanceCapacity` exactly as-is (still governs `_instanceBuffer`
  growth) but also call the registry's mesh-capacity growth (002) with the
  same `entityCount`, so both buffers grow in lockstep.
- Replace `PopulateArgs(registry, entityCount)` +
  `Submit(registry, uvBasisBuffer)` (lines 102-103) with:
  1. `registry.SharedMaterial.SetBuffer(...)` calls (unchanged, keep in
     `Submit` or inline).
  2. `registry.SharedMesh.SetSubMesh(0, new SubMeshDescriptor(0, entityCount * 6), MeshUpdateFlags.DontRecalculateBounds)`.
- Delete `PopulateArgs` entirely (lines 136-147) — no more indirect args.
- `Submit` (lines 149-154) loses the `CombatIndirectRenderData.Publish(...)`
  call; keep the two `SetBuffer` calls.

## Acceptance Criteria

- No reference to `GraphicsBuffer.IndirectDrawIndexedArgs`,
  `SystemInfo.supportsIndirectArgumentsBuffer`, or `CombatIndirectRenderData`
  remains in this file.
- With N active projectiles/AOEs, the registry's mesh submits exactly
  `N * 6` indices via its active submesh range.
- Zero active entities results in a zero-index submesh (nothing drawn) rather
  than disabling the renderer or destroying anything.

## Dependencies

Depends on 002 (mesh capacity growth API), 003 (registry owns the
`MeshRenderer`/mesh this system now drives instead of publishing to a static
handoff), and 004 (shader must read the new per-vertex `slotIndex` for the
submitted geometry to look correct — land these together).

## Complexity

Medium — this is the subtask that actually flips the mechanism over; the
surrounding scatter/upload code is untouched, but this is where the old and
new paths cross, so it's the highest-risk single change in the plan.
