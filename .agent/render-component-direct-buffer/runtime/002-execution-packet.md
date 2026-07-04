# Task Execution Packet

## Task
002-prep-system-all-entities.md

## Goal
Make `CombatRenderPrepareSystem` write `objectToWorld` into `CombatRenderComponent` for enabled and disabled render entities.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- Unity Entities query option documentation/source for enableable state.

## Behavior To Preserve
- Enabled entity matrix math uses `CombatRenderMatrixUtility.ElementFor(...)`.
- Job remains Burst `IJobChunk` in `PresentationSystemGroup`.

## Behavior To Change
- Query ignores enableable filter for `CombatRenderActiveTag`.
- Disabled entities receive degenerate matrix.
- Prep writes back to `CombatRenderComponent.objectToWorld`.

## Relevant Global Context
- `CombatRenderComponent` is 96 bytes and contains matrix/UV data.
- Batched render will read all components after `CompleteDependency()`.

## Dependencies Confirmed
- Task 001 added unified `CombatRenderComponent` and `CombatRenderMatrixUtility.ElementFor(...)` returns `Matrix4x4`.
- Local Entities package exposes `EntityQueryOptions.IgnoreComponentEnabledState`.

## Step-By-Step Instructions
- Remove `CombatRenderElement` handle/query use.
- Add size assertion for `CombatRenderComponent`.
- Use `EntityQueryOptions.IgnoreComponentEnabledState`.
- In job, use enabled mask to choose normal or degenerate matrix.

## Acceptance Criteria
- Prep query no longer filters by enabled render tag.
- Job iterates all chunk entities.
- Enabled entities get normal matrix.
- Disabled entities get scale-zero matrix.

## Validation Required
- Search confirms no `CombatRenderElement` in prep system.
- Build after all dependent tasks.

## Hard Boundaries
- Do not change matrix math for enabled entities.
- Do not change shader.
