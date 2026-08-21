# Mob Health And UI Layer Stack Plan

## Summary

Refactor the single runtime `UIDocument` into an explicit, authored back-to-front
layer stack, then add pooled mob-health projections to the shared labels layer.
The resulting front-to-back order is:

1. pause menu
2. pause background
3. popups
4. HUD
5. labels, including mob health and future ground-item labels
6. click-to-fire surface

The UXML child order is the reverse (back-to-front), because later siblings draw
over earlier siblings. Controllers add feature content only to their named layer;
they do not call `root.Add(...)` or reorder root children at runtime.

Mob health remains authoritative in `MobRoot`. `MobHealthBarUi` reads registered
`MobRoot` instances from `CombatRoot.TargetRegistry.Targets`, projects their world
positions into the existing UI Toolkit panel, and owns only pooled visual state.

## Architectural Decisions

### One panel and one authored root stack

Keep the existing screen-space `UIDocument` and `PanelSettings`. Add six stable
root-level layer elements to `SkillLoadoutUi.uxml`. Do not introduce world-space
Canvases, per-mob `UIDocument`s, or `SpriteRenderer` health bars. This preserves
one deterministic visual and input ordering mechanism.

### Stable UXML owns stable hierarchy

Author the click-to-fire surface and all layer containers in UXML. Refactor
`GameplayInputSurface` to bind callbacks to `#click-to-fire-layer` instead of
creating and inserting a `VisualElement` at runtime. This follows the UI rule that
UXML owns stable structure and removes execution-order-dependent root insertion.

### Named layers own content

- `GameplayInputSurface` binds `#click-to-fire-layer`.
- World-projected health bars attach to `#labels-layer`.
- Existing HUD stays in `#hud-container`.
- `SkillLoadoutUi` attaches the picker to `#popup-layer`.
- `PauseMenuUi` attaches menu content to `#pause-menu-layer` and controls the
  separate `#pause-background` input shield.

No controller may append feature content directly to `rootVisualElement` after
this refactor.

### Pause background is both visual dimmer and input shield

While paused, `#pause-background` is visible and uses `PickingMode.Position`.
This prevents pointer input from reaching popups, HUD, labels, or click-to-fire.
`#pause-menu-layer` remains above it; the layer ignores picking while actual menu
controls opt into position picking. When unpaused, pause background is `display:
none` and `PickingMode.Ignore`.

### Reuse combat target membership

`CombatTargetRegistry<ICombatTarget>.Targets` is already the active managed target
membership used by both scene-authored and spawned mobs. `MobHealthBarUi` filters
that list for `MobRoot`; it does not add spawn/despawn events, static mob lists, a
second registry, or UI references to `MobRoot`/`SpawnController`.

### Pool visual elements

Maintain one visual entry per registered mob and reuse entries after unregister.
Reconciliation uses reusable dictionaries/lists/stacks and no per-frame LINQ,
closures, or temporary collections. Offscreen entries stay pooled and hidden.
Ground-item production is not present in the repository, so this change provides
the shared `#labels-layer` contract without inventing a speculative item-label
source model.

## Constraints And Invariants

### UI is presentation only

- UI reads gameplay state; it does not own health or ECS entities.
- Dependency direction remains `PlayGround.Ui -> PlayGround.GameLogic ->
  PlayGround.Sim`.
- Source: `Docs/ui.md` sections **Ownership** and **State And Commands**;
  `Docs/architecture/layer-rules.md` sections **Ownership** and **Package
  Boundary**.
- Result: `MobRoot.CurrentHealth` and `MobRoot.MaxHealth` remain the only values
  rendered. No health copy becomes authoritative in UI.

### Stable hierarchy and styles belong in UXML/USS

- Stable structure belongs in UXML; layout, colors, size, and visibility classes
  belong in USS; C# owns runtime values and dynamic cloning.
- Source: `Docs/ui.md` section **UXML, USS, And C# Rules**.
- Result: root layers and bar template are authored assets. C# changes projected
  position, fill ratio, visibility, pooling, and callbacks only.

### Input layering is deliberate

- Click-to-fire is the bottom input surface. Decorative UI passes through;
  interactive controls consume input. Pause blocks lower layers.
