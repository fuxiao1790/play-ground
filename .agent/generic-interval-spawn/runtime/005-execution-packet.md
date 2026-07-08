# Task Execution Packet

## Task
005-update-docs.md

## Goal
Update `skill-system.md` to describe one generic `IntervalSpawnTrigger`.

## Files Allowed To Modify
- `Docs/reference/game-logic/skill-system.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Docs/reference/game-logic/skill-system.md`

## Behavior To Preserve
- Runtime setup descriptions remain distinct for projectile children and AOE children.
- Pulse AOE source warning/no-op remains documented.
- Directionality and spawn count semantics remain unchanged.

## Behavior To Change
- Replace separate projectile/AOE interval trigger descriptions with one `IntervalSpawnTrigger`.
- Compile pseudocode dispatches on compiled child runtime type.
- Examples use `IntervalSpawn`.

## Relevant Global Context
- Shipped code now has one merged interval trigger.
- ECS/runtime data shapes remain unchanged.

## Dependencies Confirmed
- Tasks 001-004 complete locally.

## Step-By-Step Instructions
- Collapse trigger subsections and support table.
- Update warning prose and compile pseudocode.
- Update examples and sweep old literal names.

## Acceptance Criteria
- No remaining references to old interval trigger names in `skill-system.md`.
- Doc states one trigger supports all four source/target combinations.

## Validation Required
- Search doc for `ProjectileIntervalSpawnTrigger`, `AoeIntervalSpawnTrigger`, `ProjectileIntervalSpawn`, and `AoeIntervalSpawn`.

## Hard Boundaries
- Do not modify code or tests in this task.
