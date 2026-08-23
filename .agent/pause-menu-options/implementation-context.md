# Implementation Context

## Architectural Decisions
- `PauseController` stays sole `UI/Cancel` reader; offers cancel to registered
  `IPauseCancelHandler`s newest-first, toggles pause only if none consume it.
- `PauseMenuUi` owns options-popup presentation state; registers/unregisters
  in `OnEnable`/`OnDisable`.
- Menu/popup hierarchy authored in UXML; layout/visibility class-driven in
  USS; C# wires intent and toggles classes only.
- Popup stays under `#pause-menu-layer`, frontmost sibling, position-picks
  while visible.

## Global Invariants
- UI (`PlayGround.Ui`) may depend on Game Logic; Game Logic must never
  reference UI types.
- `#pause-menu-layer` stays frontmost authored HUD layer.
- No ECS data/jobs/structural changes.
- Registration lives in `OnEnable`/`OnDisable`, never `Awake`/`OnDestroy`.
- UI work stays fixed-size, never scales with projectile/AOE/entity count.

## Ownership Boundaries
- Pause runtime state (`IsPaused`, `Time.timeScale`, `AudioListener.pause`):
  `PauseController` (Game Logic).
- Popup visibility/presentation: `PauseMenuUi` (UI).

## Data Flow
`UI/Cancel` -> `PauseController.HandleCancel` -> newest registered
`IPauseCancelHandler.TryHandlePauseCancel` -> fallback `TogglePause`.

## Lifecycle / Allocation Rules
- `RegisterCancelHandler`/`UnregisterCancelHandler` paired with
  `OnEnable`/`OnDisable` in `PauseMenuUi`.
- Popup force-closed on pause end (`OnPausedChanged(false)`) and on disable.

## ECS / Job / Threading Constraints
- None touched; pure managed UI/Game Logic path.

## Determinism Requirements
- N/A (presentation only).

## Producer / Consumer Separation
- N/A for this feature (no event payloads introduced).

## Reused Mechanisms
- Existing `UI/Cancel` action, `PauseController` pause-state authority.
- Existing `#pause-menu-layer`/`#pause-background` layer stack.
- Existing pause-menu UXML-template clone, USS hidden-class pattern.

## Introduced Mechanisms
- `IPauseCancelHandler` contract (Game Logic, no UI type).
- `#options-popup` authored element + `options-popup--hidden` class. No
  settings data model.

## Validation Requirements
- Static/code-evidence review (agents do not run Unity tests).
- User exports EditMode/PlayMode XML under `Logs/` per `Docs/testing.md`;
  agent reviews XML before claiming pass.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Game/PauseController.cs`
- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.cs`
- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.uxml`
- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.uss`
- `Assets/Tests/EditMode/UiPanelIsolationEditModeTests.cs`
- `Assets/Tests/PlayMode/UiPanelIsolationPlayModeTests.cs`
- `Docs/ui.md`
