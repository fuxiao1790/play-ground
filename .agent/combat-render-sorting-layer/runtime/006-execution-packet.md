# Task Execution Packet

## Task
006-remove-render-feature.md

## Goal
Delete old RendererFeature and remove it from Renderer2D asset.

## Files Allowed To Modify
- `Assets/Settings/Renderer2D.asset`

## Files Allowed To Delete
- `Assets/Scripts/System/Common/CombatIndirectRenderFeature.cs`

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Behavior To Preserve
- Combat sprites render through the new MeshRenderer path.

## Behavior To Change
- Old static handoff and render pass no longer exist.

## Relevant Global Context
- 005 removes all consumers of `CombatIndirectRenderData`.

## Dependencies Confirmed
- 005 removes old references.

## Step-By-Step Instructions
- Delete old C# file.
- Remove renderer feature entry from `Renderer2D.asset`.
- Verify no references remain.

## Acceptance Criteria
- No compile references to old feature or handoff.
- Renderer2D has no combat feature entry.

## Validation Required
- Search and C# compile.

## Hard Boundaries
- Do not change other Renderer2D settings.
