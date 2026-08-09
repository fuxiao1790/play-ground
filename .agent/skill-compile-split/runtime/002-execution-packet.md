# Task Execution Packet

## Task
002-rewire-skilldriver-compile-and-register.md

## Goal
Replace inline pure compilation in `SkillDriver.CompileAndRegister` with `SkillLoadoutCompiler.Compile` while retaining slot-state reconciliation and registration in `SkillDriver`.

## Files Allowed To Modify
- `Assets/Scripts/Skills/SkillDriver.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`

## Behavior To Preserve
- Snapshot aggregation timing.
- Cooldown preservation/reset behavior.
- Compiled root order, node indices, active count, warning order, and registration calls.

## Behavior To Change
- Pure root detection, per-root compile, and compile-warning traversal move behind `SkillLoadoutCompiler`.

## Relevant Global Context
- Compiler output flows into driver-owned state reconciliation, combat registration, and published warnings.
- Registration warnings append to `compiled.Warnings` before `ToArray()` publication.

## Dependencies Confirmed
- `SkillLoadoutCompiler.Compile` and `CompiledLoadout` exist in `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`.

## Step-By-Step Instructions
- Keep preserve/reset bookkeeping and snapshot aggregation.
- Call compiler immediately after snapshot aggregation.
- Rebuild driver arrays/state from compiler output and preserve cooldown state with `FindPreservedState`.
- Keep registration calls unchanged, passing compiler warning list to spawn-template registration.
- Delete now-dead root detection and warning helper methods.

## Acceptance Criteria
- No duplicate pure compile path remains.
- `CompileAndRegister` order is aggregate -> compile -> state reconcile -> register -> publish warnings.
- No other references to deleted private methods.

## Validation Required
- Scoped search/diff and structural checks only; do not run Unity tests.

## Hard Boundaries
- Modify only `SkillDriver.cs` for task implementation.
- Do not change registration behavior or unrelated methods.
