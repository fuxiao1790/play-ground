# 001 — Pause Controller And Clock Authority

## Goal

One component that owns pause state, drives `Time.timeScale` and
`AudioListener.pause`, reads the Esc toggle, and publishes state changes for
observers. No outgoing references to gameplay or UI components.

## Scope

New file: `Assets/Scripts/Game/PauseController.cs`, namespace `PlayGround.Game`
(assembly `PlayGround.GameLogic`).

```csharp
public sealed class PauseController : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;

    public bool IsPaused { get; private set; }
    public event Action<bool> PausedChanged;

    public void SetPaused(bool paused);
    public void TogglePause();
}
```

Behaviour:

- `Awake` resolves `UI/Cancel` and validates. Mirror the existing pattern in
  `PlayerRoot.cs:132-138`: `inputActions.FindAction("UI/Cancel", true)` and
  enable that single action rather than the whole UI map. A missing
  `inputActions` reference throws a clear setup error (I7); `FindAction` with
  `throwIfNotFound: true` covers the action itself.
- `OnEnable` enables the cancel action. `OnDisable` disables it, and restores
  `Time.timeScale = 1f` and `AudioListener.pause = false` if currently paused,
  so a disabled controller can never leave the game frozen or silent. Both
  globals were installed by this component, so this stays inside the I8
  boundary.
- `Update` calls `TogglePause()` on `cancelAction.WasPressedThisFrame()`. The
  Input System runs on dynamic update and is unaffected by `timeScale`, so this
  keeps working while paused.
- `SetPaused` is idempotent: return early if the value is unchanged. On change,
  set `IsPaused`, then `Time.timeScale = paused ? 0f : 1f`, then
  `AudioListener.pause = paused`, then raise `PausedChanged`.

Do not set `Time.fixedDeltaTime` — it must stay at its authored value so
physics resumes cleanly.

Do not reference `PlayerRoot`, `GameplayInputSurface`, `PauseMenuUi`, or any
other component here. Observers subscribe (D6); the UI types are in
`PlayGround.Ui` and referencing them would break the one-way assembly rule (I1).

## Acceptance Criteria

- Toggling pause sets `Time.timeScale` to `0` and back to `1`, and
  `AudioListener.pause` to match.
- `PausedChanged` fires exactly once per state change, never on a redundant
  `SetPaused` with the same value.
- Disabling the component while paused restores `timeScale` and
  `AudioListener.pause`.
- A `PauseController` with no `inputActions` assigned throws on `Awake` with a
  message naming the component and the missing field.
- `PlayGround.GameLogic` still compiles with no reference to `PlayGround.Ui`.

## Scope Estimate

Small. One file, ~80 lines, no changes to existing code.
