# 004 - Universal Popup Cancel

## Change

- Generalize pause-specific cancel-handler naming to `IUiCancelHandler`.
- Register handlers only while their popup is active, making registration order
  match popup stacking order.
- Make `SkillLoadoutUi` register its picker and close it through same Escape
  path as pause options popup.
- Keep pause toggle as fallback when active-popup stack is empty.

## Acceptance Criteria

- Escape closes skill picker without pausing gameplay.
- Escape closes options popup without resuming gameplay.
- Next Escape after popup closes toggles pause normally.
- Future popup owners can implement same interface without new input readers or
  pause-specific branches.
- Newest active handler receives cancel before older handlers.

## Dependencies

- Tasks 001 through 003.

## Scope

- Small cancel-routing generalization and second popup integration.
