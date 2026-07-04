# 002 — Shrink CombatRenderComponent (drop UV, add packed RenderMeta)

## Goal
Remove the static UV payload from the per-instance/ECS record and replace the
`float`-punned `renderId`/`align` metadata with one explicit packed `int`, taking the
component from 96 → 68 bytes while keeping it binary-identical to the GPU instance record.

## Changes ([CombatRenderComponents.cs](../../Assets/Scripts/System/Common/CombatRenderComponents.cs))
- `CombatRenderComponent`: delete `Vector4 uvOriginU;` and `Vector4 uvV;`. Add
  `int RenderMeta;` after `objectToWorld`. New layout = `Matrix4x4 (64) + int (4) = 68`.
- Rewrite metadata accessors to use `RenderMeta`:
  - `RenderTypeId { get => RenderMeta & 0x7FFFFFFF; set => RenderMeta = (RenderMeta & unchecked((int)0x80000000)) | (value & 0x7FFFFFFF); }`
  - `AlignToVelocity { get => (RenderMeta >> 31) & 1; set => RenderMeta = value != 0 ? RenderMeta | unchecked((int)0x80000000) : RenderMeta & 0x7FFFFFFF; }`
  - `IsRenderable` unchanged (derives from `RenderTypeId != 0`).
- Delete the `UvOriginU` / `UvV` properties (no remaining callers after registry edits).
- `SetVisualTransform`: keep signature; it already calls `AlignToVelocity` /
  `RenderTypeId` setters — they now target `RenderMeta`. No body change beyond removing
  any UV references (there are none).
- `ElementFor` / `DegenerateMatrix`: unchanged (read matrix + `AlignToVelocity`/
  `IsRenderable`, all still valid).
- `Get{Projectile,Aoe}RenderComponent`: **remove** the `UvOriginU = ... , UvV = ...`
  initializers (lines ~197-200 / ~217-221). They now construct just the matrix +
  `SetVisualTransform(... renderId)`; UV basis no longer travels on the component.
- Stride asserts → 68:
  - [CombatBatchedRenderSystem.cs:35](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L35)
    already uses `InstanceDataStride` (bumped in 003).
  - [CombatRenderPrepareSystem.cs:21](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs#L21):
    `Assert.AreEqual(68, ...)`.

## Acceptance criteria
- `SizeOf<CombatRenderComponent>() == 68`.
- `RenderTypeId` and `AlignToVelocity` round-trip through `RenderMeta` (unit-checkable):
  setting one does not corrupt the other; `RenderTypeId` accepts all real renderIds
  (small positives).
- Spawned projectiles align to velocity (align bit) and AOEs do not, as before.
- No remaining references to `uvOriginU`/`uvV`/`UvOriginU`/`UvV` on the component.

## Dependencies
Pairs with 003 (shader stride + `_UvBasis` read) — must ship together.

## Scope
Small-medium. Two files (`CombatRenderComponents.cs`, `CombatRenderPrepareSystem.cs`).
Note: the registry's `Entries`/`CombatRenderResourceEntry` keep `UvOriginU`/`UvV` — they
feed the GPU buffer in 001. Only the *component* loses them.
