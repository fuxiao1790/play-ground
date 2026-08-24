# Mob Health Bar UI Toolkit Performance Plan

## Summary

Keep mob health bars in standard Unity UI Toolkit `VisualElement` trees while
moving their high-frequency position and fill updates onto UI Toolkit's
GPU-backed transform path.

Refactor existing `MobResourceBarUi` path rather than adding another renderer:

- each pooled marker receives `UsageHints.DynamicTransform` before attachment;
- marker position changes through `style.translate`;
- health fill keeps fixed layout geometry and changes through left-anchored
  `style.scale`;
- existing `WorldLabelsPanel`, UXML template, USS styling, dictionary, pool,
  settings event, and target registry remain sole runtime path.

No custom mesh, custom quad renderer, new panel, ECS presentation path, or
duplicate health model enters design.

## Evidence And Performance Target

Source capture:
`ProfilerCaptures/play-ground_2026-08-23_19-07-47.csv`.

- Frames `31-319`, moving bars: `WorldLabelsPanel.PrepareRepaint` median
  `2.96 ms`; `MobResourceBarUi.LateUpdate` median `0.78 ms`; combined median
  `3.74 ms`.
- `UIR.NudgeVertices`: about `337` calls/frame and `0.93 ms` average.
- Stationary frames `5-29`: panel average `1.07 ms`, showing moving transforms
  add most avoidable panel cost.
- Layout compute/update contributes about `0.74 ms`; current
  `fill.style.width` is layout-affecting.

Post-change acceptance budget for equivalent `200` visible, moving bars after
warm-up:

- `UIR.NudgeVertices` nested under `WorldLabelsPanel.PrepareRepaint`: median
  at or below `0.10 ms` and median call count at or below `34` (10% of baseline).
- `WorldLabelsPanel.PrepareRepaint`: median at or below `1.75 ms`.
- panel plus `MobResourceBarUi.LateUpdate`: median at or below `2.75 ms`.
- stable frames with no spawn, toggle, or health mutation: `0 B` managed
  allocation attributed to `MobResourceBarUi.LateUpdate`.

These are empirical regression targets from supplied baseline, not a general
project-wide UI budget.

## Constraints And Invariants

### Ownership and dependency direction

- UI remains presentation owned by `PlayGround.Ui`; it reads game state but
  does not own health or make combat decisions. Source: `Docs/ui.md` sections
  *Ownership* and *State And Commands*.
- Dependency stays `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`.
  Simulation must not reference UI. Source:
  `Docs/architecture/layer-rules.md` section *Package Boundary*.
- `MobRoot` remains authored world-anchor and health source. UI only caches
  presentation values already present in `ResourceBarEntry`. Source:
  `Docs/ui.md` section *Input Layering*, world-projected-label paragraph, and
  `Assets/Scripts/Mob/MobRoot.cs` properties `ResourceBarAnchorPosition`,
  `CurrentHealth`, and `MaxHealth`.

### UI Toolkit structure and input

- Bars remain in distinct `WorldLabelsPanel` at sort order `-10`; `HudPanel`
  remains separate and above it. Source: `Docs/ui.md` section *Runtime Shape*.
- `#labels-layer`, marker, bar, and fill stay `PickingMode.Ignore`; world labels
  cannot intercept pointer input. Source: `Docs/ui.md` section *Input Layering*
  and `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uxml`.
- UXML owns stable repeated structure, USS owns authored visual constants, C#
  owns runtime position and health values. Source: `Docs/ui.md` section *UXML,
  USS, And C# Rules*.
- Explicit user decision: remain in UI Toolkit; do not render custom quads.

### Lifecycle and pooling

- `Awake` validates serialized references; `OnEnable` obtains panel tree and
  subscribes; `OnDisable` unsubscribes and releases runtime elements. Source:
  `Docs/ui.md` section *Lifecycle And Performance* and
  `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`.
- Existing `Dictionary<MobRoot, ResourceBarEntry>` and
  `Stack<ResourceBarEntry>` remain marker ownership and reuse mechanism.
  Disable removes markers from hierarchy but retains pooled elements. Source:
  `MobResourceBarUi.ReleaseEntry`, `ReleaseAllEntries`, and `AcquireEntry`.
- Usage hints must be assigned while marker/fill are detached, before
  `labelsLayer.Add`. Source: Unity `UsageHints` contract and current
  `CreateEntry -> AcquireEntry` lifecycle.

### Scaling and allocation

- World-label work stays `O(registered mobs + visible mob bars)` and never
  depends on projectile/AOE population. Source: `Docs/ui.md` section
  *Lifecycle And Performance*.
- No per-frame LINQ, scene search, template instantiation for reused entries,
  or new parallel collection. Source: `Docs/coding-standards.md` section
  *Allocation Rule*.
- `LateUpdate` order remains `500`, after camera `LateUpdate`, so projection
  uses final camera transform. Source: `MobResourceBarUi` execution-order
  attribute and comment.

### Settings contract

- `GameSettings` remains sole owner of display preference.
  `DisplayMobHealthBarsChanged(false)` releases markers and disabled state
  skips target iteration. Source: `Docs/contracts/game-settings-data.md`.

### Verification process

- Agent must not run Unity tests. User runs named tests and exports XML beneath
  `Logs/`; XML is sole test-result evidence. Source: `Docs/testing.md` sections
  *Running Tests* and *Result Files*.
- Timing acceptance comes from a new profiler CSV using same 200-bar scenario,
  not from NUnit timing assertions.

## Architectural Decisions

