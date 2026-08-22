# Implementation Log

## Status
Implementation wired by user; task 005 validation remains pending

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-explicit-layer-root.md | Complete | Six-layer root stack authored in UXML/USS; Docs/ui.md rewritten |
| 002-route-existing-ui.md | Complete | GameplayInputSurface/SkillLoadoutUi/PauseMenuUi rebound to named layers |
| 003-mob-health-projection.md | Complete | New pooled MobResourceBarUi + template added to #labels-layer |
| 004-scene-wiring.md | Complete | User wired scene through Unity Inspector and supplied runtime screenshot |
| 005-tests.md | Pending | No user-exported XML results reviewed |

## Completed Tasks
- 001-explicit-layer-root.md: Added `#click-to-fire-layer`, `#labels-layer`,
  `#hud-container`, `#popup-layer`, `#pause-background`, `#pause-menu-layer` as
  six back-to-front siblings in `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`
  (existing HUD children untouched under `#hud-container`). Added `.ui-layer`
  full-screen base class and `.pause-background`/`--hidden`/`--visible`
  classes to `SkillLoadoutUi.uss`. Rewrote `Docs/ui.md` **Input Layering**
  section to the six-layer contract, replacing the two-layer model. Left
  `.world-input-surface` USS class and `GameplayInputSurface`'s dynamic
  surface creation untouched (task 002 scope); both still work unchanged, so
  the app remains playable as-is between tasks 001 and 002 with the new
  layers inert/unused until 002 wires them up.

- 002-route-existing-ui.md:
  - `GameplayInputSurface.cs`: `OnEnable` now queries `#click-to-fire-layer`
    instead of creating/inserting a `VisualElement`; removed
    `CreateSurfaceIfNeeded`/`InsertSurfaceAtRootStart`; `OnDisable` unregisters
    the three pointer callbacks and clears `surface` (was
    `surface?.RemoveFromHierarchy()`, now the layer node itself stays in the
    document — only the callback bindings are owned/released). Throws if the
    layer is missing.
  - `SkillLoadoutUi.cs`: added `popupLayer` field, queried and validated
    alongside the existing HUD node check in `ConfigureHudInputLayering`;
    `OpenPicker` now adds the modal to `popupLayer` instead of `root`.
  - `PauseMenuUi.cs`: added `pauseMenuLayer`/`pauseBackground` fields, queried
    and validated in `OnEnable`; pause-menu template now parents to
    `pauseMenuLayer` instead of `root`; `OnPausedChanged` now also toggles
    `pause-background--visible`/`--hidden` and `PickingMode` on
    `pauseBackground`. `OnDisable` detaches only the owned template instance;
    the authored `#pause-background`/`#pause-menu-layer` nodes stay in the
    document.
  - Removed the now-dead `.world-input-surface` USS class from
    `SkillLoadoutUi.uss` (last reference was the removed dynamic-creation code
    in `GameplayInputSurface.cs`; `.ui-layer` + `picking-mode="Position"` on
    the authored `#click-to-fire-layer` replaces it).

