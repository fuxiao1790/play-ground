# 003 - Tests And Docs

## Change

- Add focused tests for authored popup structure, universal popup closing, and
  consume-before-toggle routing.
- Update `Docs/ui.md` with single-owner cancel routing and pause options popup
  behavior.
- Review diff without running Unity tests.

## Acceptance Criteria

- Tests fail if popup/button is absent or cancel consumption toggles pause.
- UI documentation matches runtime layer, ownership, and lifecycle behavior.
- User receives exact Unity platform/class/method request and required XML path.

## Dependencies

- Tasks 001 and 002.

## Scope

- Small regression-test and documentation update.
