# 004 - Add Eligibility And Tick Result Regression Tests

## Goal

Prove default compatibility, selected-faction behavior across every independent
simulation path, and aggregate bridge result semantics.

## Changes

1. Add EditMode truth-table coverage for shared eligibility helper:
   - hostile mode rejects same and accepts different faction;
   - selected mode accepts selected faction, including same-as-target faction;
   - selected mode rejects non-selected faction;
   - `CombatFaction.None` always rejects;
   - unknown mode rejects.

2. Extend collision integration coverage:
   - `ProjectileCollisionSimulationTests`: discrete selected-faction same-faction
     hit and unselected-faction rejection;
   - `ProjectileContinuousSimulationTests`: same cases for continuous collision;
   - `AoeSimulationTests`: selected-faction behavior through shared
     `AoeCollisionCore`, covering at least impact plus lingering regression where
     needed to prove tick/repeat gate remains intact.

3. Extend target-selection coverage:
   - `TargetedResolveEditModeTests`: root/chain selection chooses policy-eligible
     target and skips nearer policy-ineligible target;
   - `ProjectileSpawnPipelineTests`: nearest launch aim uses policy, including
     eligible same-faction target, and the selected target orients the full
     triggered radial nova;
   - `ProjectileTrackingSimulationTests`: acquire/refresh keeps selected-faction
     target and rejects a closer unselected target.

4. Extend result/finalizer coverage:
   - direct hits retain damage/crit aggregation;
   - non-damaging/status-only accepted events now increment `HitCount`;
   - multiple mixed events aggregate into one target result with total accepted
     count and direct-damage-only totals;
   - world time set to known delta yields matching
     `CombatTickResult.TickDeltaSeconds`;
   - test-owned `ICombatTarget` listener captures bridge callback and observes
     identical `HitCount` and tick delta;
   - presentation replay still occurs once for aggregated target result.

5. Update existing fixtures constructing `TargetFaction` or proxy creation
   requests to use hostile-default policy explicitly where clarity helps. Do not
   add production-only hooks.

## Acceptance Criteria

- Each distinct consumer of faction eligibility has behavioral coverage.
- Existing same-faction rejection tests continue passing under hostile-default
  mode.
- New tests prove same-faction acceptance occurs only under selected-faction
  mode.
- Result tests distinguish accepted-hit count from damage and crit counts.
- Bridge test observes final values through public callback contract.
- Tests do not depend on unordered queue iteration.
- No concrete summon/firing-proxy fixture or GameObject behavior is introduced.

## User-Run Verification

Agent must not run tests. Request these suites from user after implementation:

- **EditMode:**
  - new faction eligibility test class;
  - `PlayGround.Tests.EditMode.TargetedResolveEditModeTests` relevant eligibility
    methods;
  - `PlayGround.Tests.EditMode.CombatHitDamageScaleEditModeTests` including new
    count/tick-delta methods.
  - Export XML: `Logs/TestResults-EditMode-EcsHitEligibility.xml`.
- **PlayMode:**
  - `ProjectileCollisionSimulationTests`;
  - `ProjectileContinuousSimulationTests`;
  - `AoeSimulationTests` relevant eligibility/result methods;
  - `ProjectileTrackingSimulationTests` relevant eligibility methods;
  - `ProjectileSpawnPipelineTests` relevant nearest-aim methods.
  - Export XML: `Logs/TestResults-PlayMode-EcsHitEligibility.xml`.

Review XML files before claiming any test passed.

## Dependencies

- Tasks 001-003.

## Estimated Scope / Complexity

High. Multiple test harnesses cover distinct ECS jobs and scheduling paths.
