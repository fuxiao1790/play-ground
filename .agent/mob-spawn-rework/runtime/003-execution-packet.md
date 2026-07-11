# Task Execution Packet

## Task
003-spawn-behaviour-and-placement.md

## Goal
Add pluggable spawn behaviour and placement strategy contracts and first implementations.

## Files Allowed To Modify
- .agent/mob-spawn-rework/implementation-log.md

## Files Allowed To Create
- Assets/Scripts/Spawn/SpawnPoint.cs
- Assets/Scripts/Spawn/SpawnContext.cs
- Assets/Scripts/Spawn/ISpawnSink.cs
- Assets/Scripts/Spawn/SpawnBehaviour.cs
- Assets/Scripts/Spawn/ContinuousStreamBehaviour.cs
- Assets/Scripts/Spawn/SpawnPlacement.cs
- Assets/Scripts/Spawn/FixedPointPlacement.cs

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- .agent/mob-spawn-rework/implementation-context.md

## Behavior To Preserve
- No controller implementation yet.

## Behavior To Change
- Add separate SO axes for spawn timing and placement.

## Relevant Global Context
- SO assets hold authored data only; accumulator must live in SpawnBehaviourRuntime.
- SpawnController later implements ISpawnSink.

## Dependencies Confirmed
- Task 003 is independent.
- Assets/Scripts/Spawn exists from task 002.

## Step-By-Step Instructions
- Add SpawnPoint marker with jitter sampling and gizmos.
- Add SpawnContext readonly struct.
- Add ISpawnSink contract.
- Add SpawnBehaviour and runtime base.
- Add ContinuousStreamBehaviour with accumulator runtime and backlog clamp.
- Add SpawnPlacement and FixedPointPlacement.

## Acceptance Criteria
- Behaviour and placement are separate selectable SO assets.
- Continuous stream spawns at configured rate while under cap and clamps backlog at cap.
- Fixed point placement returns jittered point position or false if no points.
- No mutable state on SO assets.

## Validation Required
- Compile/build or explain blocked validation.

## Hard Boundaries
- Do not modify files outside allowed list except direct compile fixes.
- Do not combine with controller task.
