# 002 — Skip target-hash build when no projectiles are active

## Problem

`ProjectileTrackingSystem.OnUpdate`
(`Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs:32-72`) runs the
full setup **before** checking whether any projectile is active:

- `CompleteDependencyBeforeRO<TargetPosition>()` and
  `CompleteDependencyBeforeRO<TargetFaction>()` — two main-thread sync points.
- `ToEntityArray` + 2× `ToComponentDataArray` (`TempJob`) over the entire target
  set.
- Builds a `NativeParallelHashMap` and `NativeParallelMultiHashMap` from all
  targets.
- Schedules the acquisition + steering jobs.

When no projectiles are active (post-clear idle), all of this is wasted: the
target spatial hash exists only to serve projectile tracking, and the sync
points stall the main thread for nothing.

## Change

1. Cache an active-projectile query in `OnCreate`:
   `WithAll<ProjectileTag, Active>()`. (Tracking only matters for projectiles
   with `TrackingEnabled`, but `Active` is the cheap, sufficient gate; the
   per-entity `TrackingEnabled` check already short-circuits inside the job.)
2. At the top of `OnUpdate`, early-return when
   `activeProjectileQuery.CalculateEntityCount() == 0` (or
   `state.RequireForUpdate(activeProjectileQuery)`), **before** the
   `CompleteDependencyBeforeRO` calls and any allocation.

Prefer `RequireForUpdate` so the system is skipped entirely (also avoids the
empty-target hash build).

## Notes / caveats

- This mirrors the guard already present in `ProjectileCollisionSystem`
  (`:47`). Keep behavior identical when projectiles are active.
- Do not remove the target-hash build for the active case; tracking still needs
  it.

## Acceptance criteria

- With no active projectiles, `ProjectileTrackingSystem` performs no sync
  points, no allocations, and schedules no jobs.
- Homing/tracking behaves identically when projectiles are active.

## Dependencies

None. Independent.

## Scope

Small — localized to `ProjectileTrackingSystem.cs`.
