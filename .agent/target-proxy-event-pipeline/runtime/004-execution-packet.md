# Task Execution Packet

## Task

004-registry-and-combatroot-cleanup.md

## Goal

Remove registry entity-key bookkeeping invalidated by deferred proxy creation.

## Files Allowed To Modify

- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`
- `Assets/Scripts/System/Core/CombatRoot.cs`

## Dependencies Confirmed

- `CombatTargetProxy.Create` returns `bool`; no synchronous entity is available.

## Required Changes

- Remove `TargetsById`, `proxyKeyByTarget`, removal helper, and CombatRoot passthrough.
- Keep target list/register/unregister/configure behaviors; do not touch `CombatTargetSet`.

## Validation

- Repo-wide no-reference scan and diff check; scoped compile if possible.
