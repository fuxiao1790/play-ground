# Task Execution Packet

## Task
004-skillloadoutui-gate-cleanup.md

## Goal
Switch the picker from the removed two-boolean gate API to the single modal-suspend API.

## Files Allowed To Modify
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`

## Behavior To Preserve
- Existing picker creation, choices, cancellation, and player-root resolution.
- Existing top-layer UI Toolkit picking behavior and the USS added in task 002.

## Behavior To Change
- Picker opening calls `SetGameplayInputSuspended(true)`.
- Picker closing calls `SetGameplayInputSuspended(false)`.

## Relevant Global Context
- The modal is a distinct input-mode gate; UI picking decides normal click-through.
- No pointer-over-UI bookkeeping or substitute gate is permitted.

## Dependencies Confirmed
- `PlayerRoot` now exposes `SetGameplayInputSuspended(bool)`.
- `.world-input-surface` exists in the panel USS.
- The only remaining `SetGameplayInputGate` calls are the two planned replacements in `SkillLoadoutUi`.

## Step-By-Step Instructions
1. Replace only the open-picker legacy call with modal suspension enabled.
2. Replace only the close-picker legacy call with modal suspension disabled.
3. Do not change other logic.

## Acceptance Criteria
- No `SetGameplayInputGate` references remain.
- Picker suspension continues to freeze move/dash via `PlayerRoot`.
- Bar buttons remain top-layer controls, so they cannot start surface fire.

## Validation Required
- Search all source files for the removed API and confirm the two new calls.
- Compile if available; otherwise record exact limitation.

## Hard Boundaries
- Do not modify `PlayerRoot`, surface, USS, or input assets.
