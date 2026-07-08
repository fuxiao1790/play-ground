# Task Execution Packet

## Task
001-merge-trigger-type.md

## Goal
Rename `ProjectileIntervalSpawnTrigger` to `IntervalSpawnTrigger`, widen target tags, rename matching fixture asset, and delete broken AOE interval trigger files.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs` renamed to `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs.meta` renamed to `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs.meta`
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset` renamed to `Assets/ScriptableObjects/Triggers/IntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset.meta` renamed to `Assets/ScriptableObjects/Triggers/IntervalSpawnTrigger.asset.meta`

## Files Allowed To Delete
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs.meta`
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset.meta`

## Files Likely Needed For Reading
- Existing trigger script files and metas.
- Existing trigger asset files and metas.

## Behavior To Preserve
- Source tags remain `Projectile | Aoe`.
- Interval fields and fixture authored values remain unchanged.
- Projectile trigger script GUID and asset GUID remain attached to renamed files.

## Behavior To Change
- Target tags become `Projectile | Aoe`.
- Create menu and default file name become generic interval spawn.
- Old AOE trigger type and broken asset are removed.

## Relevant Global Context
- Runtime/ECS setup types remain unchanged.
- This is authoring rename only; compiler, validator, tests, and docs follow in later tasks.

## Dependencies Confirmed
- No prerequisite task.
- Existing `ProjectileIntervalSpawnTrigger.cs.meta` has GUID `a1000000000000000000000000000011`.
- Existing `AoeIntervalSpawnTrigger.asset` points at script GUID `a1000000000000000000000000000012`, matching the known broken fixture state.

## Step-By-Step Instructions
- Move projectile trigger script and meta to `IntervalSpawnTrigger.cs` names.
- Change class/menu/file name and target tags.
- Move projectile trigger asset and meta to `IntervalSpawnTrigger.asset` names.
- Update `m_Name` and `m_EditorClassIdentifier` in the asset.
- Delete AOE trigger script/meta and AOE trigger asset/meta.

## Acceptance Criteria
- `IntervalSpawnTrigger` exists as the merged authoring type.
- Old trigger classes no longer exist as symbols.
- Renamed asset still references GUID `a1000000000000000000000000000011`.

## Validation Required
- Search for old class declarations after task.
- Confirm renamed files exist and deleted files do not.

## Hard Boundaries
- Do not modify compiler, validator, tests, docs, or runtime files in this task.
- Do not change runtime/ECS data shapes.
- Do not introduce new abstractions.
