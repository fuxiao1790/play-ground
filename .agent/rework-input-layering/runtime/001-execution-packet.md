# Task Execution Packet

## Task
001-gameplay-input-source-interface.md

## Goal
Add the Game Logic-owned `IGameplayInputSource` seam for UI-provided fire state.

## Files Allowed To Modify
- None

## Files Allowed To Create
- `Assets/Scripts/Player/IGameplayInputSource.cs`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/PlayGround.GameLogic.asmdef`

## Behavior To Preserve
- Existing player input behavior; this task adds only the contract.

## Behavior To Change
- None until later tasks consume this interface.

## Relevant Global Context
- The interface must stay in Game Logic (`PlayGround.Player`) and contain no UI references.
- It is a mouse-fire seam with one `bool FireHeld` property.

## Dependencies Confirmed
- None required. `PlayGround.GameLogic` exists at `Assets/Scripts/PlayGround.GameLogic.asmdef`.

## Step-By-Step Instructions
1. Create the interface exactly as specified in the task.
2. Include the supplied intent comments.

## Acceptance Criteria
- Compiles in `PlayGround.GameLogic`.
- Contains no UI type reference.

## Validation Required
- Static source inspection; compile validation when available.

## Hard Boundaries
- Do not modify existing source files or implement later tasks.
