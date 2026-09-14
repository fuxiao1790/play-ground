# 005 - Launch Aim Tests

## Change

Add focused compiler, template-hash, and spawn-pipeline coverage. Extend projectile
spawn fixture with `TargetSpatialHashSystem` and real target proxy components where
integration behavior requires hash build.

## Acceptance Criteria

### EditMode - `SkillValidationEditModeTests`

Add cases proving:

- interval trigger copies nearest-hostile mode/range to projectile child only
- on-hit trigger copies policy to projectile child only
- stack trigger copies policy to projectile detonation only
- top-level/root projectile remains launch-aim disabled
- continuous projectile child retains continuous collision plus launch aim, with no
  implied tracking change
- non-projectile triggered target ignores projectile launch-aim policy

### PlayMode - `SpawnCommandUnificationTests`

Add cases proving projectile template hash:

- differs by launch-aim mode
- differs by launch-aim range
- matches for identical launch-aim content

### PlayMode - `ProjectileSpawnPipelineTests`

Add cases proving:

- nearest hostile in range determines radial nova angular origin
- same-faction nearer target is skipped
- contact-gate seed target is excluded and next nearest hostile selected
- no hostile in range preserves existing fallback pattern
- disabled policy preserves existing fallback pattern
- single-shot wave aims directly at target
- count-4 wave aimed upward produces up/left/down/right velocities in deterministic
  shot order
- successful launch aim uses same oriented radial nova even when fallback pattern is
  side-spray or forward
- disabled/no-target control retains authored side-spray/forward/radial behavior
- launch aim does not add a projectile or alter deterministic projectile IDs
- continuous command receives launch aim and materializes in continuous archetype
  without tracking
- missing hash singleton does not fail and uses fallback

Retain existing tests for directionless side-spray, radial fan-out, default heading,
discrete/continuous pool separation, continuous no-tracking, and manual forward
volley.

## Test Execution

Agent does not run tests. User runs:

- EditMode: `SkillValidationEditModeTests`
- PlayMode: `SpawnCommandUnificationTests`
- PlayMode: `ProjectileSpawnPipelineTests`
- PlayMode regression: `ProjectileContinuousSimulationTests`
- PlayMode regression: `ProjectileTrackingSimulationTests`

Export result files under `Logs/`, for example:

- `Logs/TestResults-EditMode-TriggeredProjectileLaunchAim.xml`
- `Logs/TestResults-PlayMode-TriggeredProjectileLaunchAim.xml`

Implementation review must inspect XML before reporting pass/fail.

## Dependencies

Depends on `001` through `004`.

## Estimated Scope

Medium-large: compiler permutations plus ECS integration/fallback/concurrency fixture
coverage.
