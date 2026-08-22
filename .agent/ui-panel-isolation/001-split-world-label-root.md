# 001 - Split World-Label Root From HUD Assets

## Change

Move existing world-label root from HUD UXML into a dedicated authored root and
bind `MobResourceBarUi` exclusively to it.

## Implementation

- Add `Assets/Scripts/Ui/WorldLabels/WorldLabelsUi.uxml` containing exactly one
  full-screen `#labels-layer` with `PickingMode.Ignore`.
- Add a small `WorldLabelsUi.uss` for panel-filling absolute layout. Do not make
  it depend on `SkillLoadoutUi.uss`.
- Remove `#labels-layer` from
  `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`.
- Update root-stack comments in `SkillLoadoutUi.uss` to describe HUD-only
  siblings: click-to-fire, HUD, popup, pause background, pause menu.
- Update `MobResourceBarUi` setup error to require `WorldLabelsUi.uxml`, not
  `SkillLoadoutUi.uxml`.
- Update comments in `MobResourceBar.uxml` that currently describe reparenting
  into the HUD tree.
- Preserve `MobResourceBarUi` target reconciliation, pooling, visibility,
  projection, fill caching, execution order, and serialized references.
- Add no fallback query against HUD root and no conditional dual-document path.

## Acceptance Criteria

- Repository contains exactly one authored `#labels-layer`; it belongs to
  `WorldLabelsUi.uxml`.
- HUD UXML contains no mob/world-label root or repeated bar element.
- World-label root and every repeated bar descendant are picking-ignore.
- `MobResourceBarUi` fails fast when given wrong document.
- No UI controller adds projectile/AOE `EntityQuery`, entity collection, or
  per-combat-entity visual.
- Existing health source and UI-side pool remain unchanged.

## Dependencies

None. Scene becomes fully runnable after task 002 wiring.

## Scope / Complexity

Medium: two small authored assets, one root removal, and focused controller/comment
updates. Main risk is temporary scene mismatch before editor wiring.

