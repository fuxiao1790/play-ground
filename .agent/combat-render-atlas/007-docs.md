# Docs

*(2026-07-03: re-applied after the atlas-configuration revision — "renderId → UV rect table"
language replaced with "UvRect on CombatRenderComponent, computed once"; "packs into an atlas"
replaced with "manually-assembled atlas, throws if a sprite isn't part of it" where the docs
described the mechanism, not just the outcome.)*

## Change

Update design docs describing the render path to match the atlas rework.

### Primary rewrite

`Docs/contracts/render-batch-data.md`:
- **Fields / Shape**: describe `CombatRenderComponent`, `CombatRenderElement`,
  `CombatRenderActiveTag`, `CombatRenderBatchId` as today, but reframe
  `CombatRenderBatchId` as a render-kind index into the registry's shared
  `renderId → UV rect` table (not a "GPU resource batch at submit" selector
  into a per-kind dictionary).
- **Guarantees**: replace "grouped by `CombatRenderBatchId` for instanced
  sprite submission" with a description of the shared atlas — one mesh, one
  material, one atlas texture, near-1 draw call for everything (capped at
  1023 instances/call).
- **Ordering**: replace the per-batch dictionary scatter/submit description
  with the new single-buffer scatter (`_transforms`/`_uvRects` filled in one
  active-only pass) + chunked `RenderMeshInstanced` submit.
- **Notes / TODOs**: the existing "replace the main-thread scatter with
  parallel count/prefix-sum/scatter" and "fold matrix generation into scatter
  and remove `CombatRenderElement`" deferred items are unrelated to this
  plan and stay as-is (still valid future work, not addressed here) — do not
  remove them just because this doc section is being edited.

### Secondary edits (small, targeted — not rewrites)

- `Docs/reference/simulation/projectile-system.md` — "Rendering And VFX"
  section describes `CombatBatchedRenderSystem` scattering matrices by
  `CombatRenderBatchId` and submitting per-kind batches; update to describe
  the shared atlas submit.
- `Docs/reference/simulation/aoe-system.md` — same section, same update.
- `Docs/reference/simulation/project-aoe-system-common.md` — same section,
  same update.
- `Docs/reference/simulation/project-ecs-implementation.md` — "Presentation
  Bridge" section mentions `CombatBatchedRenderSystem` resolving the owning
  `CombatRoot` and submitting batched instances through tag-scoped queries;
  update the submission description (the tag-scoped query part is unchanged,
  only the "batched by kind" framing changes).

Do not touch `Docs/profiling.md` (confirmed to have no render-specific
content during planning).

## Acceptance Criteria

- No doc describes a per-kind `Dictionary`/per-kind `Mesh`/`Material` submit
  path (grep for `Dictionary<int, NativeList<Matrix4x4>>`, per-kind wording).
- `render-batch-data.md` accurately describes: the shared mesh/material/atlas
  texture, the `renderId → UV rect` table, the single scatter pass, the
  chunked submit loop, and (unchanged) the sim/apply ownership boundaries.
- Secondary docs' edits are small and targeted (a paragraph or two each), not
  full rewrites — this task should not restructure documents beyond the
  rendering-specific sections named above.

## Dependencies

001, 002, 003, 004, 005, 006 (docs describe the final, tested shape).

## Scope

Small.
