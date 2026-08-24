# 001 — Refactor Dynamic UI Toolkit Transform Path

## Goal

Move mob-bar position and health updates onto documented UI Toolkit dynamic
transforms without changing element structure, ownership, pooling, or panel
wiring.

## Changes

### `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`

1. In `CreateEntry`, while marker tree is detached:
   - assign `UsageHints.DynamicTransform` to `marker`;
   - assign `UsageHints.DynamicTransform` to `fill`;
   - do this before any future `labelsLayer.Add` call;
   - do not assign `GroupTransform` to marker or labels layer.
2. In `Project`:
   - preserve current world anchor, viewport culling, visibility, and
     `RuntimePanelUtils.CameraTransformWorldToPanel` conversion;
   - replace `entry.Marker.transform.position` with
     `entry.Marker.style.translate = new Translate(panelX, panelY)`;
   - preserve ratio clamp and `Mathf.Approximately` cache guard;
   - replace fill percent-width write with
     `entry.Fill.style.scale = new Scale(new Vector2(ratio, 1f))`.
3. In `AcquireEntry`:
   - reset fill scale to `Vector2.one` instead of resetting inline width;
   - retain visibility reset and hierarchy attachment order.
4. Keep `ResourceBarEntry`, dictionary, stale scan, stack pool, settings event,
   and execution order unchanged.

### `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss`

1. Give `.mob-resource-bar__fill` fixed full width (`width: 100%`).
2. Set left-center transform origin (`transform-origin: 0% 50%`) so X scale
   drains/fills from left to right.
3. Keep bar dimensions, background, border, clipping, and health color.
4. Update stale comment that names `VisualElement.transform.position`; describe
   marker `style.translate` plus `DynamicTransform` behavior.

### Explicit exclusions

- No UXML hierarchy changes.
- No custom mesh or custom quad rendering.
- No panel/scene asset edits.
- No registry, ECS, health, or settings changes.
- No projection-conversion rewrite.
- No visible-only attachment or update throttling.

## Acceptance Criteria

- Marker and fill receive `UsageHints.DynamicTransform` before marker joins a
  panel.
- Marker runtime position uses `style.translate`; no
  `Marker.transform.position` write remains.
- Fill runtime value uses `style.scale`; no runtime `Fill.style.width` write
  remains.
- At ratios `0`, `0.5`, and `1`, fill is visually empty, half, and full from
  left edge without changing bar bounds.
- Marker remains bottom-centered over `MobRoot.ResourceBarAnchorPosition`.
- Offscreen display, marker pooling, stale release, settings disable/re-enable,
  and picking transparency remain behaviorally unchanged.
- No managed collection, LINQ, closure, or per-frame object creation added.
- Project compiles after Unity imports updated C# and USS.

## Dependencies

None.

## Estimated Scope / Complexity

Small. One runtime controller and one USS file; localized behavior refactor.
