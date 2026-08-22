# 004 - Profile Combat-Count Scaling Isolation

## Change

Produce controlled profiler evidence that HUD and world-label work do not scale
with projectile/AOE population.

## Capture Design

Use `BenchmarkLarge` after tasks 001-003.

Hold constant across captures:

- mob population and active/registered state
- camera position and zoom
- mob positions or visible-bar count
- HUD state, picker/pause state, and performance-overlay expansion state
- resolution, build/editor mode, profiler settings, and warm-up duration

Vary only active projectile/AOE population. Capture at least:

- low combat-entity steady state
- high combat-entity steady state
- repeated low control capture to establish profiler noise

Collect at least 300 post-warm-up frames per case and export CSV under
`ProfilerCaptures/` with names identifying low/high/control conditions. Follow
`Docs/profiling.md`; do not load whole CSV into review context.

## Metrics

Summarize separately:

- `HudPanel.PrepareRepaint`
- `WorldLabelsPanel.PrepareRepaint`
- `MobResourceBarUi.LateUpdate`
- `UIElements.UpdateRenderData`
- `LayoutUpdater.ComputeLayout`
- `RenderTree.UpdateTransforms`
- `UIR.NudgeVertices` call count and self time
- `UIR.GenerateEntries` call count and self time
- `WaitForJobGroupID` inclusive/self time under each panel
- GC allocations on UI controllers and panel subtrees

Also record fixed mob count, fixed visible-bar count, and active projectile/AOE
counts for each capture.

## Interpretation Rules

- Compare call cardinality and self time first; these expose actual UI work.
- Treat worker jobs displayed beneath `WaitForJobGroupID` as concurrent work,
  not calls made by UI.
- If HUD self work or call cardinality rises with combat count, isolation fails.
- If only inclusive waits rise while self work/calls remain flat, report worker
  saturation separately; do not call it an entity-dependent UI algorithm.
- World-label work may scale with mob/visible-bar count only. With those fixed,
  it must remain flat as projectile/AOE count changes.
- Use repeated-low variance as noise bound instead of inventing a universal
  millisecond threshold before baseline exists.

## Acceptance Criteria

- `HudPanel` contains no mob-bar `UIR.NudgeVertices` work.
- HUD and world-label UI call cardinalities do not increase between low and high
  combat-count captures.
- Median/p95 self-time deltas stay within repeated-low capture variance.
- `MobResourceBarUi.LateUpdate` and `WorldLabelsPanel` remain stable with fixed
  mob/visible-bar count.
- Steady-state UI paths allocate `0 B` except already-accepted fixed debug-text
  formatting; any allocation delta proportional to combat count fails.
- Results explicitly separate UI compute from scheduler wait time.

## Dependencies

Depends on tasks 001-003 and user-provided profiler captures.

## Scope / Complexity

Medium: controlled capture preparation and bounded CSV analysis. Main risk is
confounding visible mob bars, camera motion, or HUD state with combat-count ramp.

