# Task Execution Packet

## Task

006-swept-movement-system.md

## Goal

Move swept projectiles once per frame while recording origin before integration.

## Files Allowed To Modify

- `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`

## Files Allowed To Create

- `Assets/Scripts/System/Projectiles/SweptProjectileMovementSystem.cs`

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Discrete movement system; tracking/contact gate ordering; collision components/helper.

## Behavior To Preserve

- Discrete lane movement/bounds semantics and shared system ordering.

## Behavior To Change

- Discrete job excludes swept archetype; swept job captures origin then does identical integration/current-position bounds rebuild.

## Relevant Global Context

- Bounds stay current-position bounds; collision derives sweep box transiently.
- Swept archetype has no tracking component, so tracking self-excludes. Do not alter tracking system.
- Arming excluded. Burst/job scheduling matches discrete lane.

## Dependencies Confirmed

- Swept tag/component and swept apply archetype exist.

## Step-By-Step Instructions

1. Add `WithNone<ProjectileContinuousTag>` to discrete movement job.
2. Create swept movement system with exact listed group/order/Burst/arming/domain tags.
3. Write origin before `Position += Velocity * DeltaTime`, rebuild normal bounds via existing helper.

## Acceptance Criteria

- No double movement.
- Origin order correct; bounds helper/args identical.
- Both lanes Burst/ScheduleParallel.

## Validation Required

- Static query/order/body checks plus diff check; compile baseline may remain blocked.

## Hard Boundaries

- No collision implementation or tracking system changes.
