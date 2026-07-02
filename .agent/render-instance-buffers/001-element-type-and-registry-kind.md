# 001 — Element type → `Matrix4x4`, add `MatrixFor`, add registry `Kind`

**File:** `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Changes

1. **Remove** `public struct CombatRenderElement : IComponentData` (lines 24-27)
   and its `ECS Lifecycle:` comment. Nothing keeps it after this plan — the render
   buffer is `Matrix4x4` and tests compute via `MatrixFor`.
2. **Replace** `CombatRenderMatrixUtility.ElementFor(kin, render) -> CombatRenderElement`
   with `MatrixFor(kin, render) -> Matrix4x4` (same body, return the `Matrix4x4`
   instead of wrapping it in `CombatRenderElement`). Keep the early-out for
   `IsRenderable == 0` returning `default(Matrix4x4)` (an all-zero matrix → the
   entity contributes a degenerate instance; this matches prior behavior where a
   non-renderable entity produced a zero matrix, and such entities are gated out
   by `CombatRenderActiveTag` anyway).
3. **Add** `public enum CombatRenderKind { Projectile, Aoe }`.
4. **Add** `public CombatRenderKind Kind;` to `CombatRenderResourceEntry`.
5. **Add** a `CombatRenderKind kind` parameter to `Register(...)` and set
   `Kind = kind` on the created entry (line ~75).

## Acceptance criteria

- Project compiles except for the `Register` call sites (fixed in 002) and the
  `CombatRenderElement` references in systems/tests (fixed in 003-007).
- `MatrixFor` returns a `Matrix4x4` byte-identical to the old
  `ElementFor(...).objectToWorld`.
- No remaining `struct CombatRenderElement`.

## Dependencies

None (foundational). Blocks 002-007.
