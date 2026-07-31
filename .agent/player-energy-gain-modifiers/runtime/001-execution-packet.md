# Task Execution Packet

## Task
001-player-energy-gain-stat-fields.md

## Goal
Add base, increased-percent, and post-multiplier player energy-gain fields to `UnitStatSheet`; carry them through `SkillStatSnapshot` and `SkillStatAggregator.Aggregate`.

## Files Allowed To Modify
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`
- constructor call sites found with `new SkillStatSnapshot(`

## Behavior To Preserve
- Existing stat fields and accessors.
- Existing five-argument positional and named snapshot construction.
- `Aggregate(null, sheet)` returns `SkillStatSnapshot.Identity`.
- Fresh player and runtime-created mob sheets resolve neutral energy-gain values.

## Behavior To Change
- The aggregate snapshot exposes the three configured energy-gain values.

## Relevant Global Context
- Source of truth is `UnitStatSheet`; snapshot is the compile-time stat boundary.
- No `SkillStat` or modifier accumulator changes.
- Defaults are base `0f`, increased `0f`, multiplier `1f`; append optional constructor parameters.

## Dependencies Confirmed
- None; task 001 is the base task.

## Step-By-Step Instructions
1. Add the specified serialized Duration Skill Energy field group and clamped accessors.
2. Append optional snapshot constructor parameters and public properties.
3. Pass sheet accessors to `SkillStatAggregator.Aggregate`.
4. Do not change `SkillStatSnapshot.Identity`.

## Acceptance Criteria
- New serialized fields/accessors compile without modifying existing fields.
- Existing snapshot call sites need no edits.
- Null sheet aggregation is unchanged.
- Fresh sheets resolve `0f`, `0f`, `1f`.

## Validation Required
- Search constructor call sites and inspect defaults/null branch.
- Run relevant compile or EditMode validation when available.

## Hard Boundaries
- Do not modify trigger/compiler/test/doc files in this task.
- Do not change architecture or introduce new abstractions.
