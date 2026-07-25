# 003 — `PlayerRoot` consumes the fire source; drop mouse-button read

## Goal
Stop `PlayerRoot` from pulling the mouse `Attack` button. Fire comes from the
registered `IGameplayInputSource`. Collapse the two gate booleans into one
modal-suspend flag.

## Changes to `Assets/Scripts/Player/PlayerRoot.cs`

Remove:
- Field `private InputAction attackAction;` and its `FindAction("Attack", true)`
  line in `Awake`.
- Fields `private bool gameplayInputBlocked;` and
  `private bool pointerOverSkillUi;`.
- Method `ReadAttackHeld()`.

Add:
- `private IGameplayInputSource fireInput;`
- `private bool gameplayInputSuspended;`
- Registration API (replaces `SetGameplayInputGate`):
  ```csharp
  public void SetFireInput(IGameplayInputSource source) => fireInput = source;

  public void ClearFireInput(IGameplayInputSource source)
  {
      if (fireInput == source) fireInput = null;
  }

  // Loadout editing (picker modal) freezes gameplay input.
  public void SetGameplayInputSuspended(bool suspended) =>
      gameplayInputSuspended = suspended;
  ```

Update the gated reads:
- `ReadMoveInput()`: gate on `gameplayInputSuspended` instead of
  `gameplayInputBlocked`.
- `ReadDashPressedThisFrame()`: gate on `gameplayInputSuspended`.
- Fire in `Update`:
  ```csharp
  bool fireHeld = !gameplayInputSuspended && (fireInput?.FireHeld ?? false);
  skillDriver.Tick(fireHeld, facing.AimDirection, aimWorldPosition);
  ```

`Start` (add near existing setup): if `fireInput == null`, `Debug.LogWarning`
once that no `IGameplayInputSource` is registered so the player cannot fire
(per open-question decision #1 — warn, do not throw, to keep HUD-optional scenes
running). Keep `ReadAimWorldPosition`, `pointAction`, `lookAction`, `moveAction`,
`dashAction` unchanged.

## Notes
- The `Attack` `InputAction` and its `.inputactions` bindings are left untouched;
  `PlayerRoot` simply no longer reads them (mouse-only fire now flows through the
  surface). No asset edit.
- Suspending gameplay input does not force-release the surface; the surface only
  starts a hold on a world `PointerDown`, and the picker can only be opened by
  clicking a button (a separate press), so there is no held-through-modal case to
  reset. `!gameplayInputSuspended` in the fire expression is belt-and-suspenders.

## Acceptance Criteria
- `PlayerRoot` no longer references `attackAction` / any mouse button.
- Player fires only when the surface reports `FireHeld` and input is not
  suspended.
- Movement and dash freeze while suspended (parity with old modal behavior).
- Compiles with 002.

## Dependencies
001, and co-authored with 002.

## Scope
Small.
