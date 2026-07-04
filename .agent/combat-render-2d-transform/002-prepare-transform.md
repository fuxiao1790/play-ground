# 002 — Prepare transform: authoring + kinematics -> compact render

## Scope
`Assets/Scripts/System/Common/CombatRenderComponents.cs`
(`CombatRenderMatrixUtility`) and
`Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`.

## Change

### CombatRenderMatrixUtility
Rework the two producers to consume `CombatRenderAuthoring` (base) instead of
reading base back out of the matrix, and to write the compact fields.

- `ElementFor(in CombatKinematicsComponent kin, in CombatRenderAuthoring auth,
  in CombatRenderComponent render) -> (float4 rotation, float2 position)`
  (or write into a passed-by-ref render). Logic mirrors the current method:
  - direction = velocity-normalized if `render.AlignToVelocity != 0` and
    `lengthsq(velocity) > 1e-6`, else `(1,0)`.
  - `cos = dirX*auth.BaseCos - dirY*auth.BaseSin`,
    `sin = dirX*auth.BaseSin + dirY*auth.BaseCos`.
  - `rotation = float4(cos*BaseScale.x, -sin*BaseScale.y,
    sin*BaseScale.x, cos*BaseScale.y)`  // m00, m01, m10, m11
  - `position.xy = kin.Position` (Position.z / RenderMeta preserved from the
    existing render component).
  - `render.IsRenderable == 0` still short-circuits to the degenerate result.
- `DegenerateInstance(in CombatRenderComponent render)`: `Rotation = float4(0)`,
  keep `Position` (last-known xy + RenderZ) and `RenderMeta`. Replaces
  `DegenerateMatrix`; same zero-area-quad collapse.

### CombatRenderPrepareSystem
- `renderPrepareQuery` gains `WithAll<CombatRenderAuthoring>()`.
- Add `ComponentTypeHandle<CombatRenderAuthoring> authoringHandle` (read-only),
  `.Update` it, pass into the job.
- `RenderPrepareJob.Execute`: read the authoring array; per entity write
  `component.Rotation`/`component.Position.xy` from
  `ElementFor(kin[i], auth[i], component)` when active, else
  `DegenerateInstance(component)`; write back.
- Update the `OnCreate` size assert `68 -> 32`.

## Acceptance
- Active projectiles render at the same world transform as before (velocity
  alignment + authored rotation + scale). AOE unchanged (no align).
- Inactive/pooled entities collapse (zero-area) — nothing visible.
- Prepare no longer reads any base data out of the matrix; base comes solely
  from `CombatRenderAuthoring`.

## Depends on
001.
