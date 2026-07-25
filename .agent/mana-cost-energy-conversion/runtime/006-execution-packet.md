# Task Execution Packet

## Task
006-docs-update.md

## Goal
Update skill, spawn registry, and todo documents for the new mana-cost and ECS mana shapes.

## Files Allowed To Modify
- Docs/reference/game-logic/skill-system.md
- Docs/reference/simulation/spawn-template-registry.md
- Docs/todo.md

## Relevant Global Context
- One source of truth: authored mana -> compiled fold -> trigger conversion -> threshold.
- `TargetMana` mirrors `TargetHealth` lifecycle; consumption is deferred.

## Dependencies Confirmed
- Tasks 001 through 004 are complete.

## Acceptance Criteria
- No active `spawnEnergyCost` documentation remains.
- Docs explain support folding, trigger conversion, ECS mana ownership, and deferred consumption.

## Validation Required
- Search Docs for non-historical `spawnEnergyCost` references.

## Hard Boundaries
- Do not document a mana drain implementation that does not exist.
