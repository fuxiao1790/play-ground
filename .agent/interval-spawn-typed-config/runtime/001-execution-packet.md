# Task Execution Packet

## Task
001-trigger-fields.md

## Goal
Restore/keep two interval trigger types and give each type-specific fields.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset`
- Matching `.meta` files.

## Behavior To Preserve
- Projectile trigger source tags `Projectile | Aoe`, target tag `Projectile`.
- AOE trigger source tags `Projectile | Aoe`, target tag `Aoe`.
- Existing interval seconds/jitter semantics.
- Projectile GUID continuity for the current interval trigger script/asset.

## Behavior To Change
- `spawnCount` becomes `projectileCount` on projectile trigger.
- `spawnCount` becomes `echoCount` on AOE trigger.
- AOE trigger replaces `sideSpreadDegrees` with `scatterRadius`.
- Current merged `IntervalSpawnTrigger` is split back into the two planned trigger types.

## Dependencies Confirmed
- Current code has merged `IntervalSpawnTrigger`, so this task must restore two trigger files as the updated plan requires.
- Current script GUID `a1000000000000000000000000000011` is reused for `ProjectileIntervalSpawnTrigger`.
- AOE script GUID from plan is `963edcf1cd09aa84e92b45418222855c`.

## Validation Required
- Confirm projectile and AOE trigger files/assets exist.
- Confirm no `IntervalSpawnTrigger` class remains after later compile updates.

## Hard Boundaries
- Do not update compiler/runtime/template/tests/docs in this task.
