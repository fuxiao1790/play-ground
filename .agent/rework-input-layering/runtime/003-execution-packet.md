# Task Execution Packet

## Task
003-playerroot-consume-fire-source.md

## Goal
Make `PlayerRoot` consume the registered `IGameplayInputSource`, remove raw Attack input, and replace the two old gates with modal suspension.

## Files Allowed To Modify
- `Assets/Scripts/Player/PlayerRoot.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/Player/IGameplayInputSource.cs`
- `Assets/Scripts/SkillUi/GameplayInputSurface.cs`

## Behavior To Preserve
- Existing move, dash, aim, health, combat target, animation, and `SkillDriver.Tick` behavior.
- Aim remains from `UI/Point`; input-action asset stays unchanged.

## Behavior To Change
- Fire is `!gameplayInputSuspended && (fireInput?.FireHeld ?? false)`.
- Movement and dash are gated by `gameplayInputSuspended`.
- Missing UI fire source warns once in `Start` but does not stop HUD-optional scenes from running.

## Relevant Global Context
- Game Logic owns the input source contract and must not reference UI types.
- Fire is sampled once in `Update`; do not introduce alternate raw mouse reads.
- The UI source registers in `OnEnable`, before `PlayerRoot.Start` can issue its one-time warning.

## Dependencies Confirmed
- `IGameplayInputSource` exists in `PlayGround.Player` with `FireHeld`.
- `GameplayInputSurface` implements it and calls `SetFireInput(this)`/`ClearFireInput(this)`; those methods are absent from `PlayerRoot` and must be added by this task.
- Current `PlayerRoot` has the legacy `attackAction`, `gameplayInputBlocked`, `pointerOverSkillUi`, `SetGameplayInputGate`, and `ReadAttackHeld` paths to replace.

## Step-By-Step Instructions
1. Remove `attackAction`, its `FindAction`, both legacy booleans, `SetGameplayInputGate`, and `ReadAttackHeld`.
2. Add the input-source and modal-suspend fields plus exact registration/suspend APIs from the task.
3. Add a `Start` warning if no fire source is registered.
4. Gate move/dash with the modal flag.
5. Compute fire locally in `Update` from source and modal flag, then preserve the existing `SkillDriver.Tick` arguments.

## Acceptance Criteria
- `PlayerRoot` has no raw Attack/mouse read.
- Only registered source can make fire true, and modal suspension prevents it.
- Modal suspension retains move/dash freeze behavior.
- The paired task 002 source compiles against the new API.

## Validation Required
- Search for removed legacy symbols and required source registration APIs.
- Compile Game Logic and Skill UI code if available.

## Hard Boundaries
- Do not alter the `.inputactions` asset, UI source, USS, or `SkillLoadoutUi`.
- Do not add a fallback fire source or UI reference.
