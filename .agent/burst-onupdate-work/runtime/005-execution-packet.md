# Task Execution Packet

## Task
005-target-proxy-apply-jobs.md

## Goal
Schedule proxy updates in Burst and batch proxy deletion after Burst collection.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/TargetProxyUpdateApplySystem.cs`
- `Assets/Scripts/System/Targets/TargetProxyDeleteApplySystem.cs`
- `.agent/burst-onupdate-work/implementation-log.md`

## Behavior To Preserve
- Exact four update kinds and clamps; each event buffer clears once.
- Delete runs after combat apply and destroys only entities that still exist.

## Behavior To Change
- Update has no `Dependency.Complete`; delete uses one batch `DestroyEntity` call after job collection.

## Relevant Global Context
- No ordering changes, no structural changes in jobs. Delete completion remains necessary before single structural operation.

## Dependencies Confirmed
- Scope buffers and proxy component types exist; `EntityStorageInfoLookup.Exists` is job-safe in installed Entities.

## Validation Required
- Static checks; user-run proxy/collision/targeted Unity tests with XML required.

## Hard Boundaries
- Do not touch proxy create system or lifecycle ordering attributes.