- 003-mob-health-projection.md: Added
  `Assets/Scripts/Ui/WorldLabels/MobHealthBar.uxml`,
  `Assets/Scripts/Ui/WorldLabels/MobHealthBar.uss`, and
  `Assets/Scripts/Ui/WorldLabels/MobHealthBarUi.cs` (namespace `PlayGround.Ui`
  per task text). Controller validates `document`/`combatRoot`/
  `gameplayCamera`/`healthBarTemplate` in `Awake`, queries `#labels-layer` in
  `OnEnable`, reconciles `combatRoot.TargetRegistry.Targets` in `LateUpdate`
  ordered after `GameplayCamera` (`[DefaultExecutionOrder(500)]` vs. the
  camera's default 0). Pooling: `Dictionary<MobRoot, HealthBarEntry>` +
  `Stack<HealthBarEntry>` + reused `List<MobRoot> staleKeys`, staleness tracked
  via a per-frame `frameToken` generation stamp rather than a per-frame lookup
  set. Projection rejects behind-camera/out-of-viewport anchors via
  `Camera.WorldToViewportPoint` before calling
  `RuntimePanelUtils.CameraTransformWorldToPanel`; the zero-size `#marker` is
  moved via `VisualElement.transform.position` (no layout pass); `#bar` is
  authored with `translate: -50% -100%` in `MobHealthBar.uss` to center above
  the marker. Fill width and marker `display` are only written when the
  cached value actually changes. No changes to `MobRoot`, `SpawnController`,
  or `CombatTargetRegistry`.

## Post-004 Fix (found via user playtest)
- User wired task 004 and reported bars not appearing. Root cause:
  `MobHealthBar.uxml`'s `<Style src="MobHealthBar.uss">` only applies within
  its own `TemplateContainer`; `MobHealthBarUi.CreateEntry` pulls just
  `#marker` out and reparents it under `#labels-layer` (a branch of
  `SkillLoadoutUi.uxml`'s tree), so the stylesheet reference never travelled
  and the bar rendered with no size/color. Same situation `PauseMenuUi.cs`
  already solves by adding its stylesheet in C#. Fix: removed `<Style src>`
  from `MobHealthBar.uxml`; added a serialized `healthBarStyleSheet` field to
  `MobHealthBarUi` (validated in `Awake`, matching `PauseMenuUi`'s
  `pauseMenuStyleSheet` convention) and `marker.styleSheets.Add(...)` in
  `CreateEntry`. Also made `.mob-health-bar`'s `position: absolute` anchor
  explicit (`top: 0; left: 0;`) defensively, since it was previously relying
  on Yoga's default static-position resolution.
- **New required Inspector step for the user**: on `GameUI`'s
  `MobHealthBarUi` component, assign the new `Health Bar Style Sheet` field to
  `Assets/Scripts/Ui/WorldLabels/MobHealthBar.uss`.

## Per-Mob Anchor Fix (found via user playtest)
- Removed the global `worldOffset` field from `MobHealthBarUi`; one `GameUI`
  offset could not represent differently sized mob prefabs.
- Added local-space `healthBarOffset` authoring to `MobRoot` and exposed
  `HealthBarAnchorPosition` as read-only world-space presentation data.
- `MobHealthBarUi` now projects `mob.HealthBarAnchorPosition`. No UI type or UI
  lifecycle responsibility moved into mob code.
- **Required Inspector step for the user**: open each mob prefab and configure
  its `Health Bar Offset` on `MobRoot`.

## Generic Resource-Bar Rename
- Renamed runtime controller and assets to `MobResourceBarUi`,
  `MobResourceBar.uxml`, and `MobResourceBar.uss`, preserving their `.meta`
  GUIDs so scene assignments survive.
- Renamed serialized template/stylesheet/offset fields with
  `FormerlySerializedAs` migration attributes.
- Renamed mob anchor API to `ResourceBarAnchorPosition` and generic USS classes
  to `mob-resource-*`. Current health color now lives behind
  `mob-resource-bar--health`, leaving generic bar structure reusable for future
  resources.
- **Required Inspector step for the user**: configure `Resource Bar Offset` on
  each mob prefab after Unity recompiles.

## Historical Blocker (Resolved By User Wiring)
- Scene wiring shown below is complete. Kept as implementation history.
- 004-scene-wiring.md requires Unity Editor Inspector actions in
  `Assets/Scenes/BenchmarkLarge.unity` (add `PauseMenuUi` and `MobHealthBarUi`
  components to `GameUI` and assign serialized references). This is
  explicitly a user/editor step per project rules, not an agent file edit —
  confirmed the scene currently has `GameUI` (UIDocument + SkillLoadoutUi +
  GameplayInputSurface, no PauseMenuUi/MobHealthBarUi yet), an existing
  `CombatRoot` instance, an existing `GameplayCamera` instance, and an
  existing `PauseController` instance already used by `GameplayInputSurface`.
  Reported to user as a numbered Inspector checklist; 005 not started pending
  this.

## Validation Summary
- 001: Static/search verification only (no code changed, no Unity build
  available to the agent). Confirmed via re-read of
  `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml` that all six layers exist
  exactly once in the specified order and `#hud-container`'s existing children
  are unchanged. No scene/gameplay/ECS type references UI layering (only
  UXML/USS/Docs touched).
- 002: Static/search verification. Grepped for `root.Add(`, `root.Insert(`,
  and `BringToFront(` across the three edited controllers — none remain for
  layer ordering. Re-read all three files after edit to confirm null-checked
  layer queries throw `InvalidOperationException` with a clear message. No
  Unity compile/build run (not available to the agent in this environment).
- 003: Static verification only. Re-read the new controller/UXML/USS for
  correctness against the task's data/lifecycle/rendering contract (see
  above). No Unity compile, PlayMode, or profiler check performed — none are
  available to the agent; this needs the user's Unity Editor / Test Runner
  per `Docs/project-overview.md`.
