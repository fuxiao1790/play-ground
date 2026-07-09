# Task Execution Packet

## Task
002-wire-offensive-snapshot.md

## Goal
Make the caster's `UnitStatSheet` the source of the offensive `SkillStatSnapshot`.

## Files Allowed To Modify
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`

## Behavior To Preserve
- `SkillStatSnapshot` shape and `SkillSetCompiler` fold remain unchanged.
- Null stat sheet keeps identity behavior.
- Aggregation still runs from `CompileAndRegister`, not per-frame.

## Behavior To Change
- Aggregator accepts a `UnitStatSheet` and reads offense values.
- `SkillDriver` stores the caster sheet and passes it during compile.

## Relevant Global Context
- Offensive sheet fields map 1:1 to `SkillStatSnapshot`.
- Do not create a second offensive stat path.

## Dependencies Confirmed
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs` exists.
- `UnitStatSheet` exposes `IncreasedRatePercent`, `DamageMultiplier`, `CritChance`, `CritMultiplier`, and `AreaSizeMultiplier`.

## Step-By-Step Instructions
- Add `using PlayGround.Common.Stats;` where needed.
- Change `SkillStatAggregator.Aggregate(SkillLoadout loadout)` to `Aggregate(SkillLoadout loadout, UnitStatSheet sheet)`.
- Return identity for null sheet; otherwise construct a snapshot from sheet offense values.
- Add `[SerializeField] private UnitStatSheet statSheet;` to `SkillDriver`.
- Change `CompileAndRegister` call site to pass `statSheet`.

## Acceptance Criteria
- Compiles.
- One existing caller updated.
- Sheet offense values flow into runtime definitions.
- Null sheet preserves identity baseline.

## Validation Required
- Search for old aggregate signature/call sites.
- Compile/build if available.

## Hard Boundaries
- Do not modify files outside the allowed list except compile fixes directly caused by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
