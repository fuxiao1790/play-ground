# Implementation Context

## Architectural Decisions
- One panel, one `UIDocument`/`PanelSettings`, six authored root-level layers in `SkillLoadoutUi.uxml` (back-to-front): `#click-to-fire-layer`, `#labels-layer`, `#hud-container`, `#popup-layer`, `#pause-background`, `#pause-menu-layer`.
- Stable structure in UXML; controllers query named layers, never `root.Add`/`Insert`/`BringToFront`.
- `MobResourceBarUi` projects `MobRoot` world state into `#labels-layer`; health stays authoritative on `MobRoot`, and each mob prefab owns its local-space resource-bar anchor offset.

## Global Invariants
- UI reads gameplay state only; never owns health/ECS entities. Dependency direction: `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`.
- Stable hierarchy/styles in UXML/USS; C# owns runtime values, cloning, pooling.
- All label descendants set `PickingMode.Ignore` explicitly (not inherited).
- `Awake` = self-owned setup/validation only. `OnEnable` = query document, subscribe. `OnDisable` = unsubscribe, clear input, release owned elements.
- `CombatTargetRegistry<T>` managed list, main-thread only; UI may read `IReadOnlyList<T>` in `LateUpdate`, never mutate membership or touch ECS directly.
- `MobResourceBarUi` execution order must run after `GameplayCamera.LateUpdate` (`GameplayCamera` has no `[DefaultExecutionOrder]`, i.e. default 0).
- Pooled visuals: reusable dictionaries/lists/stacks, no per-frame LINQ/closures/temp collections.
- Scene wiring via Inspector only; never hand-edit scene YAML/.meta.
- Agent never runs Unity Test Runner; user exports XML under `Logs/`, agent reviews XML before claiming pass.

## Ownership Boundaries
- `MobRoot.CurrentHealth`/`MaxHealth` are the only rendered health values; no UI-side health copy becomes authoritative.
- `MobResourceBarUi` reads `CombatRoot.TargetRegistry.Targets` (`CombatTargetRegistry<ICombatTarget>`), filters `target is MobRoot`.
- No new registry, no spawn/despawn events, no static mob lists, and no UI type references added to `MobRoot`/`SpawnController`. `MobRoot` exposes only an authored world anchor position.

## Data Flow
- `MobRoot` authoritative health -> `CombatTargetRegistry<ICombatTarget>.Targets` (existing membership, used by both scene and spawned mobs) -> `MobResourceBarUi` reconciliation (`LateUpdate`) -> pooled bar view in `#labels-layer`.

## Lifecycle / Allocation Rules
- `MobResourceBarUi`: `Awake` validates serialized refs (`CombatRoot`, gameplay `Camera`, `VisualTreeAsset` template, stylesheet). `OnEnable` queries `#labels-layer`. `LateUpdate` (ordered after `GameplayCamera`) reconciles. Projection reads `MobRoot.ResourceBarAnchorPosition`; no global UI offset exists.
- One dictionary `MobRoot -> presentation entry`, one stack of reusable entries, reuse-on-acquire with full visual reset, release on unregister/destroy/disable.
- No Instantiate/Destroy churn, no per-frame LINQ/closures/allocation in steady state.

## ECS / Job / Threading Constraints
- Not applicable directly to this UI work beyond: never read ECS singletons/entities from UI; only managed `ICombatTarget`/`MobRoot` state.

## Determinism Requirements
- None beyond frame-ordering (camera before health-bar projection) to avoid one-frame lag.

## Producer / Consumer Separation
- `MobRoot` produces resource/position facts; `MobResourceBarUi` consumes/presents only. Current binding selects health. No response policy logic added to `MobRoot`.

## Reused Mechanisms
- Existing `UIDocument`, `PanelSettings`, root UXML/USS, UI Toolkit input path.
- Existing feature-template clone pattern (skill picker/pause menu).
- Existing `CombatRoot.TargetRegistry.Targets` membership.
- Existing `MobRoot.CurrentHealth`/`MaxHealth`.
- Existing `PickingMode` pass-through model.
- Existing mob pooling lifecycle (independent UI-side pooling, no change to `MobPool`).

## Introduced Mechanisms
- Five new named root containers + authored click-to-fire element (six layers total) in `SkillLoadoutUi.uxml`/`.uss`.
- `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uxml` + USS for repeated resource view.
- `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs` (`PlayGround.Ui`), pooled projection controller.
- Authored `#pause-background` as visual dimmer + input shield.

## Validation Requirements
- No Unity Test Runner invocation by agent. Task 005 names exact EditMode/PlayMode test classes/methods for the user to run and export XML under `Logs/` (`Logs/TestResults-EditMode-UiLayers.xml`, `Logs/TestResults-PlayMode-MobHealthUi.xml`).
- Static/search-based verification (no `root.Add`/`Insert`/`BringToFront` remaining, UXML order correct) is agent-doable.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`, `SkillLoadoutUi.uss` — root layer authoring.
- `Assets/Scripts/Ui/Hud/GameplayInputSurface.cs` — bind `#click-to-fire-layer`, remove dynamic insertion.
- `Assets/Scripts/Ui/Hud/SkillLoadout/SkillLoadoutUi.cs` — bind `#popup-layer`.
- `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.cs` — bind `#pause-background`/`#pause-menu-layer`.
- `Assets/Scripts/Ui/WorldLabels/MobResourceBar.uxml`, `MobResourceBar.uss` (or shared), `MobResourceBarUi.cs` — new.
- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`, `Assets/Scripts/Mob/MobRoot.cs`, `Assets/Scripts/Spawn/SpawnController.cs` — read-only reuse.
- `Assets/Scripts/Camera/GameplayCamera.cs` — execution-order dependency only, no edits.
- `Assets/Scenes/BenchmarkLarge.unity` — Inspector wiring (task 004, user/editor action).
- `Docs/ui.md` — layer/order/ownership documentation update (task 001).
- Assembly notes: `PlayGround.Ui.asmdef` already references `PlayGround.GameLogic` (covers `Mob`, `Camera`, `Skills`, `Spawn` — no dedicated asmdefs) and `PlayGround.Sim` (covers `CombatRoot`). No asmdef changes required for `MobResourceBarUi`.
