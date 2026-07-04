# Task Execution Packet

## Task
005-tests-docs.md

## Goal
Update affected tests and docs for compact render record and authoring split.

## Files Allowed To Modify
- `Assets/Tests/EditMode/ProjectileAuthoringEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Docs/reference/simulation/combat-render-system.md`
- `Docs/contracts/render-batch-data.md`
- memory note for `project_render_indirect` if present
- `.agent/combat-render-2d-transform/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Test files named above.
- Existing docs named above.

## Behavior To Preserve
- Test intent remains the same.
- Docs continue to describe existing zero-copy indirect renderer.

## Behavior To Change
- Tests assert 32 B render component and 16 B authoring component.
- Tests read compact render fields or command authoring instead of matrix spare cells.
- Docs describe 32 B compact 2D upload record and CPU-only authoring split.

## Relevant Global Context
- Compact render component is the GPU wire record.
- Base scale/sin/cos no longer lives in render component.

## Dependencies Confirmed
- Tasks 001-004 are implemented.
- `dotnet build .\PlayGround.Runtime.csproj` is blocked by Unity package compile errors outside project code.

## Step-By-Step Instructions
- Update size assertions.
- Update AOE playmode matrix reads to compact fields.
- Move command test visual scale/sin/cos setup to `Authoring`.
- Update render docs and memory note.
- Run available validation; report any external blockers.

## Acceptance Criteria
- Named tests no longer reference removed `objectToWorld` or render visual authoring fields.
- Docs say stride 32 and mention `CombatRenderAuthoring`.
- Implementation log updated.

## Validation Required
- Build/test if runnable.

## Hard Boundaries
- Do not broaden docs beyond render batch data and requested memory note.
- Do not change production behavior in this task except direct test-driven compile fixes if found.
