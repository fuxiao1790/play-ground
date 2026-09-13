# Task Execution Packet

## Task
`004-movement-system-emission.md`

## Goal
Emit one optional line-segment VFX event while movement integrates each active, non-arming projectile.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`

## Behavior To Preserve
- Shared movement/collision-bounds math for both projectile lanes.
- Existing schedule dependency chain.

## Behavior To Change
- Fetch fail-loud VFX dispatch singleton; pass line queue writer into parallel job; combine producer handle.
- Capture position before integration and emit through `VfxEmit.EnqueueLineSegment` after bounds update.

## Relevant Global Context
- `VfxEmit` no-ops on id `0` or non-`LineSegment` shape.
- `ProjectileTrailVfxComponent` is intentionally a required movement-query term and exists on both production archetypes.

## Dependencies Confirmed
- Task 003 component exists and both spawn archetypes include it.

## Acceptance Criteria
- VFX event spans pre/post integration position, once per matching entity/tick.
- VFX producer handle includes movement job; untrailed projectiles enqueue nothing.

## Validation Required
- Static source inspection only. Unity tests deferred to user.

## Hard Boundaries
- No new system, no use of `ProjectileContinuousStepComponent`, no `TryGet` singleton guard.
