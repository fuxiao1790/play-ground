# Implementation Log

## Status
Blocked on user action (tasks 001-003 complete; 002 needs user test run, 004 needs user profiler capture)

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-refactor-dynamic-transform-path.md | Complete | See below |
| 002-add-world-label-transform-regressions.md | Complete (code); tests not yet run | See below |
| 003-document-dynamic-world-label-contract.md | Complete | See below |
| 004-validate-profiler-budget.md | Pending | Requires user profiler capture; agent cannot complete alone |

## Completed Tasks
- **001-refactor-dynamic-transform-path.md**
  - Files changed: `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`, `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss`.
  - `CreateEntry` now sets `marker.usageHints |= UsageHints.DynamicTransform;` and `fill.usageHints |= UsageHints.DynamicTransform;` on the detached tree, before `AcquireEntry` calls `labelsLayer.Add`.
  - `Project` replaced `entry.Marker.transform.position = ...` with `entry.Marker.style.translate = new Translate(panelPosition.x, panelPosition.y);`, and replaced `entry.Fill.style.width = Length.Percent(ratio * 100f)` with `entry.Fill.style.scale = new Scale(new Vector2(ratio, 1f));`.
  - `AcquireEntry` resets fill via `entry.Fill.style.scale = new Scale(Vector2.one);` instead of resetting width.
  - `MobResourceBar.uss`: `.mob-resource-bar__fill` now has fixed `width: 100%` and `transform-origin: 0% 50%`; header comment rewritten to describe `style.translate`/`style.scale` + `UsageHints.DynamicTransform` instead of `VisualElement.transform.position`.
  - No UXML, registry, ECS, health, settings, or pooling-structure changes made; dictionary/stack pool, execution order 500, lifecycle methods, and picking-transparency helper untouched.
  - Acceptance criteria: all met per static review — no remaining `transform.position` or `style.width` writes in the file (confirmed via grep); hints assigned before attachment; ratio-driven scale preserves bar bounds; no new allocations (`Translate`/`Scale`/`Vector2` are value types).
  - Validation: static code review only. Agent did not and cannot run Unity compilation or PlayMode tests (user-run step per project process). User should open the project in Unity/rely on task 002's PlayMode tests to confirm compile + runtime behavior.
  - Deviations: none.

- **002-add-world-label-transform-regressions.md**
  - Files changed: `Assets/Tests/PlayMode/UiPanelIsolationPlayModeTests.cs`.
  - Added usings: `System.Collections.Generic`, `PlayGround.System.Combat.Targets`, `Unity.Entities`.
  - Fixed `RegisteredMobCreatesBarOnlyInWorldLabelsPanel`: now counts elements named `"marker"` via `labelsLayer.Query<VisualElement>("marker").ToList().Count` before/after registration instead of `CountDescendants` (which double/triple-counted the `bar`+`fill` descendants under one marker). Wrapped mob creation/assertions in try/finally so a failed assertion still destroys the test mob.
  - Added `RegisteredMobBarUsesDynamicUiToolkitTransforms`: places a test mob in front of `MobResourceBarUi`'s `gameplayCamera`, registers it through the real `CombatRoot.TargetRegistry`, diffs the `#labels-layer` marker set (by reference) to find the newly created marker, then asserts: marker and `#fill` both have `usageHints.HasFlag(UsageHints.DynamicTransform)`; all marker descendants remain `PickingMode.Ignore` (reusing existing `AssertPickingIgnoreRecursive` helper); moving the mob's world transform changes `marker.style.translate.value` within a bounded 10-frame poll; after polling up to 30 frames for `mob.CombatTargetProxy` to become non-`Entity.Null`, calling `CombatTargetProxy.SetHealth(mob, mob.MaxHealth * 0.5f)` and polling up to 30 more frames drives `fill.style.scale.value.value.x` to `0.5` while `.y` stays `1`. All health/position values are read from the public UI tree and public `MobRoot`/`CombatTargetProxy` API — no private runtime state or new production hooks were added. Mob is destroyed in a `finally` block.
  - Deviations: none from task spec. Task said "inspect public UI tree and public usageHints/style values" — implemented by diffing the queryable `#labels-layer` marker set rather than reaching into `MobResourceBarUi`'s private `entriesByMob` dictionary via reflection, since that dictionary holds a private nested type (`ResourceBarEntry`) and the task explicitly disallows exposing private runtime state.
  - Validation: **Not run by agent.** Per project process (`Docs/testing.md` *Running Tests*) and this plan's own "User-Run Verification" section, PlayMode tests must be run by the user in the Unity Editor, exporting XML to `Logs/TestResults-PlayMode-MobHealthBarUiPerformance.xml` for methods `RegisteredMobCreatesBarOnlyInWorldLabelsPanel` and `RegisteredMobBarUsesDynamicUiToolkitTransforms`. Agent performed static/code review only (types, member names, and API shapes cross-checked against `MobResourceBarUi.cs`, `MobRoot.cs`, `CombatTargetProxy.cs`, `Resource.cs`, and `TargetProxyCreateApplySystem.cs`) and cannot claim pass/fail without that XML.
  - Acceptance criteria: cannot be marked Done until user-run XML confirms both tests pass; code-level criteria (marker-specific counting, no reflection into private state, mob destroyed safely) are met per review.

