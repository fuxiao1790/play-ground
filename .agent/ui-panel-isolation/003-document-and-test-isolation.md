# 003 - Document And Test Panel Isolation

## Change

Replace one-panel UI contract in authoritative docs and add structural/runtime
regressions preventing world labels from returning to HUD.

## Documentation

Update `Docs/ui.md`:

- Describe two runtime panels and their independent ownership.
- Replace six-sibling single-tree diagram with HUD tree plus lower-sorted world
  labels tree.
- Document distinct PanelSettings requirement; sharing PanelSettings means
  sharing one runtime panel.
- Document panel sort values and cross-panel picking behavior.
- State explicit complexity contract:
  - HUD: `O(skill slots + fixed controls)`.
  - World labels: `O(mobs)`.
  - Neither: `O(projectiles + AOEs)`.
- Update lifecycle, scene setup, pause, and future-feature guidance.
- Record profiler-marker ownership and separation.

Update `Docs/folder-structure.md` to list HUD and world-label UI roots separately.
Update affected asset comments/error text where they still call the broad HUD
root the label owner.

## EditMode Tests

Add `UiPanelIsolationEditModeTests` and add `PlayGround.Ui` to EditMode test
asmdef references where required.

Methods:

- `HudRootDoesNotContainLabelsLayer`
- `WorldLabelsRootContainsExactlyOneLabelsLayer`
- `WorldLabelsPanelIsDistinctAndSortsBelowHudPanel`
- `WorldLabelElementsArePickingTransparent`
- `HudAndWorldLabelDocumentsUseDifferentPanelSettings`

Tests inspect real UXML, PanelSettings, and target-scene wiring through editor
asset APIs. They must fail if labels are reintroduced into HUD or both documents
share one panel settings asset.

## PlayMode Tests

Add `UiPanelIsolationPlayModeTests` and add `PlayGround.Ui` to PlayMode test
asmdef references where required.

Methods:

- `HudAndWorldLabelsUseDistinctRuntimePanels`
- `WorldLabelsAllowPointerInputToReachGameplaySurface`
- `PauseBackgroundRemainsAboveWorldLabels`
- `RegisteredMobCreatesBarOnlyInWorldLabelsPanel`
- `ProjectileAndAoePopulationDoesNotAddHudVisualElements`

Use test-owned fixtures or target-scene integration as appropriate. Observe
runtime panels, hierarchy, and actual pointer behavior; add no production-only
counters or flags. Population test asserts structural independence, not timing.

## Required User Test Runs

- **EditMode:** `UiPanelIsolationEditModeTests`
  - Export: `Logs/TestResults-EditMode-UiPanelIsolation.xml`
- **PlayMode:** `UiPanelIsolationPlayModeTests`
  - Export: `Logs/TestResults-PlayMode-UiPanelIsolation.xml`

Agent does not invoke Unity Test Runner and must review exported XML before
reporting any pass.

## Acceptance Criteria

- Docs no longer describe labels as HUD-root siblings.
- Tests fail on shared PanelSettings, wrong panel ordering, label picking, or
  HUD label contamination.
- Tests prove pointer and pause behavior across distinct runtime panels.
- No performance assertion depends on wall-clock timing in Unity Test Framework.
- Test-result claims follow XML-only evidence rule.

## Dependencies

Depends on tasks 001 and 002.

## Scope / Complexity

High: asset-contract tests plus cross-panel runtime pointer/pause integration.

