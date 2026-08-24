# Implementation Context

## Architectural Decisions
- Refactor existing `MobResourceBarUi` path; no new renderer, panel, ECS presentation path, or duplicate health model.
- Marker (not child bar) carries transform; child bar keeps its own `translate: -50% -100%` centering.
- `UsageHints.DynamicTransform` on marker and fill only, assigned before attachment; no `GroupTransform` on marker or `#labels-layer`.
- Position via `style.translate` (not `VisualElement.transform.position`).
- Health via fixed-width fill + left-anchored `style.scale` X (not `style.width`).
- Projection/registration path (`RuntimePanelUtils.CameraTransformWorldToPanel`, target registry) unchanged in this plan.
- No UXML hierarchy changes, no custom mesh/quad, no panel/scene asset edits.

## Global Invariants
- UI is presentation-only: reads `MobRoot` health/anchor, never owns health or makes combat decisions.
- Dependency direction: `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`; Sim never references UI.
- Bars stay in `WorldLabelsPanel` (sort order -10), separate from `HudPanel`.
- `#labels-layer`, marker, bar, fill remain `PickingMode.Ignore` (recursive).
- UXML = structure, USS = authored visual constants, C# = runtime position/health values only.
- `DefaultExecutionOrder(500)` / `LateUpdate` ordering preserved (runs after camera).
- `Dictionary<MobRoot, ResourceBarEntry>` + `Stack<ResourceBarEntry>` pooling mechanism unchanged; disable removes from hierarchy but keeps pooled elements.
- Usage hints must be set while marker/fill are detached (in `CreateEntry`, before `labelsLayer.Add`).
- No per-frame LINQ, scene search, template instantiation for reused entries, or new parallel collections.
- `GameSettings.DisplayMobHealthBarsChanged(false)` releases markers; disabled state skips iteration.

## Ownership Boundaries
- `MobRoot` remains authored world-anchor and health source (`ResourceBarAnchorPosition`, `CurrentHealth`, `MaxHealth`).
- UI only caches presentation values already present in `ResourceBarEntry` (e.g. `FillRatio`).

## Data Flow
- `WorldToPanel -> marker.style.translate` (replaces `-> ITransform.position`).
- `health ratio -> fill.style.scale` X-axis (replaces `-> style.width`).

## Lifecycle / Allocation Rules
- `Awake` validates serialized refs; `OnEnable` gets panel tree + subscribes; `OnDisable` unsubscribes + releases runtime elements.
- `Translate`, `Scale`, `Vector2` are value types — no new heap allocation in `LateUpdate`.

## ECS / Job / Threading Constraints
- None directly (UI-only change); health read via `MobRoot.CurrentHealth`/`MaxHealth` already bridged from ECS via `CombatTargetProxy`.

## Determinism Requirements
- None beyond preserving existing frame-order guarantees (execution order 500, LateUpdate).

## Producer / Consumer Separation
- Sim/ECS produces health values via `CombatTargetProxy` -> `MobRoot`; UI only consumes for presentation.

## Reused Mechanisms
- `WorldLabelsPanel`, `#labels-layer`, `MobResourceBar.uxml`, `MobResourceBar.uss`.
- `MobResourceBarUi` projection loop, `DefaultExecutionOrder(500)`.
- `ResourceBarEntry` cache, marker dictionary, stack pool.
- Viewport visibility / `DisplayStyle.None`.
- `GameSettings.DisplayMobHealthBarsChanged` release/skip flow.
- Recursive picking transparency.

## Introduced Mechanisms
- `UsageHints.DynamicTransform` on marker + fill (existing elements, new hint).
- `style.translate` / `style.scale` writes replacing `transform.position` / `style.width`.
- New regression tests for hints, translate movement, scale-based fill.

## Validation Requirements
- Agent must NOT run Unity tests (editor/PlayMode execution is a user step). User runs named PlayMode tests and exports XML to `Logs/`; XML is sole test-result evidence.
- Compile check can be attempted via static review / Unity compile if tooling allows; otherwise report inability honestly.
- Profiler timing validation (task 004) requires user-provided capture; not automatable by agent.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs` (task 001)
- `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss` (task 001)
- `Assets/Tests/PlayMode/UiPanelIsolationPlayModeTests.cs` (task 002)
- `Docs/ui.md` (task 003)
- `Docs/profiling.md` (task 003)
- `ProfilerCaptures/` new CSV + `.agent/mob-health-bar-ui-performance/profiling-results.md` (task 004, user-driven)

## Task Order
1. 001-refactor-dynamic-transform-path.md (no deps)
2. 002-add-world-label-transform-regressions.md (depends on 001)
3. 003-document-dynamic-world-label-contract.md (depends on 001)
4. 004-validate-profiler-budget.md (depends on 001-003, requires user profiler capture — cannot be completed by agent alone)
