# Task Execution Packet

## Task
005-update-docs.md

## Goal
Update `skill-system.md` for two typed interval triggers and new fields.

## Files Allowed To Modify
- `Docs/reference/game-logic/skill-system.md`

## Behavior To Preserve
- On-impact projectile `spawnCount` docs remain.
- Two trigger names remain.

## Behavior To Change
- Interval projectile field is `projectileCount`.
- Interval AOE fields are `echoCount` and `scatterRadius`.
- Count fields additive; geometry fields authoritative.

## Dependencies Confirmed
- 001-004 complete.

## Validation Required
- Search doc for exact merged `IntervalSpawnTrigger` and stale interval `spawnCount` prose.

## Hard Boundaries
- Do not modify code or tests here.
