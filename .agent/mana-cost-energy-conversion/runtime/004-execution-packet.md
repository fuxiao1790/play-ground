# Task Execution Packet

## Task
004-unified-ecs-resource-components.md

## Goal
Replace `TargetHealth` and `TargetMana` with typed neutral `Health` and `Mana` components carrying Current, Max, and RegenPerSecond.

## Files Allowed To Modify
- Target proxy/interface, combat apply, direct tests and resource references in docs.

## Files Allowed To Create
- Neutral resource component source and meta file.

## Behavior To Preserve
- Direct health `ComponentLookup` hit mutation, initial proxy seed, and save/restore setters.

## Behavior To Change
- GameObject max/regen pushes do not overwrite ECS Current; component names are neutral.

## Dependencies Confirmed
- Existing proxy has target health/mana seed and direct hit lookup.

## Acceptance Criteria
- No old component identifiers remain, seeded max/regen/current are correct, and Max push preserves Current.

## Validation Required
- Targeted static search, resource proxy tests, diff check; record Unity test blockers exactly.
