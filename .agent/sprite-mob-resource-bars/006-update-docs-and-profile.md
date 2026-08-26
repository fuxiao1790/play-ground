# 006 — Update Architecture And Profile New Path

## Change

Update documentation to describe sprite bars as actor presentation rather than
UI Toolkit world labels:

- `Docs/ui.md`: remove second-panel/runtime world-label contract; retain HUD UI
  Toolkit and input layering; point mob bars to scene/actor presentation.
- `Docs/folder-structure.md`: remove WorldLabels folder/panel entries.
- `Docs/contracts/game-settings-data.md`: replace `MobResourceBarUi` consumer
  with child sprite presenter.
- `Docs/layers/scene-and-authoring.md` and
  `Docs/layers/presentation-and-feedback.md`: record prefab-child ownership and
  event-driven health projection.
- `Docs/profiling.md`: remove UI Toolkit marker guidance and document sprite-bar
  comparison method.
- Any stale architecture/test comments found by final symbol search.

Profile three comparable BenchmarkLarge captures after warm-up:

1. retained UI Toolkit baseline capture/commit;
2. current no-bar scene baseline;
3. sprite-bar implementation.

User prepares/saves profiler and Inspector state. Agent may analyze exported
captures read-only but does not alter scene/prefab settings to create profile
variants.

Use same resolution, camera, spawn cap, mob count, movement phase, health-change
phase, and capture duration. Report separately:

- main-thread frame time and relevant `MobRoot`/presentation self time;
- render-thread time;
- batches/set-pass calls/draw calls and triangles;
- GC allocation;
- moving-with-static-health versus regenerating/changing-health phases;
- confirmation that `MobResourceBarUi.LateUpdate`,
  `WorldLabelsPanel.PrepareRepaint`, and world-label layout/transform work are
  absent.

Use Unity GPU Profiler or Frame Debugger for GPU/render behavior; do not infer GPU
improvement from CPU CSV alone.

## Acceptance Criteria

- Docs describe exactly one current mob-resource-bar implementation.
- Search finds no stale old controller/panel/offset references outside history or
  archived captures.
- Profile report compares all baselines under matching conditions and separates
  motion from health-change costs.
- Sprite implementation has zero managed bar-position updates during movement.
- No pass/fail performance claim is made until user supplies captures and the
  exported evidence is reviewed.
- Test result XML is stored as `Logs/TestResults-EditMode-SpriteMobBars.xml` and
  `Logs/TestResults-PlayMode-SpriteMobBars.xml` when user runs validation.

## Dependencies

- 001–005 complete.

## Scope / Complexity

Medium: cross-document cleanup plus controlled CPU/GPU profiling comparison.

