# Task Execution Packet

## Task
004-despawn-lane.md

## Goal
Implement ECS death detection for tagged proxies and Presentation despawn notification before proxy deletion.

## Files Allowed To Modify
- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`
- `.agent/ecs-unit-spawn/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Targets/CombatDespawnOnDeathSystem.cs`
- `Assets/Scripts/System/Presentation/CombatDespawnBridge.cs`
- `Assets/Scripts/System/Presentation/CombatTargetBridge.cs`

## Files Likely Needed For Reading
- `ResourceRegenSystem.cs`, `CombatApplyFinalizeSingleSystem.cs`, `TargetProxyDeleteApplySystem.cs`, `CombatActorSpawnBridge.cs`, `CombatApplyBridge.cs`.

## Behavior To Preserve
- Delete apply retains ownership of structural proxy deletion.
- Player proxy lacks death tag and is untouched.
- Bridges never hide/disable/reclaim actors.
- Combat replay still requires active target.

## Behavior To Change
- Tagged proxy at health <= 0 emits same-frame combat-despawn and delete events.
- Presentation clears managed proxy field and records actor despawn callback after combat replay, before delete.

## Relevant Global Context
- Death system after CombatApplyFinalizeSingleSystem and before ResourceRegenSystem.
- Delete must remain Presentation same frame; this makes no-dedupe detection safe. Add explicit comment.
- Only Presentation resolves companions. Resolver must tolerate destroyed Unity target but must not require `IsCombatTargetActive`.
- Reuse persistent NativeList; no DynamicBuffer over query or structural work.

## Dependencies Confirmed
- 001 has depleted-health guard; 002 introduced tag and event buffers; 003 confirms Presentation bridge ordering pattern.

## Step-By-Step Instructions
1. Create death system with required group ordering, persistent pending list, tagged Health query, and two event append lanes.
2. Comment no-dedupe relies on Presentation delete.
3. Extract CombatApplyBridge private resolver to `CombatTargetBridge` internal helper in Presentation; helper performs entity/companion resolution and rejects destroyed Unity objects, not inactive targets. Use from CombatApplyBridge.
4. Create despawn bridge after CombatApplyBridge/before delete; snapshot events; resolve target; clear proxy field then call OnCombatDespawned; clear lane.

## Acceptance Criteria
- One of each event for tagged depleted proxy; player excluded.
- Regen cannot preempt death decision.
- Callback after combat replay, before delete; field null after callback.
- Inactive living target is not rejected.
- No pooling/visibility action or per-frame allocation.

## Validation Required
- Static source checks and `git diff --check`. Do not run Unity tests.

## Hard Boundaries
- Do not modify actor/pool/spawner code or tests/docs.
- Do not change TargetProxyDeleteApplySystem ownership.
