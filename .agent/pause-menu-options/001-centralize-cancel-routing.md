# 001 - Centralize Cancel Routing

## Change

- Add `IPauseCancelHandler` beside `PauseController` in Game Logic.
- Add idempotent registration/unregistration methods.
- Route `UI/Cancel` to registered handlers in reverse registration order.
- Toggle pause only if every handler declines the request.
- Preserve `SetPaused` and `TogglePause` behavior.

## Acceptance Criteria

- Only `PauseController` reads `UI/Cancel`.
- Consumed cancel leaves `IsPaused`, `Time.timeScale`, and
  `AudioListener.pause` unchanged.
- Unconsumed cancel follows existing pause toggle path.
- Duplicate registrations do not cause duplicate calls.
- No `PlayGround.Ui` dependency enters Game Logic.

## Dependencies

- None.

## Scope

- Small runtime refactor; low risk, fixed-count managed path.

