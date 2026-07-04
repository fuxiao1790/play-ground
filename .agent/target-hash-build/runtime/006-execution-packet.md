# Task Execution Packet

## Task
006-validation.md

## Goal
Validate the completed refactor, update manual test worlds to include `TargetSpatialHashSystem`, and add focused AOE ordering/cap coverage if existing tests only check hit counts.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `.agent/target-hash-build/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- All changed runtime files from tasks 001-005.
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`

## Behavior To Preserve
- Existing PlayMode test scenarios and system ordering.
- Manual test worlds must now add `TargetSpatialHashSystem` before consumers that read `TargetSpatialHashSingleton`.

## Behavior To Change
- Add `TargetSpatialHashSystem` to manual simulation groups in tests that run tracking/collision consumers.
- Add targeted AOE hit cap/order coverage if needed.

## Relevant Global Context
- Producer runs before changed consumers through `[UpdateBefore]`, but manual test groups must include the producer system.
- Existing generated csproj may be stale and omit new Unity source files; prefer Unity Editor test/build validation over editing generated csproj.

## Dependencies Confirmed
- Tasks 001-005 are complete in code and implementation log.
- Changed consumers read `TargetSpatialHashSingleton`.

## Step-By-Step Instructions
- Add `TargetSpatialHashSystem` to test simulation groups in tracking, projectile collision, and AOE simulation tests before sorting.
- Add a focused AOE test that proves cap/order behavior by asserting the hit target IDs are the first inserted targets up to `CollisionConstants.MaxAoeTargetsPerTick`.
- Run search validation for removed local gather/hash paths and new producer/consumer wiring.
- Run Unity PlayMode tests for projectile tracking, projectile collision, AOE simulation/play mode if feasible, and combat pool cleanup.
- Update implementation log with validation result.

## Acceptance Criteria
- All tasks have acceptance criteria evaluated.
- Tests updated for new singleton producer.
- Validation commands run or inability explained.
- `implementation-log.md` updated.

## Hard Boundaries
- Do not change runtime architecture during validation unless compile errors directly caused by tasks require small fixes.
- Do not edit generated csproj unless explicitly needed and justified.
- Do not broaden test scope beyond targeted coverage.
