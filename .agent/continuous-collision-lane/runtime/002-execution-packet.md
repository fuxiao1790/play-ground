# Task Execution Packet

## Task

002-corridor-geometry.md

## Goal

Add pure Burst-compatible geometry helpers to construct swept oriented rectangle and closest-approach ordering key.

## Files Allowed To Modify

- `Assets/Scripts/System/Api/Collision/Narrowphase/CombatCollisionMath.cs` (only make capsule helper `internal` when needed by new helper)

## Files Allowed To Create

- `Assets/Scripts/System/Api/Collision/Narrowphase/CombatSweepMath.cs`

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `CombatCollisionMath.cs`
- `CombatShapeType.cs`
- `CombatCollisionComponents.cs`

## Behavior To Preserve

- Existing collision narrowphase and capsule-axis convention.

## Behavior To Change

- Provide geometry construction only. No overlap implementation, ECS system, or cached state.

## Relevant Global Context

- Geometry must be allocation-free and safe inside Burst jobs.
- Recompute support extents each tick from live shape fields.
- Rectangle output must feed existing `CombatCollisionMath.Hit` as `CombatShapeType.Rectangle` without coordinate adapters.
- No distance cap, config, or caching.

## Dependencies Confirmed

- `ProjectileContinuousTag`, `ProjectileContinuousStepComponent`, and `MaxContinuousHitsPerFrame` exist from task 001.
- `CombatCollisionMath` handles rectangle/circle/capsule shapes; capsule axis is `Rotate((0, 1), rotationRadians)` and half-segment is `halfExtents.x`.

## Step-By-Step Instructions

1. Create public static `CombatSweepMath` in collision narrowphase namespace.
2. Implement `SupportExtent` for circle, rectangle, and capsule using axis projections and collision math's capsule convention.
3. Implement `BuildSweptBox`: full segment, midpoint, travel-axis rotation, longitudinal support + segment half-length, perpendicular support.
4. On segment length squared `<= Epsilon`, return original center/half-extents/rotation exactly.
5. Implement zero-safe clamped `ClosestApproachParam`.
6. Expose existing capsule-segment helper as `internal` instead of copying convention, if needed.

## Acceptance Criteria

- No allocations/managed types/new overlap code.
- Live support computation.
- Capsule convention shared, not duplicated.
- Rectangle output directly matches collision math convention.
- Degenerate exactly reproduces discrete shape.

## Validation Required

- Static check/diff check; compile check if baseline permits. Tests come in task 008.

## Hard Boundaries

- Do not modify unrelated collision behavior or implement tests/later systems.
