# Task Execution Packet

## Task
001-shared-acquisition-helper.md

## Goal
Move nearest-hostile selection into Burst-safe `TargetedAcquisition`; resolve calls it.

## Files Allowed To Modify/Create
- Create `Assets/Scripts/System/Targeted/TargetedAcquisition.cs`.
- Modify `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`.

## Behavior
- Preserve faction, shape, radius, dedupe, rank, tie-break, and exclusion behavior.
- No managed allocation; keep `MaxChainCount` in resolve and pass it as bound.

## Dependencies Confirmed
- None required; current selection exists in resolve job.

## Validation
- Static search confirms no selection logic remains in resolve and helper call compiles by shape.
- Do not run Unity tests.
