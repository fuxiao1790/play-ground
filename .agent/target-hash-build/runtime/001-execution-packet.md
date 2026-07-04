# Task Execution Packet

## Task
001-shared-spatial-hash-helper.md

## Goal
Add shared `CombatSpatialHash` cell math helper so producers and consumers use identical cell sizes, floor math, and FNV cell key math.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs` (optional substitution only)
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs` (optional substitution only)
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs` (optional substitution only)
- `.agent/target-hash-build/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Common/CombatSpatialHash.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`

## Behavior To Preserve
- Cell sizes: projectile collision 1, tracking 64, AOE 64.
- `floor(pos / cellSize)` for cell coordinate conversion.
- FNV-1a hash constants and `(uint)x`, `(uint)y` mixing order.

## Behavior To Change
- Introduce shared helper. No required runtime behavior change.

## Relevant Global Context
- Shared code belongs under `Assets/Scripts/System/Common`.
- Helper must be Burst-compatible: static, unmanaged math only, no managed state.
- Keep `TargetKey` and `TargetIdKey` helpers where they are for this task.

## Dependencies Confirmed
- Existing duplicate cell math found in `ProjectileTrackingSystem`, `ProjectileCollisionSystem`, and `AoeCollisionCore`.
- No prior task required.

## Step-By-Step Instructions
- Add `CombatSpatialHash` with constants `ProjectileCollisionCellSize`, `TrackingCellSize`, and `AoeCellSize`.
- Add `FloorCell(float2 pos, float cellSize)`, `MinCell(float2 min, float cellSize)`, `MaxCell(float2 max, float cellSize)`, and `CellKey(int x, int y)`.
- Preserve exact FNV constants and floor math.
- Optional substitutions in existing systems are allowed, but larger consumer refactors belong to later tasks.

## Acceptance Criteria
- Helper compiles under Burst.
- Constants and math match existing implementations.
- No behavior change.

## Validation Required
- Build/compile later in final validation.
- Search-check helper content and duplicate math status.

## Hard Boundaries
- Do not modify files outside allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
