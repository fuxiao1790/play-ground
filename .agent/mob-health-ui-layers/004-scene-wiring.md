# 004 — Wire Layered UI And Mob Health In Target Scene

## Change

Wire runtime UI through Unity Inspector in `Assets/Scenes/BenchmarkLarge.unity`.
Do not hand-edit scene YAML or `.meta` files.

## Wiring

- Add/enable `PauseMenuUi` on the existing `GameUI` object.
- Assign existing `UIDocument`, pause-menu template, pause stylesheet, and
  `PauseController`.
- Add `MobHealthBarUi` on `GameUI`.
- Assign existing combat root and gameplay camera.
- Assign new mob-health template and authored world offset.
- Confirm `UIDocument` still uses `SkillLoadoutUi.uxml` and existing
  `PanelSettings`.
- Do not add Canvas, extra UIDocument, mob-prefab health components, or scene-wide
  lookup fallbacks.

## Acceptance Criteria

- Scene enters play without missing-reference or missing-layer exceptions.
- Existing HUD, performance panel, skill picker, resource bars, click-to-fire,
  and pause behavior remain wired.
- Active mobs display projected bars in target scene.
- Hierarchy/Inspector contains one shared `UIDocument` for this stack.
- Scene changes are produced by Unity serialization through Inspector/editor
  tooling, not textual YAML edits.

## Dependencies

- Depends on tasks 001–003.

## Scope / Complexity

Low: Inspector wiring and visual default tuning; risk comes from required
reference completeness rather than code volume.

