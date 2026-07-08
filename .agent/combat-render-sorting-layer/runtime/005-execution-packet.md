# Task Execution Packet

## Task
005-submission-path-rework.md

## Goal
Stop publishing indirect args and drive the registry mesh submesh range instead.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`

## Behavior To Preserve
- Existing scatter and `_InstanceData` upload.
- Existing `Material.SetBuffer` binding.

## Behavior To Change
- Zero active means zero-index submesh.
- Active count sets `entityCount * 6` indices.
- No indirect args buffer or static handoff.

## Relevant Global Context
- Registry owns capacity mesh and renderer.
- Mesh capacity grows by doubling with active count.

## Dependencies Confirmed
- 002 mesh capacity API exists.
- 003 persistent renderer exists.
- 004 shader slot index exists.

## Step-By-Step Instructions
- Remove indirect support guard and args buffer.
- Replace `CombatIndirectRenderData.Clear()` with registry zero submesh calls.
- Ensure instance and mesh capacity before upload.
- Set buffers and active submesh range.
- Delete `PopulateArgs`.

## Acceptance Criteria
- No indirect args or `CombatIndirectRenderData` references remain in file.
- Active count maps to `N * 6` indices.

## Validation Required
- C# compile and search.

## Hard Boundaries
- Do not change render query semantics.
