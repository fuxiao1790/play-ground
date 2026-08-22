# 002 - Author And Wire Distinct Runtime Panels

## Change

Create distinct HUD and world-label runtime panels through Unity Editor and wire
`BenchmarkLarge` without textual scene/meta edits.

## Unity Asset Operations

- Rename `Assets/UI/SkillPanel.asset` to `HudPanel.asset` through Unity so its
  GUID and existing scene reference remain valid and profiler marker becomes
  `HudPanel`.
- Duplicate HUD panel settings to `Assets/UI/WorldLabelsPanel.asset` through
  Unity.
- Keep theme, scale mode, reference resolution, DPI, target display, atlas,
  shader, and color settings identical.
- Set panel sorting:
  - `WorldLabelsPanel`: `-10`
  - `HudPanel`: `0`
- Do not point both documents at one PanelSettings asset; that would recreate
  one runtime panel.

## Scene Wiring

- Existing `GameUI` retains HUD `UIDocument`, `SkillLoadoutUi`, `ResourceBarUi`,
  `PerformanceUi`, `GameplayInputSurface`, and `PauseMenuUi`.
- HUD document source remains `SkillLoadoutUi.uxml` and uses `HudPanel`.
- Add sibling scene object `WorldLabelsUI`.
- Add one `UIDocument` using `WorldLabelsUi.uxml` and
  `WorldLabelsPanel.asset`.
- Move the existing `MobResourceBarUi` component from `GameUI` to
  `WorldLabelsUI` so component and document ownership are co-located.
- Assign its document, combat root, gameplay camera, resource-bar template, and
  stylesheet references.
- Preserve existing EventSystem/InputSystemUIInputModule.
- Do not add a Canvas, per-mob documents, or a second
  `MobResourceBarUi` instance.

## Runtime Ordering And Input Checks

- HUD draws over world labels.
- Pause background dims world labels because HUD panel sorts above label panel.
- Label panel contains no focusable/pickable element.
- Pointer input passes through label panel to HUD click-to-fire surface.
- `hudDocument.runtimePanel` and `worldLabelsDocument.runtimePanel` are distinct.

## Acceptance Criteria

- Profiler exposes separate `HudPanel.PrepareRepaint` and
  `WorldLabelsPanel.PrepareRepaint` samples.
- Mob damage/position changes never dirty HUD visual tree.
- Skill/pause/player-resource changes never traverse mob bar elements.
- Scene enters Play Mode without missing-document/layer/reference exceptions.
- Existing skill picker, pause shielding, resource HUD, performance overlay, and
  click-to-fire behavior remain functional.
- Scene and asset references are serialized by Unity; no scene YAML or `.meta`
  file is hand-edited.

## Dependencies

Depends on task 001 assets and controller contract.

## Scope / Complexity

Medium: editor-authored asset rename/duplication plus scene wiring. Input and
sort-order verification are integration-sensitive.

