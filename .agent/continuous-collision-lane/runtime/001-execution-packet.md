# Task Execution Packet

## Task

001-continuous-types.md

## Goal

Declare swept-archetype discriminator, per-projectile sweep origin, and fixed per-frame swept-hit cap.

## Files Allowed To Modify

- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Api/Collision/CollisionConstants.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `ProjectileEcsComponents.cs`
- `CollisionConstants.cs`

## Behavior To Preserve

- All current projectile behavior. No new types are referenced yet.

## Behavior To Change

- Expose types/constants for later swept lane work only.

## Relevant Global Context

- Swept membership is a distinct archetype, not an enableable overlay; tag must be plain `IComponentData`.
- Swept archetype will never carry `ProjectileTrackingComponent`.
- Full sweep coverage means no range/cap configuration; performance cap limits retained hits, not path length.
- Lifecycle comments must use `ECS Lifecycle:`.

## Dependencies Confirmed

- None required.
- `ProjectileTag`, `ProjectileTrackingComponent`, and `CollisionConstants` exist at expected locations.

## Step-By-Step Instructions

1. Add `ProjectileContinuousTag : IComponentData` with prescribed lifecycle/exclusivity comment.
2. Add `ProjectileContinuousStepComponent : IComponentData` containing `float2 Origin` and prescribed lifecycle/geometry comments.
3. Add `CollisionConstants.MaxContinuousHitsPerFrame = 16` with safety-bound comment.
4. Do not add a sweep config or clamp.

## Acceptance Criteria

- Both components have lifecycle comments.
- Tag is not enableable.
- No config singleton/distance clamp.
- Compile and retain unchanged behavior.

## Validation Required

- Static verification and project compile check if available.

## Hard Boundaries

- Modify only listed files.
- No architecture changes, later-task implementation, config singleton, or runtime behavior.
