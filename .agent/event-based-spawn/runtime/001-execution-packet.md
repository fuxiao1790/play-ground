# Task Execution Packet

## Task
001-spawn-request-contract.md

## Goal
Declare the unmanaged managed-to-ECS `CombatSpawnRequest` buffer element, minimal `CombatSpawnResult`, and its result singleton contract; attach only the request buffer to the shared combat scope.

## Files Allowed To Modify
- `Assets/Scripts/System/Core/CombatScopeOwner.cs`
- Existing/new spawn-contract source alongside `ProjectileSpawnEvent` and AOE spawn events.
- Existing/new application-result-contract source alongside `CombatApplyResultSingleton`.

## Files Allowed To Create
- A spawn request/result contract source file if matching existing organization best.

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `CombatScopeOwner.cs`
- `ProjectileSpawnPipeline.cs`
- `AoeSpawnPipeline.cs`
- `CombatApplyResults.cs`

## Behavior To Preserve
- Current spawn event buffers and all producers/consumers remain unchanged.
- No request/result producer or consumer behavior is introduced.

## Behavior To Change
- The scope entity gains `DynamicBuffer<CombatSpawnRequest>` during acquire.

## Relevant Global Context
- Payloads are plain unmanaged data; caster is a target-proxy `Entity`, with `Entity.Null` for ownerless submissions.
- Result lane is a system singleton, not scope-owned. Its list lifecycle belongs to future `SpawnIntakeSystem`.
- The request represents pre-authorization intent; events remain post-authorization ECS data.

## Dependencies Confirmed
- None; task 001 has no prerequisite.

## Step-By-Step Instructions
1. Define `CombatSpawnRequest : IBufferElementData` with Kind, TemplateKey, Position, AimDirection, Faction, SourceId, JitterSeed, ContactGateSeedTargetId, and Caster.
2. Define minimal `CombatSpawnResult` with Caster and inline note that mana adds outcome/id/cost fields later.
3. Define `CombatSpawnResultSingleton : IComponentData` with `NativeList<CombatSpawnResult> Results` and `JobHandle ProducerHandle`.
4. Add `CombatSpawnRequest` buffer during `CombatScopeOwner.Acquire`.
5. Do not remove or use existing event buffers.

## Acceptance Criteria
- Project compiles and scope acquire includes the request buffer.
- Request buffer/result lane remain inert.
- Existing spawns have no behavior change.

## Validation Required
- Compile/build check if available plus static checks confirming the buffer add and no request/result consumers.

## Hard Boundaries
- Do not modify files outside the allowed list except imports directly required by this task.
- Do not introduce producer/consumer systems or bridge behavior.
- Do not remove legacy event buffers or alter current spawn flow.
