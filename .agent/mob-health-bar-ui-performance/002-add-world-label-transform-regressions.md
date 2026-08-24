# 002 — Add World-Label Transform Regressions

## Goal

Protect UI Toolkit-only rendering path, panel ownership, transform hints,
movement, and health-fill behavior without asserting profiler timings in NUnit.

## Changes

### `Assets/Tests/PlayMode/UiPanelIsolationPlayModeTests.cs`

1. Correct `RegisteredMobCreatesBarOnlyInWorldLabelsPanel`:
   - count elements named `marker` directly under/querying `#labels-layer`, not
     every descendant;
   - retain assertion that exactly one marker appears for registered mob;
   - retain assertion that HudPanel element count does not change.
2. Add `RegisteredMobBarUsesDynamicUiToolkitTransforms`:
   - place test mob at a visible camera position;
   - register through real `CombatRoot.TargetRegistry`;
   - wait for marker creation;
   - assert marker and `#fill` include `UsageHints.DynamicTransform`;
   - assert all marker descendants remain `PickingMode.Ignore`;
   - move mob world transform, wait for presentation update, and assert marker
     `style.translate` changes;
   - update authoritative proxy health through
     `CombatTargetProxy.SetHealth`, wait for managed `MobRoot.CurrentHealth` and
     presentation to catch up, then assert fill X scale matches health ratio
     while Y scale remains `1`.
3. Ensure each created mob is destroyed in `finally`/teardown-safe cleanup so a
   failed assertion does not leak registration into later tests.
4. Do not add production test hooks or expose private runtime state.

### Test semantics

- Inspect public UI tree and public `usageHints`/style values.
- Drive health through existing target-proxy contract rather than modifying a
  second local health representation.
- Allow bounded frame polling for ECS health propagation; fail with a clear
  timeout message.
- Do not assert milliseconds or profiler marker counts in NUnit.

## Acceptance Criteria

- Existing panel-isolation and picking assertions remain.
- Marker-count test reflects actual marker count regardless of bar/fill
  descendants.
- New test fails if marker returns to CPU-only position path, hints are removed,
  fill returns to width mutation, world movement stops projecting, or health
  ratio stops updating.
- Test creates no alternate health owner and changes no production API.

## User-Run Verification

- Platform: PlayMode.
- Class: `PlayGround.Tests.PlayMode.UiPanelIsolationPlayModeTests`.
- Methods:
  - `RegisteredMobCreatesBarOnlyInWorldLabelsPanel`
  - `RegisteredMobBarUsesDynamicUiToolkitTransforms`
- Export result XML:
  `Logs/TestResults-PlayMode-MobHealthBarUiPerformance.xml`.

Agent reviews XML before reporting any pass/fail result.

## Dependencies

- Depends on task 001.

## Estimated Scope / Complexity

Medium. Existing scene integration test plus ECS-to-managed health propagation.
