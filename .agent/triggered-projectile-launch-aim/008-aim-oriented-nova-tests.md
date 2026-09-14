# 008 - Aim-Oriented Nova Tests

## Change

Replace obsolete shot-replacement/RNG-preservation expectations in
`ProjectileSpawnPipelineTests` with aim-oriented nova expectations.

## Acceptance Criteria

- Count `1`: only projectile points at nearest hostile.
- Count `4`, target above origin: deterministic shot order is up, left, down,
  right, within float tolerance.
- Count `3`, target at arbitrary non-axis direction: shot `0` matches target and
  pairwise angular spacing is `120` degrees.
- Stored fallback pattern `SideSpray` plus successful acquisition still produces
  aim-oriented radial nova.
- Stored fallback pattern `Forward` plus successful acquisition still produces
  aim-oriented radial nova.
- Disabled policy and failed acquisition retain existing pattern outputs.
- Same-faction and contact-gate exclusions still select correct target.
- Continuous template produces same oriented nova and remains no-tracking.
- Count and deterministic projectile IDs unchanged.

Remove/rename tests asserting non-aimed shots or RNG sequence match disabled-policy
control, because those assertions encode superseded behavior.

## Test Execution

Agent does not run tests. User runs PlayMode:

- `ProjectileSpawnPipelineTests`
- regression `ProjectileContinuousSimulationTests`
- regression `ProjectileTrackingSimulationTests`

Export `Logs/TestResults-PlayMode-TriggeredProjectileAimOrientedNova.xml` for review.

## Dependencies

Depends on `007-aim-oriented-nova.md`.

## Estimated Scope

Small-medium PlayMode test revision.