- **003-document-dynamic-world-label-contract.md**
  - Files changed: `Docs/ui.md`, `Docs/profiling.md`.
  - `Docs/ui.md`: added a new "World-Label Dynamic Transform Contract" subsection under *Lifecycle And Performance*, covering: `style.translate` + `UsageHints.DynamicTransform` (assigned pre-attachment) for marker position; fixed-geometry fill + left-anchored `style.scale` for health (fill width never mutated at runtime); no `GroupTransform` on the stationary `#labels-layer`; marker/fill pooling and recursive `PickingMode.Ignore` remain required; reading `MobResourceBarUi.LateUpdate` together with `WorldLabelsPanel.PrepareRepaint` when profiling.
  - Checked `Docs/ui.md` (and the rest of `Docs/`) for stale notes describing `VisualElement.transform.position` or dynamic fill width — none existed, so no correction was needed there.
  - `Docs/profiling.md`: added a "World-Label Query Guidance" section (before "Grep Approach") listing the world-label-relevant markers (`WorldLabelsPanel.PrepareRepaint`, `MobResourceBarUi.LateUpdate`, `UIR.NudgeVertices` nested under the panel, `RenderTree.UpdateTransforms`, `LayoutUpdater.ComputeLayout`/`UpdateSubTree`), instructing controller+panel cost be summed, phases (stationary/moving/health-changing) be compared separately, and explicitly noting the CSV is CPU-only evidence (GPU cost needs Frame Debugger/GPU profiler).
  - Deviations: none. No claim was added that GPU time is measured by the CPU profiler CSV, matching the task's explicit exclusion.
  - Validation: doc content cross-checked against the task-001 implementation actually landed in `MobResourceBarUi.cs`/`MobResourceBar.uss` (e.g. `transform-origin: 0% 50%`, `UsageHints.DynamicTransform` on both marker and fill) so the contract describes the real code, not the pre-refactor behavior. No build/test step applies to doc-only changes.

## Blockers
- **002 validation is blocked on a user step**: agent cannot run Unity PlayMode tests. Task 004 additionally requires a user profiler capture and cannot be completed by the agent under any circumstance. Tasks 001-003 are otherwise done; 004 cannot start meaningfully until the user runs the 002 tests and provides a new profiler capture.

## Validation Summary
- 001: Static review passed (no old API usage remains, structure matches task spec exactly). Unity compile/runtime not verified by agent — pending user confirmation, to be exercised concretely by task 002's PlayMode tests.
- 002: Static/code review passed. Actual test execution (compile + pass/fail) requires the user to run PlayMode tests in Unity and export XML per `Docs/testing.md`.
- 003: Doc content reviewed against actual task-001 code; no test/build step applies.
