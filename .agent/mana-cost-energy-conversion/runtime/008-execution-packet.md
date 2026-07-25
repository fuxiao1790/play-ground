# Task Execution Packet

## Task
008-unified-managed-resource-and-roots.md

## Goal
Replace player-specific holders and MobRoot inline health with a shared managed Resource mirror.

## Files Allowed To Modify
- UnitStatSheet, PlayerRoot, MobRoot, target proxy read helpers, direct tests, and obsolete resource files.

## Files Allowed To Create
- Shared managed Resource source and meta file.

## Files Allowed To Delete
- PlayerHealth and PlayerMana source/meta files after callers migrate.

## Behavior To Preserve
- Player death/hurt presentation, save restore, mob soft death/pool reuse, and per-root reactions.

## Behavior To Change
- Roots mirror ECS Current and push only authored Max/regen updates; all managed resource holders are Resource.

## Dependencies Confirmed
- Neutral components, push API, and regen system exist.

## Validation Required
- Static old-type search, existing root tests when Unity is available, and diff check.
