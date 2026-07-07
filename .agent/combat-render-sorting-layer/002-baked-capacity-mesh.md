# 002 - Baked Capacity Mesh

## Scope

`CombatRenderResourceRegistry` (`Assets/Scripts/System/Common/CombatRenderComponents.cs`).

## Change

Replace `BuildUnitQuadMesh()` (4 vertices, 1 quad,
[CombatRenderComponents.cs:289-313](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L289-L313))
with a capacity-baked mesh builder:

- `BuildCapacityMesh(int capacity)` produces `capacity` quads:
  - Vertices: the same four unit-quad corners
    (`(-0.5,-0.5)`, `(-0.5,0.5)`, `(0.5,0.5)`, `(0.5,-0.5)`) repeated per quad,
    `capacity * 4` total.
  - UV0: the same `(0,0)`, `(0,1)`, `(1,1)`, `(1,0)` repeated per quad —
    unchanged from today, just repeated.
  - UV1 (new): `slotIndex` as a float, same value for all 4 corners of a quad
    (`0` for quad 0, `1` for quad 1, ...) — this is what subtask 004's shader
    change reads instead of `SV_InstanceID`.
  - Triangles: `{0,1,2,0,2,3}` per quad, offset by `quad*4`, `capacity * 6`
    indices total.
  - `mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (BoundsHalfExtent * 2))`
    set explicitly (do not `RecalculateBounds()` — the whole point is the
    renderer must never be culled as instances move within the capacity).
- Track current baked capacity on the registry (mirrors
  `CombatBatchedRenderSystem._instanceCapacity`'s role, but owned here since
  the registry owns the mesh).
- Growth: when the active count (passed in from `CombatBatchedRenderSystem`,
  see 005) exceeds current baked capacity, rebuild the mesh at
  `max(currentCapacity * 2, requiredCount)` — same doubling policy as
  `EnsureInstanceCapacity`
  ([CombatBatchedRenderSystem.cs:119-134](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L119-L134)).
  Never shrink.
- Initial capacity: reuse `InitialInstanceCapacity` (1024) as the starting
  mesh capacity, or introduce a registry-local constant of the same value —
  keep them equal so mesh and instance-buffer growth stay in lockstep.

## Acceptance Criteria

- `SharedMesh` always has `capacity * 4` vertices / `capacity * 6` indices,
  where `capacity` is monotonically non-decreasing across the mesh's lifetime.
- `mesh.bounds` never triggers visible culling regardless of where active
  instances are positioned in the world.
- No behavior change yet visible in-game (this subtask only prepares the mesh
  shape; nothing submits it differently until 005).

## Dependencies

None (mesh building is self-contained). Subtask 003 attaches this mesh to a
`MeshRenderer`; subtask 004 updates the shader to read the new UV1 stream;
subtask 005 drives the growth/`SetSubMesh` calls from
`CombatBatchedRenderSystem`.

## Complexity

Small-medium — mostly mechanical mesh-building code, but the growth/rebuild
path needs care to match the existing doubling policy exactly.
