# Task Execution Packet

## Task

003-collapse-compiler-dispatch.md

## Goal

Replace trigger-subclass dispatch with one interval handler that dispatches by compiled child runtime type.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillSetCompiler.cs`

## Behavior To Preserve

- Parent eligibility and pulse-AOE no-op guard; energy/mana formulas; count formulas; jitter seed increments; existing runtime setup classes and host fields.

## Behavior To Change

- Interval trigger accepts all child shapes and attaches matching setup by compiled runtime type.

## Dependencies Confirmed

- Task 002 provides concrete `IntervalSpawnTrigger` with required fields.

## Acceptance Criteria

- No deleted trigger-type references; one handler assigns exactly the matching existing setup.

## Validation Required

- Static inspection/search. Unity tests deferred to user.

## Hard Boundaries

- Do not alter runtime setup types, ECS systems, or formulas.
