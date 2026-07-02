# Task Execution Packet

## Task
005-tests-migration.md

## Goal
Migrate tests off shared-component batch id APIs and add coverage proving reused slots receive the new render batch id.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `.agent/rendering-rework/implementation-log.md`

## Files Allowed To Create
- `.agent/rendering-rework/runtime/005-execution-packet.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/rendering-rework/implementation-context.md`
- `.agent/rendering-rework/005-tests-migration.md`
- Relevant PlayMode test files.

## Behavior To Preserve
- Existing PlayMode test intent and fixtures remain.
- Existing spawn/reuse tests continue to assert entity reuse.

## Behavior To Change
- Tests construct `CombatRenderBatchId` as normal component data.
- Tests count render batch ids manually instead of using shared filters.
- New assertions cover stale batch-id prevention on reused projectile basic, projectile child-spawner, and AOE slots.

## Relevant Global Context
- `CombatRenderBatchId` is no longer shared data.
- Every reused slot must write `CombatRenderBatchId.Value = RenderTypeId`.

## Dependencies Confirmed
- Tasks 002 and 003 write batch id on projectile/AOE reuse and cold paths.
- Task 004 render system no longer uses shared filters.

## Step-By-Step Instructions
- Replace `AddSharedComponent` in projectile test helpers with normal component creation/set.
- Replace `SetSharedComponentFilter` usage in render count helper with manual `CombatRenderBatchId.Value` filtering.
- Add missing `CombatRenderBatchId` to manual renderable archetypes where needed.
- Add stale-batch-id reuse assertions for basic projectile, child-spawner projectile, and AOE.

## Acceptance Criteria
- No test uses `AddSharedComponent` or `SetSharedComponentFilter` for `CombatRenderBatchId`.
- Stale batch-id reuse coverage exists.

## Validation Required
- Search PlayMode tests for old shared APIs.
- Run relevant PlayMode tests if possible after docs task.

## Hard Boundaries
- Do not modify unrelated tests.
- Do not add production-only hooks for tests.
- Do not change architecture.
