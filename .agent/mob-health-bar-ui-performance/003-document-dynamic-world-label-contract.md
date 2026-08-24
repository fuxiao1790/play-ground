# 003 — Document Dynamic World-Label Contract

## Goal

Make optimized standard UI Toolkit path an explicit project contract so future
world-label work does not reintroduce CPU vertex nudging, width-driven layout,
or a parallel renderer.

## Changes

### `Docs/ui.md`

Extend world-label performance guidance with these rules:

- mob bars remain standard UI Toolkit elements in `WorldLabelsPanel`;
- moving marker uses `style.translate` and
  `UsageHints.DynamicTransform`, assigned before panel attachment;
- health fill keeps fixed layout geometry and uses left-anchored
  `style.scale` for dynamic ratio;
- `GroupTransform` is not applied to stationary `#labels-layer` merely because
  it contains independently moving markers;
- marker/fill pooling and `PickingMode.Ignore` remain required;
- custom quad/mesh rendering is not current world-label path;
- profiler validation must read controller cost together with
  `WorldLabelsPanel.PrepareRepaint`.

Update any implementation note that still describes
`VisualElement.transform.position` or dynamic fill width.

### `Docs/profiling.md`

Add focused query guidance for world labels:

- `WorldLabelsPanel.PrepareRepaint`;
- `MobResourceBarUi.LateUpdate`;
- `UIR.NudgeVertices` under world-label panel;
- `RenderTree.UpdateTransforms`;
- `LayoutUpdater.ComputeLayout` and `LayoutUpdater.UpdateSubTree`;
- compare stationary, moving, and health-changing phases separately;
- sum controller and panel markers for total CPU cost.

Do not add claims that GPU time is measured by CPU profiler CSV.

## Acceptance Criteria

- Docs match implemented UXML/USS/C# path after task 001.
- UI ownership, panel isolation, input transparency, settings ownership, and
  scaling contract remain unchanged.
- Docs clearly prohibit custom rendering for current mob bars without making a
  universal claim that UI Toolkit custom geometry can never be used elsewhere.
- Profiling section distinguishes CPU panel preparation from GPU render time.

## Dependencies

- Depends on task 001 so documentation describes final implementation.

## Estimated Scope / Complexity

Small. Two focused documentation updates.
