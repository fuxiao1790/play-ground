# Task Execution Packet

## Task
005-aoe-target-acquisition-hash-preservation.md

## Goal
Prove AOE collision, targeted acquisition, and projectile tracking remain on retained spatial hashes after discrete BVH migration. This is a preservation gate; runtime behavior must not change.

## Files Allowed To Modify
- None expected. If an acceptance criterion lacks regression coverage, stop and report exact missing coverage instead of broadening this preservation task.

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Api/Collision/CollisionConstants.cs`
- `Assets/Scripts/System/Targeted/TargetedAcquisition.cs`
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs`
- Named test classes from task acceptance criteria.

## Behavior To Preserve
- AOE occupied-cell traversal, duplicate suppression, 32-hit cap, faction/AABB/exact filters, cooldowns, payloads, VFX, lifecycle.
- Targeted occupied-cell selection, bounded nearest list, distance/snapshot-index order, exclusions, timing, chains, mana gate, acquisition flags, hit/VFX payloads, walk end.
- Tracking cell bands, snapshot-index validation, tracked-target ID refresh, acquisition behavior.

## Behavior To Change
- None.

## Relevant Global Context
- `TargetBroadphaseSingleton` owns mixed acceleration: discrete BVH plus retained continuous/AOE/tracking hashes.
- Owner type renames from task 002 are expected; they are not consumer migration.
- Only discrete projectile collision may query BVH.

## Dependencies Confirmed
- Task 002 retains `AoeOccupiedCells`, `TrackingCells`, and `TrackingIndicesById` built from shared snapshots.
- Task 003 migrates only discrete projectile collision.

## Step-By-Step Instructions
1. Read task and every listed runtime file relevant to acceptance criteria.
2. Trace impact and lingering AOE inputs to `AoeOccupiedCells` and `AoeCollisionCore`.
3. Verify duplicate suppression and `CollisionConstants.MaxAoeTargetsPerTick` remain.
4. Trace targeted snapshot/cell enumeration, nearest ranking/ties/exclusions, timing/mana/lifecycle.
5. Trace tracking refresh/acquisition through `TrackingIndicesById` and `TrackingCells`, including directional bands/index validation.
6. Search all AOE/targeted/tracking consumers for BVH types/workspaces/tree dependency; none may exist.
7. Confirm named test classes still contain relevant behavioral coverage.
8. Perform static diff/search review only. Do not run tests.

## Acceptance Criteria
- Impact/lingering AOE use retained occupied cells, not BVH.
- Duplicate suppression and `MaxAoeTargetsPerTick` remain.
- No all-hits/streaming/fixed-cap change.
- Targeted snapshot keeps occupied-cell view; no BVH traversal.
- Targeted ranking, exclusion, timing, mana, payload, and lifecycle unchanged.
- Tracking keeps tracking cells and ID map; no BVH dependency.
- Named test suites remain regression coverage.
- No AOE/targeted/tracking BVH consumer introduced.

## Validation Required
- Static searches and scoped diff review.
- No Unity tests or Unity test runner.
- No task-005 file changes expected.

## Hard Boundaries
- Do not modify runtime or tests.
- Do not migrate any consumer to BVH.
- Do not change caps, ordering, acquisition, or lifecycle.
- Stop on missing required coverage or architectural ambiguity.