- Source: `Docs/ui.md` section **Input Layering**;
  `Assets/Scripts/Ui/Hud/GameplayInputSurface.cs`.
- Result: all label descendants explicitly use `PickingMode.Ignore`; layer-root
  ignore is not treated as inherited. HUD/popups opt controls into picking.

### Lifecycle boundaries

- `Awake` validates self-owned references. `OnEnable` queries the document and
  subscribes/binds. `OnDisable` unregisters callbacks, clears held input, and
  releases owned runtime elements.
- Source: `Docs/ui.md` section **Lifecycle And Performance** and
  `Docs/coding-standards.md` section **Awake vs OnEnable Boundary**.
- Result: controller callbacks cannot duplicate across enable cycles, and pooled
  visuals are detached/cleared by their owner on disable.

### Managed main-thread membership

- `CombatTargetRegistry<T>` owns a managed `List<T>` exposed as
  `IReadOnlyList<T>`. Registration/unregistration occurs through managed roots on
  the main thread.
- Source: `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`,
  `Assets/Scripts/Mob/MobRoot.cs`, and `Assets/Scripts/Spawn/SpawnController.cs`.
- Result: UI may reconcile the read-only target list in `LateUpdate`; it must not
  mutate registry membership or access ECS entity data directly.

### Frame ordering

- `MobRoot.Update` mirrors current ECS resources into `Resource`.
- `GameplayCamera.LateUpdate` moves/zooms the camera.
- Source: `Assets/Scripts/Mob/MobRoot.cs` and
  `Assets/Scripts/Camera/GameplayCamera.cs`.
- Result: `MobHealthBarUi` runs in `LateUpdate` with an execution order after
  `GameplayCamera`, so it reads current health and final camera transform for the
  frame. It uses `RuntimePanelUtils.CameraTransformWorldToPanel` against the
  existing panel.

### Pooling and allocation budget

- Mob spawn and AI are hot paths. Repeated Instantiate/Destroy, per-frame LINQ,
  closures, and collection allocations are bugs unless proven harmless.
- Source: `Docs/coding-standards.md` sections **Allocation Rule** and
  **Performance Budget Rule**; `Assets/Scripts/Spawn/MobPool.cs`.
- Result: health bar views are pooled, target reconciliation uses reusable
  storage, UXML queries are cached, and only active registered mobs are visited.
  Offscreen bars are hidden before fill/position presentation work where possible.

### Scene wiring

- Required references use Inspector injection and fail-fast validation. Scene
  YAML/meta files are not hand-edited to simulate assignments.
- Source: `Docs/coding-standards.md` sections **Root Component Rule** and **Fail
  Fast Validation**; `Docs/ui.md` section **Scene And Inspector Setup**.
- Result: `MobHealthBarUi` receives camera, combat root, template, and authored
  offset through serialized fields. `PauseMenuUi` is wired through the Unity
  Inspector in the target scene.

### Test execution

- Agent writes/names tests but never invokes Unity Test Runner. User exports XML
  under `Logs/`; only that XML can support a pass claim.
- Source: `Docs/project-overview.md` **Agent instructions** and
  `Docs/testing.md` **Running Tests / Result Files**.

## Mechanisms Reused Vs. Introduced

### Reused

- Existing `UIDocument`, `PanelSettings`, root UXML/USS, and UI Toolkit input
  event path.
- Existing feature-template pattern used by skill picker and pause menu.
- Existing `CombatRoot.TargetRegistry.Targets` as mob lifecycle membership.
- Existing `MobRoot.CurrentHealth` / `MaxHealth` presentation view.
- Existing `PickingMode` pass-through model.
- Existing mob object pooling lifecycle; UI visuals independently pool presentation
  entries without changing mob pool behavior.

### Introduced

- Five new named root containers plus authored click-to-fire element, defining
  six total layers.
- `MobHealthBar.uxml` and corresponding USS classes for repeated health views.
- `MobHealthBarUi`, one UI-owned projection/pooling controller.
- Pause background as a stable authored element and input shield.

