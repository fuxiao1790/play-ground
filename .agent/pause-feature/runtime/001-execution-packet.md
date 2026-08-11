# Task Execution Packet

## Task

001-pause-controller.md

## Goal

Add standalone Game Logic pause authority using `UI/Cancel` to toggle `Time.timeScale` and `AudioListener.pause`, publishing `PausedChanged`.

## Files Allowed To Modify

- None.

## Files Allowed To Create

- `Assets/Scripts/Game/PauseController.cs`

## Behavior To Preserve

- No Game Logic reference to UI.
- Authored `Time.fixedDeltaTime` is unchanged.

## Behavior To Change

- Esc toggles global pause state.
- Disabling controller restores globals it installed.

## Relevant Global Context

- Component owns no reactions in player/UI; observers subscribe later.
- Validate owned serialized state in `Awake`; enable/disable action in lifecycle callbacks.
- Tests must not be run by agent.

## Dependencies Confirmed

- None: task has no prerequisites.

## Step-By-Step Instructions

1. Create `PlayGround.Game.PauseController` with serialized `InputActionAsset`, `IsPaused`, `PausedChanged`, `SetPaused`, and `TogglePause`.
2. In `Awake`, validate `inputActions`, resolve `UI/Cancel` with `FindAction("UI/Cancel", true)`.
3. Enable action in `OnEnable`; disable it in `OnDisable`, restoring global pause state when paused.
4. Toggle on `WasPressedThisFrame` in `Update`.
5. Make `SetPaused` idempotent and change state/globals/event in specified order.

## Acceptance Criteria

- Toggle updates both globals.
- Event fires once per actual state change.
- Disable restores globals.
- Missing `inputActions` throws clearly.
- No UI reference from Game Logic.

## Validation Required

- Static inspection; Unity test execution deferred to user.

## Hard Boundaries

- No existing-file changes.
- No new architecture or outgoing gameplay/UI dependencies.
