# 002 - Author Options Popup

## Change

- Center `.pause-menu` in both axes.
- Add Options button to pause panel.
- Add empty, hidden, full-screen options-popup overlay and centered blank panel
  after the menu panel in UXML.
- Cache and wire options controls in `PauseMenuUi`.
- Implement `IPauseCancelHandler`; consume cancel only when popup is visible.
- Close popup whenever pause ends or component disables.

## Acceptance Criteria

- Pause panel is centered without fixed top padding.
- Options button opens visible blank popup above menu controls.
- Popup blocks pointer interaction with menu underneath.
- First Escape with popup open closes popup and keeps game paused.
- Next Escape toggles pause normally.
- Resume behavior remains unchanged.

## Dependencies

- Task 001.

## Scope

- Small UI controller and authored UXML/USS change.

