# Task Execution Packet

## Task
002-test-migration.md

## Goal
Migrate affected test source from support-owned stacking to trigger-owned stacking; remove assertions for deleted behavior.

## Files Allowed To Modify
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- Listed test files; `StackTrigger`, `SkillSetCompiler`, `SkillLoadoutValidator`, and test helpers only as needed.

## Behavior To Preserve
- Stack detonation accrues/detonates at existing hit count when trigger threshold is set equivalently.
- Additive supports on detonation set still affect inner definition.
- Nested outgoing trigger attaches to inner detonation.

## Behavior To Change
- Test authored stack configuration uses `StackTrigger`, never `StackingSupport`.
- Effect sets are plain sets with any legitimate stat supports retained.
- Replace obsolete no-support warning test with targeted-skill generic-tag warning; remove two tests for obsolete rules.
- Update root binding premise to a two-node StackTrigger chain with one root.

## Relevant Global Context
- No runtime-shape change: wrappers still exist at `StackingDetonation`, but target sets no longer intrinsically compile as wrappers.
- `StackTrigger` targets `Projectile | Aoe`; targeted target should produce `UnsupportedTriggerTarget`.

## Dependencies Confirmed
- Task 001 complete: `StackTrigger` owns fields/wrapping and support hierarchy is deleted.
- Direct source check found an obsolete `ConversionSupport`-specific test and test helper in `SkillValidationEditModeTests.cs` (`CompilerInvokesConversionSupportCompileHook`, `TrackingConversionSupport`). Delete these test-only references as direct compile fixes caused by task 001; no replacement because conversion hooks no longer exist.

## Step-By-Step Instructions
- Follow `002-test-migration.md` exactly for all identified tests.
- Delete the obsolete conversion-support test and nested helper noted above; it exercises only the deleted abstraction.
- Do not alter unrelated tests that directly construct runtime stacking snapshots.

## Acceptance Criteria
- No `StackingSupport` or `ConversionSupport` reference in test source.
- Existing stacking assertions reflect trigger-owned values.
- Exactly obsolete validator tests removed; targeted-skill case tests generic target warning.

## Validation Required
- Do not run Unity tests/test runner. Run source/static checks only, including removed-type search and formatting/diff checks.
- After implementation, user must run EditMode `SkillValidationEditModeTests` and `ProjectileContinuousAuthoringEditModeTests`, plus PlayMode `AoePlayModeTests.ProjectileImpactAoeApplicatorStackTriggerDetonatesStackSet`, exporting XML to `Logs/`.

## Hard Boundaries
- Change only listed test files.
- No production, asset, catalog, doc, log, or later-task changes.
- Do not introduce production test hooks or architecture changes.
