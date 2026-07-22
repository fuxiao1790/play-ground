# 004 — Tests & docs

## Goal
Update existing tests to the energy vocabulary, add coverage that proves the energy semantics
(cost drives cadence), and update the reference docs.

## Test changes

### `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `CompilerConvertsProjectileIntervalJitterPercentToSeconds` and the AOE analogue: rework to
  assert energy mapping. Set `trigger.energyPerSecond`, `trigger.energyJitterPercent`, and the
  child skill's `spawnEnergyCost`, then assert `setup.EnergyPerSecond`, `setup.EnergyThreshold`
  (== child `spawnEnergyCost`), and `setup.EnergyThresholdJitter`
  (== threshold * jitterPercent/100). Rename the tests accordingly.

### `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `MakeEvent` / helpers: replace `timedIntervalSeconds` with energy params
  (`energyPerSecond`, `spawnEnergyCost`); build `TimedSpawnComponent` with the energy fields.
- Update `CreateChildSpawnerEntity` and any `TimedSpawnComponent { IntervalSeconds = ... }`
  construction.
- The arm-hold and enable/disable tests keep their intent; only field names change.

### New test — energy cadence
- Add a play-mode test: two spawners with equal `energyPerSecond` but different
  `spawnEnergyCost` (e.g. 1 vs 2). Advance the sim a fixed time; assert the cheaper skill
  produced ~2x the child events of the expensive one. Also assert a spawner with
  `energyPerSecond = 0` emits nothing.

## Doc changes

### `Docs/reference/game-logic/skill-system.md`
- Update the `ProjectileIntervalSpawnTrigger` / `AoeIntervalSpawnTrigger` sections: fields are
  `energyPerSecond` + `energyJitterPercent`; describe the accrual-to-threshold model and that
  the threshold is the child skill's `spawnEnergyCost`. Update the `intervalJitterPercent`
  clamp/conversion paragraph to the threshold-jitter equivalent.
- Add `spawnEnergyCost` to the Skills / definition field descriptions.

### Simulation docs
- `Docs/reference/simulation/spawn-template-registry.md` and any timed-spawn description:
  update wording from "interval / cooldown" to "energy accrual"; reaffirm that timing (now
  energy) stays slim per-source timer config and is **not** part of the template hash.

## Acceptance criteria
- All edit/play-mode tests compile and pass against the energy fields.
- New cadence test demonstrates cost-driven cadence and the disabled (rate 0) case.
- Docs describe the energy model with no lingering interval-based wording in the travel-spawn
  sections.

## Scope
Medium. Test edits + one new test + doc edits.

## Dependencies
002 + 003.