1. **Transform existing marker, not child bar.** Marker is zero-size anchor and
   already owns screen projection. Its child `translate: -50% -100%` continues
   to center bar independently.
2. **Set `DynamicTransform` on marker and fill before attachment.** Marker moves
   every visible frame; fill scales when health changes. No `GroupTransform`:
   `#labels-layer` itself does not move, and markers move independently.
3. **Use `style.translate`, not `VisualElement.transform.position`.** This is
   Unity 6 documented runtime-moving-element path paired with
   `DynamicTransform`.
4. **Use scale for health, not width.** Fill gets fixed `100%` width and
   left-center transform origin; runtime changes only X scale. This preserves
   bar background, border, clipping, and health color while avoiding Yoga
   width changes.
5. **Keep projection and registration path unchanged in this change.** Existing
   `RuntimePanelUtils.CameraTransformWorldToPanel` preserves panel scaling and
   coordinate behavior. Duplicate projection and stale dictionary scan are
   secondary controller costs and need separate evidence before refactor.
6. **Keep current element hierarchy.** No custom geometry, renderer, new
   runtime type, or scene/panel rewiring.

## Mechanisms Reused Vs. Introduced

### Reused

- `WorldLabelsPanel`, `#labels-layer`, `MobResourceBar.uxml`, and
  `MobResourceBar.uss`.
- `MobResourceBarUi` projection loop and `DefaultExecutionOrder(500)`.
- `ResourceBarEntry` presentation cache.
- marker dictionary and detached-entry stack pool.
- viewport visibility and `DisplayStyle.None` behavior.
- `GameSettings.DisplayMobHealthBarsChanged` release/skip flow.
- recursive picking transparency.

### Introduced

- No new type or data path.
- Two standard UI Toolkit usage hints on existing elements.
- Standard `style.translate` and `style.scale` writes replacing existing
  transform-position and width writes.
- Regression assertions for transform hints, position movement, fill scaling,
  and marker-specific counting.

## Minimal/Additive Vs. Refactor Comparison

### Minimal/additive approach

- **Resulting data flow:** current `WorldToPanel -> ITransform.position` and
  `health ratio -> style.width` remain; add only `DynamicTransform` hint.
- **New concepts/types introduced:** none.
- **Copies/translations added:** none.
- **Long-term cost:** keeps undocumented-for-this-purpose `ITransform.position`
  path and layout-affecting health width updates; profiler could retain layout
  and CPU vertex work, leaving two related performance mechanisms.

### Refactor approach

- **Resulting data flow:** `WorldToPanel -> marker.style.translate` and
  `health ratio -> fill.style.scale`, both through one documented UI Toolkit
  dynamic-transform mechanism.
- **Existing concepts/types changed or removed:** remove runtime
  `ITransform.position` and `style.width` writes; keep same elements and entry
  type.
- **Copies/translations removed or avoided:** avoids layout-size mutation for
  health and CPU vertex nudging for movement; adds no representation.
- **Long-term benefit:** one standard UI Toolkit transform model for dynamic bar
  presentation, clearer profiler expectations, no alternate renderer to keep
  synchronized.

### Decision

- **Choose refactor.** It changes existing update path directly, adds no second
  source of truth, and targets both measured transform and layout hotspots.
- Default rule applied: one UI Toolkit presentation path describes mob bars;
  no compatibility or migration reason exists for parallel old/new paths.

## Design Validation

| Invariant | Validation |
|---|---|
| UI does not own health | Ratio still read from `MobRoot`; entry stores only last rendered ratio. |
| Panel isolation | No `PanelSettings`, `UIDocument`, scene, or root UXML changes. |
| Picking transparency | UXML and recursive ignore setup remain; tests retain recursive assertions. |
| Lifecycle safety | Hints assigned in detached `CreateEntry`; pooling and release stay unchanged. |
| Allocation-light hot path | `Translate`, `Scale`, and `Vector2` are value types; no new collection, LINQ, closure, or instantiation in `LateUpdate`. |
| Camera-final projection | `DefaultExecutionOrder(500)` and `LateUpdate` remain. |
| Settings ownership | Existing event subscription and `ReleaseAllEntries` flow remain. |
| Scales only with mobs | Existing target-registry iteration remains; no projectile/AOE access added. |
| UI Toolkit-only requirement | All rendering remains authored `VisualElement` + UXML + USS. |

## Task Index

1. [001 — Refactor dynamic UI Toolkit transform path](001-refactor-dynamic-transform-path.md)
2. [002 — Add world-label transform regressions](002-add-world-label-transform-regressions.md)
3. [003 — Document dynamic world-label contract](003-document-dynamic-world-label-contract.md)
4. [004 — Validate 200-bar profiler budget](004-validate-profiler-budget.md)

## Dependencies And Considerations

- Unity version is `6000.4`; implementation uses Unity 6 `Translate`, `Scale`,
  and `UsageHints.DynamicTransform` APIs already available through
  `UnityEngine.UIElements`.
- Existing `RegisteredMobCreatesBarOnlyInWorldLabelsPanel` counts all
  descendants but expects `+1`, while one marker contains bar and fill
  descendants. Task 002 replaces this with marker-specific counting rather
  than preserving structurally incorrect assertion.
- Performance task requires user-provided capture. No automated timing test can
  establish render performance reliably.
- If targets are missed, next investigation remains UI Toolkit-only and should
  separately measure visible-only hierarchy attachment and projection-loop
  cost. Those speculative changes are outside this plan.

## Open Questions

None. Ownership, lifecycle, visual technology, and performance scenario are
confirmed by docs, code, profiler capture, and user decision.
