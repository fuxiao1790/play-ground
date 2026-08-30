# Task Execution Packet

## Task
004-continuous-hash-preservation.md

## Goal
Prove continuous projectile collision remains on retained spatial hash after discrete BVH migration. Preserve algorithm exactly. Add only missing regression coverage needed by task acceptance criteria.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs` — add a focused corridor-edge regression only if existing coverage does not satisfy acceptance criteria.

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetBroadphaseSystem.cs`
- `Assets/Scripts/System/Api/Collision/Broadphase/CombatSpatialHash.cs`

## Behavior To Preserve
- Endpoint plus travel-corridor union bounds.
- `MaxTargetRadius` expansion and spatial-hash cell range/enumeration.
- Faction, contact gate, AABB, endpoint/corridor exact tests.
- Bounded candidate list, TOI/index sorting, impact positions, emissions, pierce, and deactivation.

## Behavior To Change
- Runtime: none.
- Tests: add only missing near-corridor-edge spatial-hash regression, using existing harness.

## Relevant Global Context
- BVH is discrete-only. Continuous long sweeps intentionally remain spatial-hash queries.
- Owner rename from task 002 is expected and is not a task-004 algorithm change.
- Do not add BVH types, traversal, query circles, or a second broadphase path.

## Dependencies Confirmed
- Task 002 owner exposes retained `ProjectileCollisionCells`, `MaxTargetRadius`, snapshots, and handle protocol.
- Task 003 discrete consumer alone now queries `DiscreteBvh`.

## Step-By-Step Instructions
1. Read task and both continuous source/test files in full.
2. Verify source still reads `ProjectileCollisionCells` and `MaxTargetRadius` and uses `CombatSpatialHash.FloorCell`/`CellKey`.
3. Verify no `BvhTree`, `BvhTraversalWorkspace`, or BVH query-circle logic exists in continuous source.
4. Verify union bounds, expansion, cell traversal, exact tests, cap/sort, impact, gate, emission, and lifecycle code are unchanged except task-002 owner type rename.
5. Inspect existing tests. If no target-near-corridor-edge case exists, add one test method using existing helpers; do not alter runtime source or restructure harness.
6. Perform static diff/search validation. Do not run Unity tests.

## Acceptance Criteria
- Continuous collision reads retained hash cells and `MaxTargetRadius`.
- No BVH query or BVH consumer path exists in continuous job.
- Endpoint/corridor bounds and cell calculation remain unchanged.
- Hit cap, TOI/index order, impact position, gates, spawns, and deactivation remain unchanged.
- PlayMode tests cover long travel corridor and target near corridor edge.
- Task 003 introduced no continuous algorithm rewrite.

## Validation Required
- Static search/diff review.
- Scoped diff contains only allowed test file if test addition is required; otherwise no task-004 file changes.
- Do not run Unity tests or Unity test runner.

## Hard Boundaries
- Do not modify `ProjectileContinuousCollisionSystem.cs`.
- Do not modify discrete, AOE, targeted, tracking, or broadphase runtime files.
- Do not add BVH-specific expectations to continuous behavior.
- Stop on architectural ambiguity.
