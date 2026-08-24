# 004 — Validate 200-Bar Profiler Budget

## Goal

Confirm dynamic-transform refactor removes measured CPU vertex and layout cost
under equivalent 200-bar load.

## User Profiling Matrix

Capture one development/editor profiling session containing labeled or clearly
separated phases after warm-up:

1. `200` registered, visible bars; actors and camera stationary.
2. Same `200` visible bars; camera or actors moving so marker positions change
   every frame.
3. Same movement while health ratios change.
4. Mob health bars disabled through `GameSettings` toggle.
5. Mob health bars re-enabled to exercise pooled reattachment.

Export CPU profiler data to a new CSV under `ProfilerCaptures/`. Preserve
baseline capture; do not overwrite it.

## Analysis

For frames after warm-up, summarize median, p95, maximum, calls, and GC for:

- `MobResourceBarUi.LateUpdate`;
- `WorldLabelsPanel.PrepareRepaint`;
- `RenderTree.UpdateTransforms` beneath world-label panel;
- `UIR.NudgeVertices` beneath world-label panel;
- `LayoutUpdater.ComputeLayout`;
- `LayoutUpdater.UpdateSubTree`;
- combined controller plus panel cost per frame.

Compare against baseline recorded in `index.md`. Treat CPU CSV as CPU evidence
only; use Frame Debugger/GPU profiler separately if GPU cost becomes relevant.

## Acceptance Criteria

Equivalent 200-visible-moving-bar phase meets all:

- median `UIR.NudgeVertices <= 0.10 ms`;
- median `UIR.NudgeVertices` calls `<= 34`;
- median `WorldLabelsPanel.PrepareRepaint <= 1.75 ms`;
- median combined controller + panel `<= 2.75 ms`;
- stationary frames without spawn/toggle/health changes attribute `0 B` GC to
  `MobResourceBarUi.LateUpdate`;
- no visual displacement, fill-direction, toggle, pooling, or pointer-routing
  regression is observed.

If any threshold fails, keep task open and record exact failing marker. Do not
switch to custom rendering. Start next investigation from remaining marker:

- transform cost: verify effective usage hints on live markers;
- layout cost: verify no runtime width/height mutation remains;
- controller cost: isolate double projection and stale dictionary scan;
- offscreen hierarchy cost: measure visible-only attachment as separate UI
  Toolkit refactor.

## Result Recording

Add implementation-time `profiling-results.md` in this task directory with:

- new capture path;
- Unity/editor or player target and build configuration;
- phase frame ranges;
- baseline vs new table;
- pass/fail per acceptance criterion;
- remaining hotspot, if any.

## Dependencies

- Depends on tasks 001-003.
- Requires user-generated profiler capture.

## Estimated Scope / Complexity

Medium. Runtime capture plus bounded CSV analysis; no code changes expected.
