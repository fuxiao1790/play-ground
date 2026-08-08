# Task Execution Packet

## Task
004-docs-and-tests.md

## Goal
Document new targeted flow and add regression coverage.

## Files Allowed To Modify
- `Docs/reference/simulation/targeted-system.md`
- `Docs/contracts/spawn-events-and-commands.md`
- `Assets/Tests/EditMode/TargetedSpawnPipelineEditModeTests.cs`
- `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs`
- Create `Assets/Tests/EditMode/TargetedAcquisitionEditModeTests.cs` if needed.

## Behavior
- Cover acquired/unacquired pose, nearest hostile/faction, agreement, and origin-started first link.

## Dependencies Confirmed
- Tasks 001-003 must be present before tests are edited.

## Validation
- Static review only here; user runs tests and supplies XML.
