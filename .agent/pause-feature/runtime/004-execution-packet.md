# Task Execution Packet

## Task

004-pause-menu-ui.md

## Goal

Add pause overlay template and UI controller that projects `PauseController.IsPaused` and resumes through its public API.

## Files Allowed To Create

- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.cs`
- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.uxml`
- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.uss`

## Files Allowed To Modify

- None.

## Behavior To Preserve

- Skill bar and picker remain reachable when overlay is visible.
- World input blocking stays task 003's responsibility.
- UI owns no duplicate pause state and has no per-frame work.

## Behavior To Change

- Pause state shows an overlay with a Resume button; clicking it calls `SetPaused(false)`.

## Relevant Global Context

- UI can depend on Game Logic, not reverse.
- Controller validates serialized references in `Awake`.
- Clone template once in `OnEnable`, own/remove it in `OnDisable`, cache/unregister button callback.
- Use USS class for visibility; UXML structure/names and USS visual values only.

## Dependencies Confirmed

- Task 001 `PauseController` exposes `IsPaused`, `PausedChanged`, `SetPaused`.
- Task 003 owns all world click-through blocking.

## Step-By-Step Instructions

1. Create `PauseMenuUi` under `PlayGround.Ui` with serialized UIDocument, template, stylesheet, and pause controller; validate each with clear errors matching HUD style.
2. In `OnEnable`, resolve HUD root, clone template, attach stylesheet, cache `#resume`, register click callback, subscribe and set initial visibility.
3. In `OnDisable`, unsubscribe, unregister callback, and remove only cloned element.
4. Toggle a USS class in pause event; Resume calls `pauseController.SetPaused(false)` and does not change visibility itself.
5. Keep overlay layout clear of skill bar and its decoration non-picking.

## Acceptance Criteria

- Repeated pause toggles do not duplicate cloned UI.
- Resume hides only via pause event.
- Overlay allows skill bar/picker while world clicks are blocked by task 003.
- No `Update` method.
- Missing reference errors name fields.

## Validation Required

- Static source inspection only. Scene wiring/manual verification and Unity test execution are user actions.

## Hard Boundaries

- Do not modify root HUD UXML/USS, scene/prefab/assets/meta, or task-003 input behavior.
- No direct pause state model or UI->ECS behavior.
