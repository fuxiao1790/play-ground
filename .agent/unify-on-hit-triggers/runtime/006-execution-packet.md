# Task Execution Packet

## Task

006-interval-trigger-attribute-removal.md

## Goal

Remove effect-describing interval trigger fields and setup overrides so interval children use their own compiled count, spread, echo count, and scatter radius.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Interval trigger, compiler's `ApplyIntervalSpawn`, runtime setups, interval template registration/building, child-spawn behavior constructor, existing interval tests.

## Behavior To Preserve

- Interval energy/cost conversion, generic validation, `SideSpray`, jitter, timed spawning, and root child-resolution floors.

## Behavior To Change

- Interval projectile uses child count/spread; AOE uses child echo/scatter; targeted uses child echo.

## Relevant Global Context

- Child skill set owns what it spawns; `TriggerLink` prices the link; trigger owns when it fires.

## Dependencies Confirmed

- None; task is independent. Existing compiler/setup/template override paths and test assignment exist.

## Step-By-Step Instructions

1. Delete four interval trigger fields and serialization import/attribute if unused.
2. Remove interval setup override fields and compiler assignments.
3. Simplify AOE template builder signature/callers and targeted template overwrite.
4. Remove affected existing test assignment; add child count/spread ownership test.

## Acceptance Criteria

- No removed trigger attributes remain in executable code; setups/builders no longer carry overrides; new test pins child burst ownership.

## Validation Required

- Static searches/source inspection and compile-only build. User runs Unity tests and provides XML.

## Hard Boundaries

- Do not touch assets/YAML, validator, AOE interval spawning pattern, or any unrelated behavior.
