# Task Execution Packet

## Task
001-refactor-dynamic-transform-path.md

## Goal
Move mob-bar position and health updates onto documented UI Toolkit dynamic transforms (`style.translate` + `UsageHints.DynamicTransform` for position; `style.scale` for health) without changing element structure, ownership, pooling, or panel wiring.

## Files Allowed To Modify
- Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs
- Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs (already read in full)
- Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss

## Behavior To Preserve
- World anchor, viewport culling, visibility, `RuntimePanelUtils.CameraTransformWorldToPanel` conversion.
- Ratio clamp and `Mathf.Approximately` cache guard.
- `ResourceBarEntry`, dictionary, stale scan, stack pool, settings event, execution order (500), lifecycle (Awake/OnEnable/OnDisable).
- Bar dimensions, background, border, clipping, health color in USS.
- Picking transparency (recursive Ignore) — untouched by this task.

## Behavior To Change
- `CreateEntry`: assign `UsageHints.DynamicTransform` to `marker` and to `fill`, while detached, before any future `labelsLayer.Add`. No `GroupTransform` anywhere.
- `Project`: replace `entry.Marker.transform.position = ...` with `entry.Marker.style.translate = new Translate(panelX, panelY)`. Replace `entry.Fill.style.width = Length.Percent(ratio * 100f)` with `entry.Fill.style.scale = new Scale(new Vector2(ratio, 1f))`.
- `AcquireEntry`: reset fill scale to `Vector2.one` (via `new Scale(Vector2.one)`) instead of resetting inline width.
- USS `.mob-resource-bar__fill`: fixed `width: 100%`; `transform-origin: 0% 50%` (left-center) so X scale drains/fills left-to-right; update stale comment mentioning `VisualElement.transform.position` to describe `style.translate` + `DynamicTransform`.

## Relevant Global Context
- UI presentation-only; `MobRoot` remains sole health/anchor source.
- Dependency direction `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim` unaffected (no Sim changes).
- No per-frame LINQ/allocation; `Translate`/`Scale`/`Vector2` are value types, safe for `LateUpdate`.
- Usage hints must be set on detached elements only (before `labelsLayer.Add`).

## Dependencies Confirmed
- None (task 001 has no dependencies — first task in plan).

## Step-By-Step Instructions
1. In `CreateEntry` (currently constructs `marker`/`fill` and returns `ResourceBarEntry`), after finding `fill` and before `return`, set `marker.usageHints |= UsageHints.DynamicTransform;` and `fill.usageHints |= UsageHints.DynamicTransform;`.
2. In `Project`, replace the `entry.Marker.transform.position = new Vector3(...)` line with a `style.translate` assignment using the panel-space X/Y. Replace the `entry.Fill.style.width = Length.Percent(ratio * 100f)` line with `entry.Fill.style.scale = new Scale(new Vector2(ratio, 1f))`.
3. In `AcquireEntry`, replace `entry.Fill.style.width = Length.Percent(100f)` with `entry.Fill.style.scale = new Scale(Vector2.one)`.
4. In `MobResourceBar.uss`, update `.mob-resource-bar__fill` to fixed `width: 100%` and `transform-origin: 0% 50%`; update any comment describing the old `transform.position`/width approach.

## Acceptance Criteria
- Marker and fill receive `UsageHints.DynamicTransform` before marker joins a panel.
- No `Marker.transform.position` write remains; position goes through `style.translate`.
- No runtime `Fill.style.width` write remains; health goes through `style.scale`.
- At ratios 0, 0.5, 1, fill is empty/half/full from the left edge without changing bar bounds.
- Marker stays bottom-centered over `MobRoot.ResourceBarAnchorPosition` (child bar's own `-50% -100%` translate untouched).
- Offscreen display, pooling, stale release, settings disable/re-enable, picking transparency unchanged.
- No new managed collection/LINQ/closure/per-frame allocation.
- Code compiles (best-effort static check; Unity compile is a user step if needed).

## Validation Required
- Static/code review confirming no remaining `transform.position` or `style.width` runtime writes in this file.
- Cannot run Unity/PlayMode tests or trigger Unity compilation myself — report this honestly.

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks (no test changes, no doc changes here).
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
