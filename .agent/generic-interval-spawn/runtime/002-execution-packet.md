# Task Execution Packet

## Task
002-collapse-compiler-dispatch.md

## Goal
Replace two interval-spawn compiler branches and private methods with one `IntervalSpawnTrigger` branch and `ApplyIntervalSpawn` method.

## Files Allowed To Modify
- `Assets/Scripts/Skills/SkillSetCompiler.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`

## Behavior To Preserve
- Parent must be projectile or AOE.
- Pulse AOE source remains no-op.
- Projectile child writes `RuntimeChildSpawnSetup` to `ChildSpawnSetup`.
- AOE child writes `RuntimeAoeIntervalSpawnSetup` to `AoeIntervalSpawnSetup`.
- Jitter seed increments when a setup is produced.

## Behavior To Change
- Dispatch uses `IntervalSpawnTrigger`, then compiled child runtime type.
- Previously mismatched projectile/AOE target combos now produce the matching setup.

## Relevant Global Context
- Runtime/ECS shapes stay unchanged.
- Compiler is authoring/compile boundary only.

## Dependencies Confirmed
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs` exists and declares `IntervalSpawnTrigger`.
- Old `ApplyChildSpawn` and `ApplyAoeIntervalSpawn` have no callers outside their current compiler branches.

## Step-By-Step Instructions
- Replace two `chain.link is ProjectileIntervalSpawnTrigger` / `AoeIntervalSpawnTrigger` branches with one `IntervalSpawnTrigger` branch.
- Replace `ApplyChildSpawn` and `ApplyAoeIntervalSpawn` with one `ApplyIntervalSpawn`.
- Share interval/jitter computation.
- Branch on `RuntimeProjectileDefinition` and `RuntimeAoeDefinition` compiled child types.

## Acceptance Criteria
- Four source/target combinations compile through one trigger.
- Pulse AOE sources remain no-op.
- No references to removed private method names remain.

## Validation Required
- Search `SkillSetCompiler.cs` for old trigger names and old method names.
- Later build/test validation will cover compile.

## Hard Boundaries
- Do not modify validator, tests, docs, or runtime/ECS files in this task.
- Do not change runtime setup structs or fields.
