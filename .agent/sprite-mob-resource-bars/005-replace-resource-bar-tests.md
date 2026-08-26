# 005 — Replace Resource-Bar Tests

## Change

Remove UI Toolkit world-label assertions from
`UiPanelIsolationEditModeTests`/`UiPanelIsolationPlayModeTests`. Keep HUD input,
pause, picker, and fixed HUD element-count tests that remain valid. Rename test
files/classes if remaining scope is HUD-only.

Add EditMode read-only prefab-authoring coverage:

- each authored mob prefab has one configured `MobResourceBarSprite` child;
- background/fill renderers and shared sprite/material are assigned;
- resource-bar child is beneath mob root and has prefab-specific local
  transform;
- fill configuration is left-anchored, ratio-proportional, and sorted above
  mob visual;
- BenchmarkLarge has no old world-label panel/controller and has
  `GameRoot.gameSettings` assigned.

Tests inspect Inspector-authored files. Agent must report failures as authoring
instructions and must not patch prefab/scene serialization to make tests pass.

Add PlayMode behavior coverage in `MobResourceBarSpritePlayModeTests`:

- `MovementUsesParentTransformWithoutChangingAuthoredLocalTransform`;
- `HealthChangeSetsFillScaleProportionalToRatio`;
- `SoftDeathHidesBarAndInitializeForSpawnRestoresIt`;
- `DisplaySettingChangeControlsLiveRendererVisibility`;
- `PooledMobRestoresFullBarAfterPriorLifeDamage` (may extend
  `MobSpawnControllerPlayModeTests` when reuse fixture is clearer there).

Do not add production counters or test-only callbacks to prove refresh counts;
observe renderer enabled state, local transform, scale, health, and actual pool
reuse through public runtime behavior.

## Acceptance Criteria

- No test references `MobResourceBarUi`, `WorldLabelsUI`, `labels-layer`,
  `WorldLabelsPanel`, or dynamic UI Toolkit usage hints.
- Tests fail clearly for missing prefab renderer/child/settings wiring.
- Tests cover movement, damage, death, setting visibility, and pooled reset.
- Existing dynamically constructed bar-less mob fixtures continue working.
- User runs required suites and exports XML under `Logs/`; results are reviewed
  before any pass claim.
- Agent makes no serialized authoring-file edits in response to failures.

## Dependencies

- 001–004 complete.

## Scope / Complexity

Medium: replace obsolete UI tests and add focused prefab/runtime coverage.

