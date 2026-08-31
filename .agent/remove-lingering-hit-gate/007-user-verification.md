---
name: user-verification
description: Request focused EditMode and PlayMode verification from the user and review exported Unity result XML files.
---

# 007 - User Verification

## Depends On

Tasks 001-006 must be implemented first.

## Agent Boundary

Do not run tests or invoke the Unity test runner. Ask the user to run the tests
below. Do not claim any test passed until the corresponding exported XML under
`Logs/` has been read and verified.

## EditMode

- Platform: EditMode
- Class: `ProjectileAuthoringEditModeTests`
- Method: `SpawnTemplateEvents_AreBlittableAndContentHashed`
- Result XML: `Logs/TestResults-EditMode-RemoveLingeringHitGate.xml`

This covers the `AoeSpawnCommand` field rename and content-hash contract. The
same test also contains projectile data, but projectile cooldown names and
behavior remain unchanged.

## PlayMode

- Platform: PlayMode
- Class: `AoeSimulationTests` (entire class, including
  `LingeringHitsEveryTickWhileTargetPresent`,
  `LingeringReentryHitsOnNextTick`, and
  `ImpactArchetypeOmitsLingeringOnlyComponentsAndHasLargerChunkCapacity`)
- Class/method: `AoePlayModeTests.CombatRootRegisterSpawnTemplateDeduplicatesOnScope`
- Class/method: `CombatPoolCleanupSystemTests.TrimsEverySparsePoolInOnePass`
- Result XML: `Logs/TestResults-PlayMode-RemoveLingeringHitGate.xml`

This covers every-tick collision, AOE spawn materialization/archetypes, pool
reuse/cleanup helpers, and template registry behavior.

## Acceptance Criteria

- Both named XML files exist under `Logs/`.
- Agent reads both XML files and reports their actual results.
- No test-pass claim relies on editor logs, console output, or screenshots.
- Projectile repeat-hit cooldown behavior remains out of scope and unchanged.
