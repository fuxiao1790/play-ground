# Task Execution Packet

## Task
001-uv-basis-gpu-buffer.md

## Goal
Add a registry-owned persistent GPU buffer containing every registered kind's UV basis, indexed by `renderId`.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `.agent/combat-render-uv-lookup/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Behavior To Preserve
- Existing UV basis math and `Entries` source data.
- Existing mesh/material/atlas ownership.

## Behavior To Change
- Registry tracks dirty UV table state and lazily uploads `CombatUvBasis[]` to a `GraphicsBuffer`.
- `Register` and `Unregister` mark UV table dirty.
- `Unregister` disposes UV buffer.

## Relevant Global Context
- Slot 0 must be safe zero basis.
- Buffer count is `_nextRenderId`, covering indices `0.._nextRenderId - 1`.
- UV basis buffer is owned by registry and references no ECS memory.

## Dependencies Confirmed
- None required.
- Code evidence: `CombatRenderResourceRegistry` owns `Entries`, `Register`, and `Unregister` in `CombatRenderComponents.cs`.

## Step-By-Step Instructions
- Add `CombatUvBasis` sequential struct with two `Vector4` fields.
- Add `_uvBasisBuffer` and `_uvDirty`.
- Set `_uvDirty = true` after `Entries[renderId]` is written.
- Dispose/null `_uvBasisBuffer` and mark dirty on `Unregister`.
- Add `EnsureUvBasisBuffer()` that reallocates when null or too small, fills slot 0 zero and registered entries, calls `SetData`, clears dirty, and returns buffer.

## Acceptance Criteria
- Buffer contains authored `UvOriginU`/`UvV` by render id.
- Clean repeated calls return existing buffer.
- No per-frame UV allocation when clean.
- Buffer disposed on unregister.

## Validation Required
- Build/compile later after lockstep tasks.
- Search code for `EnsureUvBasisBuffer` and disposal path.

## Hard Boundaries
- Do not modify files outside the allowed list except direct compile fixes.
- Do not change architecture.
- Do not introduce abstractions not described by the task.
- Stop on architectural ambiguity.
