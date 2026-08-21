# 002 — Route Existing UI Through Named Layers

## Change

Refactor existing UI controllers to bind authored layers instead of dynamically
creating/reordering root children.

## GameplayInputSurface

- Query `#click-to-fire-layer` during `OnEnable`.
- Register pointer callbacks during `OnEnable`; unregister during `OnDisable`.
- Remove `CreateSurfaceIfNeeded`, `InsertSurfaceAtRootStart`, and hierarchy
  removal/reinsertion behavior.
- Preserve pointer capture, pause handling, and `PlayerRoot` input binding.
- Fail fast when layer is missing.

## SkillLoadoutUi

- Cache `#popup-layer` during `OnEnable` and validate it with existing HUD nodes.
- Add skill picker modal to popup layer instead of `rootVisualElement`.
- Preserve `GameplayInputBlock.SkillPicker` behavior and modal cleanup.

## PauseMenuUi

- Query and validate `#pause-background` and `#pause-menu-layer`.
- Instantiate existing pause-menu template into pause-menu layer.
- On pause: show background, set it to `PickingMode.Position`, and show menu.
- On resume: hide background, set it to `PickingMode.Ignore`, and hide menu.
- Keep menu layer pass-through except interactive controls.
- Detach only template content owned by controller during `OnDisable`; authored
  layer nodes remain in document.

## Acceptance Criteria

- No affected controller uses `root.Add(...)`, `root.Insert(...)`, or
  `BringToFront()` for layer ordering.
- Click-to-fire remains functional through decorative HUD/label space.
- Skill picker renders above HUD and below pause background.
- Pausing visually dims popup/HUD/labels and prevents their pointer input.
- Resume button remains interactive above pause background.
- Disable/re-enable does not duplicate callbacks or leave pointer capture held.
- Missing named layer produces clear setup exception.

## Dependencies

- Depends on task 001 layer names and UXML structure.

## Scope / Complexity

Medium: three focused controller lifecycle refactors with input-sensitive
behavior.

