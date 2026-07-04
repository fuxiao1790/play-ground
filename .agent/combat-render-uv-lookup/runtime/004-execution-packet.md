# Task Execution Packet

## Task
004-docs-and-tests.md

## Goal
Update docs and tests for the new per-kind UV lookup model and 68-byte instance layout.

## Files Allowed To Modify
- `Docs/reference/simulation/combat-render-system.md`
- `Docs/contracts/render-batch-data.md`
- Tests directly affected by `CombatRenderComponent` stride/meta behavior.
- `.agent/combat-render-uv-lookup/implementation-log.md`

## Files Allowed To Create
- New focused test file if local test structure supports it.

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Tests/PlayMode/*`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`

## Behavior To Preserve
- Existing render contract, one draw call, no runtime atlas packing.
- Existing test caller use of `RenderTypeId` and `AlignToVelocity` properties.

## Behavior To Change
- Docs describe `objectToWorld + RenderMeta` instance record and separate `_UvBasis`.
- Docs describe registry-owned UV buffer and atlas-static premise.
- Tests assert metadata packing round-trip and new stride where applicable.

## Relevant Global Context
- UV basis math remains valid; only storage moved.
- `CombatRenderBatchId` remains a kind id and now is also the shader lookup id through `RenderMeta`.
- `_InstanceData` and `_UvBasis` must be bound with `Material.SetBuffer`.

## Dependencies Confirmed
- 001-003 complete by code evidence:
  - `EnsureUvBasisBuffer()` exists.
  - `CombatRenderComponent` has `RenderMeta`.
  - `InstanceDataStride` is 68 and shader uses `_UvBasis`.

## Step-By-Step Instructions
- Update combat render system doc summary, data flow, key types, UV basis section, and critical constraints.
- Update render batch data contract fields, guarantees, and ordering.
- Add or extend test for `RenderTypeId`/`AlignToVelocity` round-trip through `RenderMeta`.
- Bump old stride assertions if found.

## Acceptance Criteria
- Docs describe once-uploaded per-kind UV table and 68-byte instance.
- Tests compile and pass, or inability to run is explained.

## Validation Required
- Search for stale 96-byte/instance-UV claims in targeted docs/tests.
- Run relevant Unity tests/build if available.

## Hard Boundaries
- Do not broaden docs beyond targeted render contract updates.
- Do not add production-only test hooks.
- Do not change runtime behavior outside compile fixes caused by this task.
