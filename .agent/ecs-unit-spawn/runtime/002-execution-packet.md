# Task Execution Packet

## Task
002-handshake-contract.md

## Goal
Add deferred target-proxy spawn/despawn contracts, pass death intent into proxy creation, and make create simulation fully unmanaged by emitting spawn results.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/ICombatTarget.cs`
- `Assets/Scripts/System/Targets/TargetProxyEvents.cs`
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`
- `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs`
- `Assets/Scripts/System/Core/CombatScopeOwner.cs`
- `.agent/ecs-unit-spawn/implementation-log.md`

## Files Allowed To Create
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Presentation/SpawnRejectionBridge.cs`
- `Assets/Scripts/System/Targets/TargetProxyDeleteApplySystem.cs`
- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`

## Behavior To Preserve
- Existing create token, pending dictionaries, cancellation mechanism, created proxy component values, registry active guard, scope ownership.
- Actor bindings still wait for next task bridge.

## Behavior To Change
- Create event includes `DespawnOnDeath` byte.
- Simulation creates proxy and appends one spawn result without resolving any managed target.
- Scope owns spawn-result and combat-despawn buffers.

## Relevant Global Context
- Simulation must use unmanaged data only; `TargetCompanion` resolution belongs in Presentation.
- Death opt-in is a static creation tag; player has none.
- No DynamicBuffer reference survives `CreateEntity` or other structural call.
- Every lifecycle declaration has `ECS Lifecycle:` comment.

## Dependencies Confirmed
- `CombatTargetProxy.Create`, `TryTakePendingCreate`, target proxy archetype, create buffer, and CombatScopeOwner exist.
- Task 001 completed; no code dependency.

## Step-By-Step Instructions
1. Add three default interface members exactly per task contract.
2. Add `DespawnOnDeathTag` beside proxy tag with lifecycle comment.
3. Extend create event and add two event-buffer types with lifecycle comments.
4. Set create event death flag from `target.CombatDespawnOnDeath`.
5. Query scope with read/write create and spawn-result buffers; use a reused NativeList to collect results while creating proxies.
6. Remove pending target resolution, companion add, and proxy assignment from Simulation; conditionally tag each proxy and append collected results after structural calls.
7. Add both buffers once in CombatScopeOwner.Acquire.

## Acceptance Criteria
- Defaults keep player/tests unchanged.
- Create simulation has no `ICombatTarget`, companion, or pending-create consumption.
- One token/proxy result per created entity.
- Scope owns new buffers without duplicate addition.
- No DynamicBuffer held across structural call.
- Lifecycle comments present.

## Validation Required
- Static source inspection and `git diff --check`. Do not run Unity tests.

## Hard Boundaries
- Do not implement bridge, death system, GameObject changes, tests/docs.
- Do not relax CombatTargetRegistry active guard.
