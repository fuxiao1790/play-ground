# Task Execution Packet

## Task
003-docs-and-validation.md

## Goal
Update design docs and run focused validation.

## Files Allowed To Modify
- `Docs/flows/spawn-event-to-entity.md`
- `Docs/profiling.md`
- `Docs/reference/simulation/project-ecs-implementation.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/aoe-system.md`
- `Docs/reference/simulation/project-aoe-system-common.md`
- `Docs/todo.md`
- Task context/log files under `.agent/spawn-apply-parallel-reuse/`

## Files Allowed To Create
- None beyond task orchestration files.

## Files Allowed To Delete
- None for this task.

## Files Likely Needed For Reading
- `Docs/layers/ecs-simulation.md`
- `Docs/contracts/spawn-events-and-commands.md`
- `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`

## Behavior To Preserve
- Docs must describe existing code, not invent a new runtime path.

## Behavior To Change
- Spawn flow docs describe command lists and single-threaded Burst reuse.
- Profiling docs no longer frame worker-lane imbalance as expected.

## Relevant Global Context
- Data flow is event -> ordered command list -> single-cursor reuse -> cold suffix.
- Cold fallback means true pool shortage for that archetype.

## Dependencies Confirmed
- Task 001 evidence: `NativeList` command containers exist in expansion systems.
- Task 002 evidence: apply systems use one Burst `IJob` per pool and old helper is deleted.

## Step-By-Step Instructions
- Align docs with command-list and single-thread reuse path.
- Run focused build/tests where available.
- Capture exact validation failures.

## Acceptance Criteria
- Spawn flow docs describe command lists and single-threaded Burst reuse.
- Profiling docs no longer describe worker-lane imbalance as expected.
- Build or focused Unity tests run, or failure is captured with exact cause.

## Validation Required
- `dotnet build .\PlayGround.Runtime.csproj`
- Optional test builds or Unity PlayMode focused run when available.

## Hard Boundaries
- Do not modify files outside the allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
