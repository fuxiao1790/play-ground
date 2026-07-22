# Task Execution Packet

## Task

002-authoring-and-compile.md

## Goal

Move travel cadence authoring to trigger gain rate and child-definition energy cost, then map it through compilation and driver construction.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`

## Behavior To Preserve

- Burst geometry, child template keys, jitter seed assignment, and compilation structure.
- Old rate values must not migrate because old interval semantics are incompatible.

## Behavior To Change

- Triggers expose `energyPerSecond = 2f` and renamed jitter percent (with only the jitter rename retaining `FormerlySerializedAs`).
- Projectile/AOE definition bases expose default `spawnEnergyCost = 1f`.
- Compiler maps rate from trigger and positive cost from authored child definition to energy setup fields; jitter is threshold percent.
- Driver builds and gates energy timed-spawn data.

## Relevant Global Context

- `EnergyPerSecond`, `EnergyThreshold`, and `EnergyThresholdJitter` are present from task 001.
- Cost belongs to the authored child definition; never inject it from the parent or trigger.
- Use the task's existing threshold minimum constant/pattern. No new timing path or template-hash inputs.

## Dependencies Confirmed

- 001 completed: the energy fields exist in `TimedSpawnComponent`, both runtime setup types, and `ProjectileChildSpawnConfig`; scoped old-field search returned no matches.

## Acceptance Criteria

- Correct trigger and definition fields, compiler ownership mapping, driver construction, and enabled gate.

## Validation Required

- Targeted static check; full compilation is coordinated with task 003.

## Hard Boundaries

- Only task-listed files. Do not edit assets, tests, tick system, spawn application, or CombatRoot.
