# 005 — Add UI Layer And Mob Health Regression Tests

## Change

Add test coverage for authored order, input behavior, projection lifecycle, and
pooled mob reuse. Add `PlayGround.Ui` reference to relevant test asmdef(s) only
where controller types are exercised.

## EditMode Coverage

Create `UiLayerContractEditModeTests`:

- `RootLayersAreAuthoredInBackToFrontOrder`
- `RootLayersHaveExpectedPickingDefaults`
- `MobHealthTemplateIsPickingTransparent`

Tests clone/load real UXML assets and assert named hierarchy/order rather than
duplicating a second expected UI tree in runtime code.

## PlayMode Coverage

Create `UiLayerInputPlayModeTests`:

- `DecorativeLabelsAllowClickToFire`
- `InteractiveHudControlPreventsClickToFire`
- `PauseBackgroundBlocksPopupHudLabelsAndFire`
- `PauseMenuRemainsInteractiveAboveBackground`
- `SkillPickerIsParentedToPopupLayer`

Create `MobResourceBarUiPlayModeTests`:

- `RegisteredMobCreatesOneProjectedHealthBar`
- `DamageUpdatesExistingBarWithoutRecreatingIt`
- `OffscreenMobHidesProjectedBar`
- `UnregisteredMobRecyclesProjectedBar`
- `PooledMobReuseResetsFillAndReusesVisualCapacity`

Use test-owned fixtures under `Assets/Tests/Fixtures/` where scene/UI wiring is
needed. Observe public runtime behavior and actual visual hierarchy; do not add
production-only counters or test flags. A reusable pool-capacity diagnostic may
be exposed only if useful for normal profiling, otherwise verify element identity
through test-owned hierarchy observations.

## Manual Visual Checks

- Damage several overlapping mobs near each HUD region.
- Open skill picker; verify it covers bars but remains below pause background.
- Pause while picker open; verify dimmer covers picker/HUD/labels and resume stays
  clickable.
- Click decorative label/bar space; verify firing continues.
- Resize Game view and change camera zoom; verify bars remain anchored.
- Spawn/reclaim through configured cap; watch Profiler GC allocation and UI update
  cost.

## Required User Test Runs

Agent does not invoke Unity Test Runner. User runs:

- **EditMode:** `UiLayerContractEditModeTests`
  - Export: `Logs/TestResults-EditMode-UiLayers.xml`
- **PlayMode:** `UiLayerInputPlayModeTests`, `MobResourceBarUiPlayModeTests`, and
  existing `MobSpawnControllerPlayModeTests`
  - Export: `Logs/TestResults-PlayMode-MobHealthUi.xml`

Agent must review those XML files before reporting any tests passed.

## Acceptance Criteria

- Tests fail when root sibling order changes.
- Tests fail when decorative labels intercept input.
- Tests fail when pause background does not shield lower layers.
- Tests fail when popup or pause content is attached to wrong layer.
- Tests prove registration, damage update, offscreen hiding, unregister, and pooled
  reuse behavior through public/runtime-visible effects.
- Test result claims follow project XML evidence rule.

## Dependencies

- Depends on tasks 001–004.

## Scope / Complexity

High: UI Toolkit event dispatch and pooled runtime projection require realistic
PlayMode fixtures.
