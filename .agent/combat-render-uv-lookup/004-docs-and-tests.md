# 004 — Docs & tests

## Goal
Bring the design docs and tests in line with the new UV-lookup model and stride.

## Docs
- [combat-render-system.md](../../Docs/reference/simulation/combat-render-system.md):
  - Summary + Data flow: per-instance record is now `objectToWorld + renderId`; UV basis
    is a **static per-kind `StructuredBuffer<CombatUvBasis>` uploaded once** and looked up
    in the shader by `renderId`. Note the atlas-is-static premise that licenses the
    one-time upload.
  - Key types: update `CombatInstanceData` stride (96 → 68), note `CombatRenderComponent`
    no longer carries `UvOriginU`/`UvV` (now `RenderMeta` packed int); add
    `CombatUvBasis` + the registry-owned `_uvBasisBuffer`.
  - "The UV basis (why not a rect)" section stays valid — only the storage location moved;
    add a line that the basis is now per-kind on the GPU, not per-instance.
  - Critical constraints: add `_UvBasis` to the `Material.SetBuffer`-not-MPB rule.
- [render-batch-data.md](../../Docs/contracts/render-batch-data.md): update the per-instance
  record contract to the 68-byte layout and the separate per-kind UV table.

## Tests
- Any assert of `SizeOf<CombatRenderComponent> == 96` / `InstanceDataStride == 96` → 68.
- Tests that set `RenderTypeId` / `AlignToVelocity` on `CombatRenderComponent`
  (ProjectileSpawnPipelineTests, AoeSimulationTests, CombatPoolCleanupSystemTests) still
  compile — they use the properties, which now target `RenderMeta`. Verify they pass.
- Tests reading `objectToWorld` (AoePlayModeTests:740) are unaffected.
- Add/extend a test asserting `RenderTypeId`/`AlignToVelocity` round-trip through
  `RenderMeta` without cross-corruption.
- CombatAtlasTestFixture: if it asserts UV values on the component, move that assertion to
  the registry's `Entries`/UV buffer source instead.

## Acceptance criteria
- Docs describe the once-uploaded per-kind UV table and 68-byte instance.
- Full Edit/PlayMode suite compiles and passes.

## Dependencies
Follows 001–003.

## Scope
Small-medium (mostly prose + assert bumps).
