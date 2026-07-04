# 001 — Per-kind UV basis GPU buffer in the registry

## Goal
`CombatRenderResourceRegistry` builds and owns a persistent `GraphicsBuffer` holding
every registered kind's UV basis, indexed by `renderId`, rebuilt only when the kind set
changes. This is the single GPU-side source of truth for atlas UVs.

## Changes
- Add fields: `GraphicsBuffer _uvBasisBuffer;` and `bool _uvDirty;`.
- UV record layout (per kind): `2 × float4` = 32 bytes → `uvOriginU` (origin.xy, U.xy) +
  `uvV` (V.xy, 0, 0). Matches today's two vectors exactly. Define a small
  `[StructLayout(Sequential)] struct CombatUvBasis { Vector4 OriginU; Vector4 V; }`
  (stride 32) shared conceptually with the shader struct.
- `Register(...)`: after writing `Entries[renderId]`, set `_uvDirty = true`. (UV math
  unchanged.)
- `Unregister()`: set `_uvDirty = true`; dispose `_uvBasisBuffer` and null it.
- Add `public GraphicsBuffer EnsureUvBasisBuffer()`:
  - if `!_uvDirty && _uvBasisBuffer != null` return it.
  - size `count = _nextRenderId` (slots `0.._nextRenderId-1`; slot 0 = zero basis).
  - (re)allocate `_uvBasisBuffer` as `new GraphicsBuffer(GraphicsBuffer.Target.Structured,
    count, 32)` when null or too small; fill a temp `CombatUvBasis[count]` from `Entries`
    (index 0 left zero), `SetData`, clear `_uvDirty`, return it.
- Dispose `_uvBasisBuffer` wherever `Unregister` tears down shared resources.

## Acceptance criteria
- After N `Register` calls, `EnsureUvBasisBuffer()` returns a buffer of length
  `_nextRenderId` whose entry `[renderId]` equals that kind's authored `UvOriginU`/`UvV`.
- Calling it twice with no intervening (un)register does not reallocate or re-`SetData`.
- No allocation on the per-frame path (only on dirty).
- Buffer disposed on `Unregister`; no leak across scene reloads.

## Dependencies
None. Can land before 002/003 (buffer simply unused until the shader reads it), but must
not ship to players without 002/003 (stride mismatch).

## Scope
Small. One file: [CombatRenderComponents.cs](../../Assets/Scripts/System/Common/CombatRenderComponents.cs).
