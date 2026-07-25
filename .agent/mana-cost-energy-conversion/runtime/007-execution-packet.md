# Task Execution Packet

## Task
007-ecs-resource-regen-system.md

## Goal
Add a serial ECS regen system that updates neutral Mana and live Health after combat application.

## Files Allowed To Modify
- New combat resource system and directly affected PlayMode tests.

## Behavior To Preserve
- Health does not revive after reaching zero. Regen zero is a no-op.

## Dependencies Confirmed
- `Health`/`Mana` carry Current, Max, and RegenPerSecond; combat apply directly mutates Health.

## Acceptance Criteria
- Mana clamps at Max, health zero regen does not change, health regen follows damage and skips depletion.

## Validation Required
- Focused ECS test where Unity available; static ordering/search and diff check otherwise.
