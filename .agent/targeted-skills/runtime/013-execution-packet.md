# Task Execution Packet

## Task

013-validation-warnings.md

## Goal

Surface targeted authoring clamps, errors, and silent-failure warnings through
`SkillDriver.ValidationWarnings` without changing targeted runtime behavior.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Skills/SkillValidationWarning.cs`
- Focused EditMode validation tests.
- `.agent/targeted-skills/implementation-log.md`

## Relevant Global Context

- Validation is advisory except documented errors. `acquireRadius <= 0` blocks spawn.
- Targeted has separate single-hit (`lifetimeSeconds == 0`) and lingering (`> 0`) variants.
- Single-hit delayed walks use a compiler-generated fail-safe lifetime of
  `maxTargets * chainDelaySeconds + 1/60`; no authored single-hit lifetime exists.
- Do not change runtime/authoring architecture to satisfy validation.
- Unity tests are user-run only; require an XML result before claiming pass.

## Dependencies Confirmed

- Tasks 009, 010, and 011 code exists: targeted compiler/runtime definitions,
  `TargetedIntervalSpawnTrigger`, and targeted support tags.
- Existing validator already implements most targeted warning paths.

## Required Work

- Warn when an interval walk exceeds `tickIntervalSeconds` and independently
  when it exceeds lingering `lifetimeSeconds`.
- Warn when a targeted interval child's energy threshold exceeds its
  projectile/lingering-AOE source's lifetime capacity.
- Add focused coverage and validate statically; defer Unity execution to user.

## Acceptance Criteria

- One EditMode assertion per warning/error case, correct severity.
- Compiled clamps match documented bounds.
- Valid targeted loadout has zero warnings.
- `acquireRadius <= 0` blocks spawn.

## Hard Boundary

Do not add a single-hit `lifetimeSeconds` field. Single-hit delayed walks retain
their compiler-generated fail-safe lifetime and are covered by task 005
behavioural tests, not validation warnings.
