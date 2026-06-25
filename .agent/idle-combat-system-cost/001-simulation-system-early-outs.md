# 001 — Early-out guards for lifetime / pulse / movement systems

## Problem

Three simulation systems allocate `TempJob` containers and/or schedule jobs
every frame regardless of active count:

- `CombatLifetimeSystem.OnUpdate`
  (`Assets/Scripts/System/Common/CombatLifetimeSystem.cs:31-62`) — allocates a
  `NativeQueue<VfxPendingSpawn>` and schedules 3 chained jobs every frame.
- `AoePulseVfxSystem.OnUpdate`
  (`Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs:23-41`) — allocates a
  `NativeQueue<VfxPendingSpawn>` and schedules 2 jobs every frame.
- `ProjectileMovementSystem.OnUpdate`
  (`Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs:15-23`) —
  schedules a parallel job every frame.

The job bodies filter on `Active`, so they do little per-entity work when idle,
but the allocation + schedule + dispose + (for lifetime/pulse) the VFX flush
chain runs unconditionally.

## Change

Add active-entity guards using the existing pattern in the codebase
(`state.RequireForUpdate(activeQuery)` or `CalculateEntityCount() == 0`
early-return):

- **`ProjectileMovementSystem`**: cache an `EntityQuery` for
  `WithAll<ProjectileTag, Active>()` in `OnCreate` and
  `state.RequireForUpdate(query)`. No active projectiles → system skipped.
- **`CombatLifetimeSystem`**: it processes both projectiles and AOEs, so a
  single `RequireForUpdate` is insufficient. Use
  `RequireAnyForUpdate` of the active-projectile query and the active-AOE query,
  or early-return when both `CalculateEntityCount() == 0`. Only allocate the
  `NativeQueue` and schedule the jobs that have matching active entities (skip
  the projectile job if no active projectiles, skip the AOE job if no active
  AOEs; skip the VFX flush if nothing was scheduled).
- **`AoePulseVfxSystem`**: cache a `WithAll<AoeTag, Active>()` query and
  `state.RequireForUpdate(query)`.

## Notes / caveats

- `CombatLifetimeSystem` chains `aoeJob` after `projectileJob` because both
  write the same `NativeQueue` parallel writer. If only one job runs, keep the
  chain correct (the running job feeds the VFX flush directly).
- Optional secondary improvement: reuse a persistent `NativeQueue` cleared each
  frame instead of per-frame `TempJob` alloc in lifetime/pulse. Minor; can be a
  follow-up.
- Do not change job bodies or VFX semantics.

## Acceptance criteria

- With no active projectiles/AOEs, these three systems schedule no jobs and
  allocate nothing.
- With active entities, lifetime expiry, AOE pulse VFX, and projectile movement
  behave exactly as before.

## Dependencies

None. Independent of 002/003.

## Scope

Small — three localized edits.
