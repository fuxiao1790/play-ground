# Implementation Log

## Status
In progress — docs/tests prep for 003 done ahead of dependency order at user's
explicit request; blocked on user for task 002 (Unity Editor operation) before
either can be validated

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-split-world-label-root.md | Complete | New WorldLabelsUi.uxml/.uss; #labels-layer removed from HUD |
| 002-author-and-wire-distinct-panels.md | Blocked | Unity Editor asset/scene work — requires user, agent cannot perform |
| 003-document-and-test-isolation.md | Prepped, unvalidated | Docs done; tests written but depend on task 002 for compile-clean pass — user chose to prep ahead of the 002 dependency |
| 004-profile-scaling-isolation.md | Pending | Depends on 001-003 + user profiler captures |

## Completed Tasks
- 001-split-world-label-root.md:
  - Added `Assets/Scripts/Ui/WorldLabels/WorldLabelsUi.uxml` (single authored
    `#labels-layer`, `PickingMode.Ignore`, own `<Style src="WorldLabelsUi.uss">`).
  - Added `Assets/Scripts/Ui/WorldLabels/WorldLabelsUi.uss` (panel-filling
    absolute layout for `.labels-layer`; no dependency on `SkillLoadoutUi.uss`).
  - Removed `#labels-layer` from `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`.
  - Updated root-stack comment in `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uss`
    to list HUD-only siblings and point to `WorldLabelsUi.uxml`/`.uss` for
    world labels.
  - Updated `MobResourceBarUi.cs` `OnEnable` error text to reference
    `WorldLabelsUi.uxml` instead of `SkillLoadoutUi.uxml`.
  - Updated stale HUD-tree comment in `MobResourceBar.uxml` to reference
    `WorldLabelsUi.uxml`.
  - No changes to `MobResourceBarUi` reconciliation, pooling, projection,
    fill-caching, execution order, or serialized fields.
  - No fallback query against HUD root and no dual-document path added.

- 003-document-and-test-isolation.md (prep only; not dependency-verified against
  a complete task 002 — user explicitly asked to prep this ahead of that
  ordering):
  - `Docs/ui.md`: introduced the two-panel (`HudPanel`/`WorldLabelsPanel`)
    model in Runtime Shape (sort orders, distinct-`PanelSettings` requirement);
    replaced the six-sibling single-tree Input Layering diagram with a
    per-panel diagram and cross-panel picking/pause explanation; added the
    explicit `O(...)` complexity contract and profiler-marker-ownership note
    to Lifecycle And Performance; updated Scene And Inspector Setup and
    Adding A New UI Feature for the two-panel choice.
  - `Docs/folder-structure.md`: `PlayGround.Ui.asmdef` entry now lists HUD and
    world-label UI roots/panels separately.
  - Added `Assets/Tests/EditMode/UiPanelIsolationEditModeTests.cs` (5 methods
    from the task spec). `HudRootDoesNotContainLabelsLayer`,
    `WorldLabelsRootContainsExactlyOneLabelsLayer`, and
    `WorldLabelElementsArePickingTransparent` inspect UXML assets only and
    should already be exercisable today (001 is done).
    `WorldLabelsPanelIsDistinctAndSortsBelowHudPanel` (needs
    `Assets/UI/HudPanel.asset`/`WorldLabelsPanel.asset`) and
    `HudAndWorldLabelDocumentsUseDifferentPanelSettings` (opens
    `BenchmarkLarge.unity` additively via `EditorSceneManager`, looks for root
    objects `GameUI`/`WorldLabelsUI`) will fail until task 002 is done — that
    is expected/intended (red until 002, green after).
  - Added `Assets/Tests/PlayMode/UiPanelIsolationPlayModeTests.cs` (5 methods
    from the task spec). All five load `BenchmarkLarge.unity` additively via
    `SceneManager.LoadSceneAsync` and look up `GameUI`/`WorldLabelsUI` root
    objects, so **all five currently fail** until task 002 wires
    `WorldLabelsUI` into the scene. `RegisteredMobCreatesBarOnlyInWorldLabelsPanel`
    builds a minimal `MobRoot` fixture (mirroring
    `MobSpawnControllerPlayModeTests.CreateMobPrefab`) and registers it
    directly into the `CombatRoot` read via reflection off the scene's
    `MobResourceBarUi.combatRoot` field, then asserts exactly one marker is
    added under `WorldLabelsPanel`'s `#labels-layer` and HudPanel's element
    count is unchanged.
  - Added `PlayGround.Ui` to `Assets/Tests/PlayMode/PlayGround.Tests.PlayMode.asmdef`
    (needed for `MobResourceBarUi`). Did **not** add it to the EditMode asmdef
    — the EditMode tests only touch `UnityEngine.UIElements`/`UnityEditor`
    types, so the reference would be unused.
  - **Known scope-down / risk, flagged for review once Unity is available:**
    `ProjectileAndAoePopulationDoesNotAddHudVisualElements` does not manually
    spawn projectiles/AOEs (that needs `CombatRoot.Spawn`/AOE type-registry
    setup whose exact fixture shape I could not verify without running
    Unity). It instead asserts HudPanel's element count is flat across 30
    frames of whatever ambient combat activity the scene produces on its own.
    This is weaker than the task's literal intent (controlled low/high
    population) and should be revisited once the user can run it and observe
    whether `BenchmarkLarge` actually generates ambient combat activity when
    loaded without player input.
  - None of the "Required User Test Runs" exports have been produced — no
    Unity Test Runner invocation has happened. Per project rule, only the
    user runs Unity Test Runner; this agent has not claimed any test result.

## Blockers
- 002-author-and-wire-distinct-panels.md requires Unity Editor asset rename/
  duplication (PanelSettings) and scene wiring (new `WorldLabelsUI`
  GameObject + `UIDocument`, moving `MobResourceBarUi` component, assigning
  the new `WorldLabelsUi.uxml`). Per project convention, editor/scene/asset
  authoring is done by the user through the Unity Editor, not by hand-editing
  YAML/meta files. Handoff instructions given to user; orchestration paused
  until task 002 is confirmed done.

## Validation Summary
- 001: Verified by search — exactly one authored `name="labels-layer"` in the
  repo (`WorldLabelsUi.uxml`); no remaining `labels-layer` element in
  `SkillLoadoutUi.uxml`. New `.uxml`/`.uss` files have no `.meta` yet; Unity
  will generate them on next editor import (expected — these are new asset
  additions, not hand-edited existing metadata).
- Scene (`BenchmarkLarge`/`GameUI`) still references old single-document setup
  until task 002 rewires it; scene will not enter Play Mode correctly with a
  fully-consistent two-panel setup until then. This is expected per the plan's
  own dependency note ("scene becomes fully runnable after task 002 wiring").
- 003: Code written but not compiled or run by the agent (no Unity access).
  3 of 5 EditMode tests should already be exercisable against current repo
  state; the other 2 EditMode tests and all 5 PlayMode tests require task 002
  first. User must run Unity Test Runner and export XML before any pass/fail
  claim is trustworthy, per project rule.
- 002/004: Not started/not run.
