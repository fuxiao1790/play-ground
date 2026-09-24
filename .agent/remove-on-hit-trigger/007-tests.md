---
name: tests
description: Remove or adjust every test broken by the OnHitTrigger removal
---

# 007 — Tests

## Goal
After tasks 001–006, any test that references `OnHitTrigger` as a type, or constructs an
`OnHitSpawnRef`/passes an on-hit argument to a now-shrunk constructor, fails to compile. Fix all
of them. Tests that specifically exercise on-hit-chain *behavior* are deleted (the behavior no
longer exists). Tests that only used an on-hit-related constructor as incidental scaffolding are
adjusted to the new (shorter) signature with no change to what they're testing.

## Dependencies
Tasks 001–006 complete.

## Files to Inspect (found via `grep -rl "OnHitTrigger\|OnHitSpawnRef\|ProjectileHitPayload" Assets/Tests`)

- `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs` (heaviest user — many dedicated
  `OnHitTrigger` test methods, e.g. around lines 131, 158–159, 313, 800–923, 1008–1009, 1093, 1185
  per research grep; re-grep against current file, do not trust these line numbers blindly)
- `Assets/Tests/EditMode/TargetedContractsEditModeTests.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/EditMode/SkillSoundRecursiveRegistrationEditModeTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs` and/or
  `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs` (confirm which path(s) actually exist)
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/EditMode/ProjectileAuthoringEditModeTests.cs`

## Rules for Each File

1. **Method constructs/asserts an `OnHitTrigger` chain** (e.g. creates an `OnHitTrigger` asset,
   wires it as a `TriggerLinkSlot`, then asserts the compiled `Impact*`/`OnHit*` field or the
   resulting on-hit spawn behavior): **delete the whole test method.** There is no replacement
   behavior to assert — the feature is gone, not renamed.

2. **Method's only contact with this removal is constructing `new ProjectileHitPayload(combatPayload, onHitSpawnRef)`**
   or passing an `onHitSpawn:`/`OnHitSpawnRef` argument into an otherwise-unrelated helper (e.g.
   building a plain projectile spawn command to test pooling, pipeline unification, or pool
   cleanup): **adjust the call to the new shorter signature** (drop the argument). Do not delete
   these tests — they test unrelated behavior (pool reuse, command unification, etc.).

3. **`SkillValidationEditModeTests.cs` specifically**: this file has both kinds. Read each failing
   method individually — do not bulk-delete the file. Any method whose name/body concerns
   `OnHitTrigger` validation warnings, on-hit tag compatibility, or on-hit chain compilation is
   deleted per rule 1. Any method using `CreateAsset<OnHitTrigger>(...)` merely as a generic
   "some `TriggerLink`" fixture for an unrelated assertion (rare — check each) should instead use
   `IntervalSpawnTrigger` or `StackTrigger` as the fixture type, whichever compiles with minimal
   change and doesn't alter what the test is actually checking; if no substitution preserves the
   test's intent, delete the method per rule 1 rather than force a fit.

4. After edits, re-run `grep -rn "OnHitTrigger" Assets/Tests/` — it must return nothing.

## Behavior to Preserve
- Every test unrelated to on-hit-chain behavior keeps testing exactly what it tested before.

## Behavior to Change
- Test coverage for `OnHitTrigger` chain compilation/validation/runtime firing is removed —
  correct, since the feature is removed.

## Acceptance Criteria
- `Assets/Tests/` contains no reference to `OnHitTrigger`, `OnHitSpawnRef`, or a
  `ProjectileHitPayload`/`AoeSpawnRequest`/similar constructor call with an on-hit argument.
- No test unrelated to on-hit chains had its assertions changed.

## Validation
- `grep -rn "OnHitTrigger\|OnHitSpawnRef" Assets/Tests/` returns nothing.
- **Do not run the Unity test runner.** Per project convention, name the exact tests you touched
  (platform, class, method) and hand them to the user to run; report results only from a
  user-provided XML file under `Logs/` (see `Docs/testing.md` §Result Files).
