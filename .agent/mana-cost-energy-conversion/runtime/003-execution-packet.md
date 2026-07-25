# Task Execution Packet

## Task
003-trigger-link-mana-to-energy-conversion.md

## Goal
Add shared interval trigger conversion and derive thresholds from compiled child mana costs.

## Files Allowed To Modify
- Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs (new)
- Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs
- Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs
- Assets/Scripts/Skills/SkillSetCompiler.cs
- Assets/Tests/EditMode/SkillValidationEditModeTests.cs

## Relevant Global Context
- Runtime child definitions carry folded `ManaCost`.
- Conversion happens at compile/registration time; timed spawn jobs consume only baked floats.
- Preserve trigger asset data by keeping inherited serialized field names.

## Dependencies Confirmed
- Task 001 runtime `ManaCost` is compiled.
- Task 002 named supports add `SkillStat.ManaCost`.

## Acceptance Criteria
- Child threshold equals folded child mana times trigger ratio, clamped by conversion.
- Existing rate/jitter fields and behavior remain.
- Tests cover default identity, support increase, and 2x conversion.

## Validation Required
- Focused `SkillValidationEditModeTests` when project lock permits; static verification otherwise.

## Hard Boundaries
- No ECS job changes or mana consumption.
