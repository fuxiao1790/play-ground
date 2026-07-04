# Task Execution Packet

## Task
004-shader-rendertype-skip.md

## Goal
Confirm no shader changes are needed because disabled entities upload degenerate matrices.

## Files Allowed To Modify
- None

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

## Behavior To Preserve
- Shader remains unchanged.

## Behavior To Change
- None.

## Relevant Global Context
- Prep writes zero-scale matrices for disabled render entities.

## Dependencies Confirmed
- Task 002 implemented degenerate matrix writes.

## Step-By-Step Instructions
- Verify shader diff is empty.

## Acceptance Criteria
- Shader remains unchanged.

## Validation Required
- `git diff -- Assets/Shaders/CombatAtlasIndirectSprite.shader` returns no diff.

## Hard Boundaries
- Do not edit shader.
