# 008 — Update docs

## Changes

- `Docs/contracts/render-batch-data.md` (~L22): drop `CombatRenderElement` from
  the shared render data list; note that prepared object-to-world matrices now
  live in per-batch `NativeList<Matrix4x4>` buffers owned by
  `CombatRenderPrepareSystem` and consumed by `CombatBatchedRenderSystem` (no
  per-entity matrix component, no `ToComponentDataArray` copy).
- `Docs/reference/simulation/project-aoe-system-common.md` (~L280): remove
  `CombatRenderElement` from the AOE component list; add a one-line note that the
  render matrix is prepared into the shared per-batch buffers.

## Acceptance criteria

- No doc lists `CombatRenderElement` as a live component.
- Render-batch data flow doc reflects the buffer-based pipeline.

## Dependencies

Independent; do after the code lands so the docs match reality.
