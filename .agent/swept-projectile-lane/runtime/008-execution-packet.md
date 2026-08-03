# Task Execution Packet

## Task

008-tests.md

## Goal

Add pure authoring/geometry tests and simulation/lane-isolation regression coverage; update directly affected existing projectile tests.

## Files Allowed To Modify

- Directly affected files under `Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/`, including existing projectile collision/spawn pipeline/pool cleanup/tracking tests when assertion updates are required by the second command list/archetype.

## Files Allowed To Create

- `Assets/Tests/EditMode/CombatSweepMathEditModeTests.cs`
- `Assets/Tests/EditMode/SweptProjectileAuthoringEditModeTests.cs`
- `Assets/Tests/PlayMode/SweptProjectileSimulationTests.cs`
- A directly scoped projectile spawn-pipeline PlayMode test file if necessary.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Existing projectile collision/spawn/tracking/pool tests and fixtures; test asmdefs; all feature sources and task 008 plan.

## Behavior To Preserve

- Production code remains untouched; existing discrete/tracking tests remain meaningful.

## Behavior To Change

- Test expectations account for separate swept command list/archetype and assert feature contracts through public/real behavior.

## Relevant Global Context

- Do not add production test metadata/counters.
- Pure sweep math covers no ECS behavior. Simulation tests must use established fixtures and actual ECS effects.
- Validate full segment/no cap, nearest/pierce order, impact snap, gate, lane isolation, structural tracking exclusion, no double movement, both stats lanes.

## Dependencies Confirmed

- All feature code tasks 001–007 complete with static validation; full compile remains baseline-blocked.

## Step-By-Step Instructions

1. Follow existing fixture/asmdef styles; add pure geometry tests listed in task.
2. Add compiler/authoring tests for conflict error/refund, propagated flag paths, lint warnings and simulation-assembly exclusion.
3. Add PlayMode swept collision and isolation tests listed; update existing expansion expectations for two lists.
4. Run only feasible targeted test/compile/static checks; clearly distinguish baseline failures.

## Acceptance Criteria

- Each named regression contract covered, no test-only production code, all four paths verified.

## Validation Required

- Run added/affected tests if possible; compile/test runner baseline failures recorded exactly.

## Hard Boundaries

- No production edits or architecture changes. Do not weaken/remove existing discrete assertions.
