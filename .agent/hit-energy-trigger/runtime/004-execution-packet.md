# Task Execution Packet

## Task
004-regression-tests.md

## Goal
Replace legacy feature tests with HitEnergy vocabulary and add regression coverage for float accumulation, multiplier formulas, template authority, adjacency, and edge-local identity.

## Files Allowed To Modify
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs`
- `Assets/Tests/EditMode/TriggerEnergyCompilerEditModeTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`
- Other test files containing removed task-001-003 symbols or directly needed for specified coverage
- `.agent/hit-energy-trigger/implementation-log.md`

## Files Allowed To Create
- Focused HitEnergy EditMode/PlayMode test files and matching `.meta` files if cleaner than expanding existing classes.

## Files Allowed To Delete
- None, except obsolete test-only files if task cannot be completed through migration; report any such deletion explicitly.

## Files Likely Needed For Reading
- New production `HitEnergyPayload`, `TargetHitEnergy`, `HitEnergyActivationSystem`, compiler/runtime edge, SkillDriver payload builder, and existing test helpers.

## Behavior To Preserve
- Existing non-feature test intent and unrelated combat assertions.
- Interval trigger behavior.
- Accepted-hit `CombatTickResult.HitCount` behavior.

## Behavior To Change
- All legacy stack/debuff/detonation fixtures/assertions become HitEnergy fixtures/assertions.
- Integer count assertions become float energy assertions with explicit tolerances.
- Tests prove independent multipliers, edge identity/isolation, registered-template authority, bounds, expiry, and next-update timing.

## Relevant Global Context
- Effective formulas: source energy times contribution multiplier; target energy times requirement multiplier.
- Each compiled edge has unique `AccumulatorId`, even for reused assets/equal values.
- Source payload includes only its adjacent outgoing edge.
- Activation processor runs before finalizer, retaining fractional remainder and capped overflow.
- Output stats come only from registered triggered template.

## Dependencies Confirmed
- Tasks 001-003 production symbols exist.
- Production legacy-feature symbol search is clean.
- Test legacy references are isolated by current search inventory.

## Step-By-Step Instructions
1. Migrate every test reference to removed feature types and fields.
2. Add compiler coverage for all skill kinds and `RuntimeHitEnergyTrigger` composition/copy semantics.
3. Add payload formula coverage for `2 * 0.5 = 1` and `4 * 1.5 = 6`.
4. Add stable distinct accumulator id and same-asset three-skill/two-edge adjacency coverage.
5. Preserve direct-root exclusion and interval-trigger regression coverage.
6. Convert simulation fixtures to `HitEnergyPayload`, `TargetHitEnergy`, `HitEnergyActivationSystem`, and `HitEnergyProgress`.
7. Add `0.4 + 0.4 + 0.4` against `1.0` => one activation and `0.2` remainder, explicit tolerance.
8. Cover large/capped overflow, expiry, projectile/impact/lingering outputs, targeted source, buffer-full refresh/drop, unknown kind, template authority, and next-update timing.
9. Ensure tests use only new domain symbols.

## Acceptance Criteria
- Tests fail on asset/type/template/equal-energy id deduplication.
- Tests fail if source directly funds non-adjacent edges.
- Projectile, AOE, targeted sources and projectile/AOE outputs covered.
- Floating tolerances and next-update timing explicit.
- No removed feature symbols remain under `Assets/Tests`.

## Validation Required
- User deferred final validation until task 005 ends.
- Perform static symbol/fixture inspection and `git diff --check` only.
- Do not run Unity tests or Unity test runner.
- Final handoff must request `Logs/TestResults-EditMode-HitEnergyTrigger.xml` and `Logs/TestResults-PlayMode-HitEnergyTrigger.xml`.

## Hard Boundaries
- Test changes only; no production behavior changes except direct compile fixes strictly caused by test-visible API inconsistency, which must be reported.
- Do not weaken assertions merely to compile.
- Do not add production test hooks.
- Do not implement task 005 assets/docs.
- Stop on architectural ambiguity.
