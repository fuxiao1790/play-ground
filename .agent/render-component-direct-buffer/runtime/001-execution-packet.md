# Task Execution Packet

## Task
001-unified-render-component.md

## Goal
Merge render matrix and UV upload data into `CombatRenderComponent` so the component is the 96-byte GPU instance layout.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- Compile-fix users of removed `CombatRenderElement` caused by this task.

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- Render tests that reference `CombatRenderElement`.

## Behavior To Preserve
- Active projectile/AOE render matrix math.
- Existing UV basis mapping.
- Existing visual scale reads used by collision/lifetime VFX sizing.

## Behavior To Change
- Remove `CombatRenderElement`.
- Remove `CombatInstanceData`.
- Store GPU upload fields directly on `CombatRenderComponent`.

## Relevant Global Context
- GPU stride must be 96 bytes.
- Prep system owns per-frame matrix writes.
- Spawn-time builders populate UV data and initial visual transform data.

## Dependencies Confirmed
- No prerequisite task.
- Code search found `CombatRenderElement` references in spawn apply systems and tests; these are direct compile fallout.

## Step-By-Step Instructions
- Replace `CombatRenderComponent` with the GPU layout.
- Preserve compatibility accessors needed by existing systems without increasing struct size.
- Make `CombatRenderMatrixUtility.ElementFor(...)` produce the matrix written into the unified component.
- Remove separate upload/intermediate structs.
- Add size assertion in prep system `OnCreate`.

## Acceptance Criteria
- `CombatRenderComponent` is 96 bytes.
- Fields are `Matrix4x4 objectToWorld`, `Vector4 uvOriginU`, and `Vector4 uvV`.
- `CombatRenderElement` and `CombatInstanceData` are removed.
- Spawn-time render component builders populate the new struct.

## Validation Required
- Build/compile after dependent tasks or after compile-fix edits.
- Search for removed type names.

## Hard Boundaries
- Do not alter shader behavior.
- Do not change projectile/AOE simulation contracts except compile fixes from removed render element.
- Do not introduce a new component to hold old render metadata.
