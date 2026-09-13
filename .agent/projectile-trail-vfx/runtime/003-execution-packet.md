# Task Execution Packet

## Task
`003-ecs-trail-component-and-archetypes.md`

## Goal
Materialize resolved projectile trail id/width into both projectile ECS lanes.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`

## Behavior To Preserve
- Shared `WriteCommon` remains source for values written to both spawn lanes.
- Pool reuse rewrites all common components each spawn.

## Behavior To Change
- Add `ProjectileTrailVfxComponent` to both archetypes and job handles.
- Pass its chunk array to `WriteCommon`, which writes command trail id/width.

## Dependencies Confirmed
- `ProjectileSpawnCommand.TrailVfxId` and `.TrailWidth` exist.

## Acceptance Criteria
- Both spawn archetypes have trail component; both callers pass it to `WriteCommon`.
- `WriteCommon` overwrites its two values from command on every spawn.

## Validation Required
- Static source inspection only. Unity tests deferred to user.

## Hard Boundaries
- Do not alter lane-specific simulation behavior or add a system.
