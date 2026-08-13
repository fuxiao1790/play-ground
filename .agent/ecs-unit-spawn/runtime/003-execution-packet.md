# Task Execution Packet

## Task
003-spawn-result-bridge.md

## Goal
Create Presentation bridge to bind newly created proxy entities to managed actors, push spawn confirmation, and destroy cancelled-create orphans.

## Files Allowed To Modify
- `.agent/ecs-unit-spawn/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Presentation/CombatActorSpawnBridge.cs`

## Files Likely Needed For Reading
- `Assets/Scripts/System/Presentation/SpawnRejectionBridge.cs`
- `Assets/Scripts/System/Targets/TargetProxyDeleteApplySystem.cs`
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`
- `Assets/Scripts/System/Targets/TargetProxyEvents.cs`

## Behavior To Preserve
- Actor remains disabled/inactive until task 005 consumes recorded callback next Update.
- Only Presentation resolves TargetCompanion.

## Behavior To Change
- Same-frame spawn results bind `TargetCompanion`, assign proxy, invoke `OnCombatSpawned`.
- Cancellation destroys unobserved proxy immediately.

## Relevant Global Context
- Presentation bridges run after Simulation and before next actor Update.
- TargetProxyDeleteApplySystem must run after this bridge.
- No DynamicBuffer references may survive a structural entity operation.

## Dependencies Confirmed
- Task 002 types/buffer/create results exist, and create system emits results.

## Step-By-Step Instructions
1. New SystemBase under Combat.Presentation, in PresentationSystemGroup and before delete apply.
2. Query scope for CombatScope plus writable TargetProxySpawnResult.
3. Complete dependency; snapshot results per scope; for each result use pending token.
4. On missing token destroy orphan directly; on success add TargetCompanion, assign proxy, call spawn callback.
5. Clear result buffer after handling each scope without retaining a DynamicBuffer over structural calls.

## Acceptance Criteria
- Confirmation assigns proxy and callback same frame.
- Companion non-null after success.
- Cancelled token yields destroyed orphan with no companion.
- Buffer clears each frame.
- No GameObject activation/visibility action.

## Validation Required
- Static source checks and `git diff --check`. Do not run Unity tests.

## Hard Boundaries
- No death system, no GameObject-side enable/reclaim, no tests/docs.
