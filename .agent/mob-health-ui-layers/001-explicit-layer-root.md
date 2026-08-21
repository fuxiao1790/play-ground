# 001 — Author Explicit UI Layer Root

## Change

Refactor `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml` so its root has this stable
back-to-front sibling order:

1. `#click-to-fire-layer`
2. `#labels-layer`
3. `#hud-container`
4. `#popup-layer`
5. `#pause-background`
6. `#pause-menu-layer`

Keep existing HUD children inside `#hud-container`. Add full-screen layer styles
to `SkillLoadoutUi.uss`; include explicit hidden and visible pause-background
classes. Record the order and picking contract in `Docs/ui.md`, replacing the
current two-layer model.

## Details

- Click layer fills panel and defaults to `PickingMode.Position`.
- Labels layer fills panel and uses `PickingMode.Ignore`.
- HUD container preserves current layout/padding and remains pass-through except
  for interactive descendants.
- Popup layer fills panel and ignores picking; popup controls opt in.
- Pause background fills panel, dims lower layers, and is hidden initially.
- Pause menu layer fills panel and ignores picking; menu controls opt in.
- Root layers must not depend on `z-index` or controller execution timing.
- Document sibling draw order, picking rules, and named ownership.

## Acceptance Criteria

- UXML contains every named layer exactly once and in specified order.
- Existing HUD hierarchy and styles remain under `#hud-container`.
- Full-screen layers cover panel without inheriting HUD side padding.
- `Docs/ui.md` specifies requested front-to-back order and owner of each layer.
- No scene, gameplay, or ECS type references UI layering.

## Dependencies

None.

## Scope / Complexity

Medium: one root UXML refactor, shared USS updates, and authoritative UI
documentation update.

