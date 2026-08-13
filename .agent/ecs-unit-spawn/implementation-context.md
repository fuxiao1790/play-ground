# Implementation Context

## Architectural Decisions
- Actor Update submits proxy intent; Simulation creates/detects; Presentation pushes results; actors act on pushed data next Update.
- ECS knows only `DespawnOnDeathTag`; no prefab, pool, sprite, instance identity, or domain identity in ECS.
- Target proxy creation emits result; managed companion binding and actor callbacks occur only in Presentation.
- Death emits distinct combat-despawn and proxy-delete events. Delete remains structural lifecycle cleanup.

## Global Invariants
- Simulation never reads managed companions or Unity objects.
- Only presentation bridges resolve `TargetCompanion`.
- Player has no death-despawn tag and is unaffected.
- Killing hit reaches actor before despawn is pushed; bridges run before proxy delete apply.
- In-flight spawns count toward SpawnController cap.
- Proxy components do not encode domain identity.

## Ownership Boundaries
- Actor owns Unity state, pool actions, visual/physics shutdown, and proxy intent.
- ECS owns proxy runtime health, regen, collision and death decision.
- Presentation bridges transfer finalized ECS results to actors.

## Data Flow
- Create event with token and death flag -> Simulation creates proxy + spawn result -> Presentation binds companion/calls actor -> next Update enables mob.
- Health <= 0 on tagged proxy -> despawn and delete events -> Presentation records actor despawn -> delete applies -> next Update actor reclaims pool.

## Lifecycle / Allocation Rules
- Pool rent returns disabled object; pending lists reusable.
- Target proxy entities create/destroy once per mob lifecycle; no identity/prefab in ECS.
- Actor cannot run before confirmed proxy; no double-return from teardown delete.

## ECS / Job / Threading Constraints
- Simulation data only unmanaged ECS components/buffers.
- Structural deletion only TargetProxyDeleteApplySystem in Presentation.
- Presentation system ordering: CombatApplyBridge, spawn bridge, despawn bridge, delete apply.

## Determinism Requirements
- Current frame ordering prevents duplicate death emissions because delete follows same-frame push.

## Producer / Consumer Separation
- Actors produce create/update/delete intent. Simulation produces result/despawn events. Presentation consumes events and touches managed state.

## Reused Mechanisms
- `TargetProxyCreateEvent`, pending-create token dictionary, scope buffers, `TargetCompanion`, `SpawnRejectionBridge` pattern.

## Introduced Mechanisms
- `DespawnOnDeathTag`, spawn-result event, combat-despawn event, spawn/despawn Presentation bridges, SpawnController pending-confirmation tracking.

## Validation Requirements
- Do not run Unity tests or test runner. User must run Unity PlayMode command exporting XML; agent reviews XML before claiming results.
- Use static/source checks as task validation when applicable.

## Files / Systems Mentioned By The Plan
- ResourceRegenSystem, ICombatTarget, TargetProxyEvents, CombatTargetProxy, TargetProxyCreateApplySystem, CombatScopeOwner, CombatActorSpawnBridge, CombatDespawnOnDeathSystem, CombatDespawnBridge, TargetProxyDeleteApplySystem, MobPool, SpawnController, MobRoot.