`MobHealthBarUi` is justified because no existing controller owns world-projected
labels, camera projection, or per-target visual pooling. It creates presentation
state only; it does not duplicate gameplay ownership.

## Design Validation

| Invariant | Validation |
|---|---|
| Exact visual order | Stable root sibling order is click, labels, HUD, popups, pause background, pause menu. No runtime root append can bypass it. |
| Exact input order | Pause background consumes input while visible; labels ignore input; HUD/popups consume only through interactive descendants; click surface receives remaining pointer input. |
| One health source | Fill reads `MobRoot.CurrentHealth / MobRoot.MaxHealth`; UI stores only last-rendered ratio and view handles. |
| Pooled mob reuse | Registry unregister removes/recycles view; later re-registration reacquires and fully refreshes same or another pooled view. |
| Scene and spawned mobs | Both register in `CombatRoot.TargetRegistry`, so one reconciliation path covers both. |
| Camera timing | Projection executes after gameplay camera `LateUpdate`, avoiding one-frame positional lag. |
| Offscreen/dead behavior | Behind-camera, out-of-viewport, inactive, and unregistered targets hide or release views without gameplay mutation. |
| Allocation budget | Cached layer/template references and reusable collections eliminate recurring managed allocation; no Canvas/UIDocument per mob. |
| Future item labels | Shared labels container establishes ordering without inventing a second panel or item-domain contract before item ownership exists. |

## Minimal/Additive Vs. Refactor Comparison

### Minimal/additive approach

- **Resulting data flow:** each mob owns a world-space sprite/canvas bar; existing
  popups and pause menu continue appending directly to root; input surface remains
  dynamically inserted.
- **New concepts/types introduced:** per-mob presentation component and possibly
  per-mob Canvas/UIDocument.
- **Copies/translations added:** separate render-order rules for world sprites and
  UI Toolkit; separate input/visibility behavior outside the panel hierarchy.
- **Long-term cost:** visual order cannot express the requested six-layer contract
  in one place; future ground labels repeat projection/layer decisions; runtime
  insertion order remains lifecycle-dependent.

### Refactor approach

- **Resulting data flow:** authoritative mob state -> existing target registry ->
  one UI projection controller -> shared labels layer. All UI features attach to
  authored named layers in one panel.
- **Existing concepts/types changed or removed:** dynamic click-surface creation and
  arbitrary `root.Add(...)` calls are removed; existing popup/pause controllers
  route into explicit containers.
- **Copies/translations removed or avoided:** no second mob registry, spawn UI
  events, health model, world-space UI stack, or cross-renderer ordering rules.
- **Long-term benefit:** one inspectable source of truth for visual/input order;
  future labels share the same layer; pooling and projection have one owner.

### Decision

- **Choose refactor.** Stable layers collapse existing ad hoc insertion into one
  authored stack and allow mob health to participate without a parallel renderer
  or lifecycle path.
- Default decision rule applied: where two representations or data paths would
  describe the same active mobs or UI stacking, retain one source of truth unless
  a concrete compatibility need appears. None exists here.

## Task List

1. [001-explicit-layer-root.md](001-explicit-layer-root.md) — author and document
   stable root layer contract.
2. [002-route-existing-ui.md](002-route-existing-ui.md) — bind click, popup, and
   pause features to named layers.
3. [003-mob-health-projection.md](003-mob-health-projection.md) — add pooled mob
   health bars in labels layer.
4. [004-scene-wiring.md](004-scene-wiring.md) — wire required controllers/assets
   through Inspector in target scene.
5. [005-tests.md](005-tests.md) — add contract and runtime regression coverage.

## Open Questions, Dependencies, And Considerations

- No ground-item/loot runtime exists in current code. This plan reserves and
  documents its target layer but does not invent item ownership or implement item
  labels.
- Exact bar colors, dimensions, and world offset are authored presentation values;
  initial defaults should be reviewed visually during user playtest.
- Current `Docs/flows/mob-spawn-and-behaviour.md` describes spawning as future work,
  while `SpawnController`/`MobPool` now exist. That stale documentation is outside
  this UI change unless implementation reveals a conflicting lifecycle contract.
- No `info.md` exists for this task; repository docs and current code are the
  exploration ground truth.

