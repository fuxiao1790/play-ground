# Task Execution Packet

## Task
001-add-skill-loadout-compiler.md

## Goal
Add pure `SkillLoadoutCompiler` and `CompiledLoadout` without changing `SkillDriver`.

## Files Allowed To Modify
- None.

## Files Allowed To Create
- `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Skills/SkillLoadout.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

## Behavior To Preserve
- Existing root detection, compile order, null-skip compaction, and compiler-warning order.

## Behavior To Change
- None; move pure logic behind a new callable type.

## Relevant Global Context
- Compiler reads only `SkillLoadout.Nodes`/`MaxRootSets` and snapshot.
- No fields, static mutable state, `SkillDriver`, `CombatRoot`, or `CombatVfxRoot` references.

## Dependencies Confirmed
- None required; `SkillSetCompiler` and `SkillLoadoutValidator` exist.

## Step-By-Step Instructions
- Copy root detection, per-root compile, `HasTriggeredOnlyConversionSupport`, and `AppendCompilerWarnings` into new class.
- Seed warnings from validator.
- Return fresh `CompiledLoadout` with roots, indices, count, and warning list.

## Acceptance Criteria
- New file standalone and pure.
- `SkillDriver.cs` unchanged by task 001.

## Validation Required
- Scoped source search/diff only; do not run Unity tests.

## Hard Boundaries
- Modify/create only listed file.
- Do not implement task 002.
