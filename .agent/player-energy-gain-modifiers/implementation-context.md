# Implementation Context

## Architectural Decisions
- `UnitStatSheet` is the player stat sheet and remains the source of the three player-level energy-gain terms.
- Carry values through `SkillStatSnapshot` and `SkillStatAggregator`; do not add a `SkillStat` enum member or alter `StatModifierAccumulator`.
- Interval energy gain is a per-trigger-edge value, resolved locally from the snapshot and stored only in the compiled runtime setup.

## Global Invariants
- Fold shape is `(base + added) * (1 + increased) * postMultiplier`, with the interval energy result floored to `0.01f`.
- Neutral values preserve authored energy: base `0f`, increased `0f`, multiplier `1f`.
- New `SkillStatSnapshot` constructor parameters are trailing optional parameters so current calls compile unchanged.
- The multiplier serialized field must initialize to `1f`, including runtime-created mob sheets.

## Ownership Boundaries
- `UnitStatSheet` is shared by player and mob roots; its values are authored stat data.
- `SkillStatSnapshot` is the flat compile-time stat boundary.
- Trigger energy remains edge-owned; supports cannot modify it.

## Data Flow
- `UnitStatSheet` -> `SkillStatAggregator.Aggregate` -> `SkillStatSnapshot` -> existing interval compiler methods -> runtime setup `EnergyPerSecond`.

## Lifecycle / Allocation Rules
- No new runtime state, allocations, ECS data, or lifecycle changes.

## ECS / Job / Threading Constraints
- No ECS/job/threading changes.

## Determinism Requirements
- Preserve existing compile-time mapping and default behavior.

## Producer / Consumer Separation
- Do not introduce support-side energy-gain modifiers or a second representation of resolved energy.

## Reused Mechanisms
- Existing `UnitStatSheet` clamp/percent accessors.
- Existing snapshot aggregation and compiler snapshot parameter flow.
- Existing trigger-local scalar resolver precedent.

## Introduced Mechanisms
- Three stat-sheet and snapshot energy-gain fields.
- One interval-trigger energy resolver in task 002.

## Validation Requirements
- Verify constructor call sites remain valid, null sheet still returns `Identity`, fresh sheets expose neutral values, and relevant EditMode tests pass.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Docs/reference/game-logic/skill-system.md`
