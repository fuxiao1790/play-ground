# Spatial Hash Projectile Tracking

**Summary**
- Use a per-frame target spatial hash so tracking reacquire no longer scans every target for every projectile.
- Keep existing target lock when usable; use hash only when target missing, invalid, or likely unreachable.

**Profiling Observations**
- `ProjectileTargetAcquisitionJob` is the dominant tracking cost in the latest profile:
  `4.41 ms` current frame time and `34.33 ms` accumulated over `8` worker
  instances.
- `ProjectileSteeringJob` is much smaller: `0.533 ms` current frame time and
  `3.74 ms` accumulated over `8` worker instances.
- Acquisition is about `8.27x` the steering wall time in that capture, so
  steering math is not the first optimization target.
- The current acquisition path still does expensive target lookup work at
  projectile scale. The likely hot costs are cached target refresh misses that
  fall back to target-buffer scans, plus initial/reacquire frames where many
  projectiles scan the same scope target buffer.
- First optimization should reduce lookup complexity before changing steering:
  make current-target refresh O(1) by target id, then bound reacquire candidate
  count with a scope-aware spatial index.
- The optimization must be measured against `ProjectileTargetAcquisitionJob`
  after implementation; `ProjectileSteeringJob` should stay separately visible
  so regressions are easy to spot.

**Key Changes**
- In `ProjectileTrackingSystem`, build temporary scope-aware target indexes each update:
  - `target id -> target buffer index` for cheap current-target refresh.
  - `cell key -> target indices` spatial hash for bounded reacquire.
- Refresh current target by id map, not full target scan.
- Reacquire by checking nearby cells around projectile position within tracking range, then choose first/best viable target by turn angle, not nearest distance.
- Add viability rule: target must match mask, be in range, and be reachable enough for current velocity plus turn speed so projectile does not spin around an impossible target.
- Keep `CombatTargetElement` and `ICombatTarget` unchanged.

**Implementation Notes**
- Reuse collision-style hash math where practical, but do not reuse the
  `ProjectileCollisionSystem` map instance directly. Collision builds and
  disposes its target cell map inside the later collision system, after tracking
  and movement have already run.
- Extract/share small static helper methods for `FloorCell` and scope-aware
  `CellKey` if duplication grows, but keep tracking and collision maps separate.
- Do not use the collision cell size for tracking. Collision currently uses a
  fine `1f` cell size because projectile hit queries are projectile-AABB sized.
  Tracking reacquire queries are homing-range sized, often `150-200` units, so
  `1f` cells would cause tens of thousands of mostly empty hash probes per
  reacquire.
- Use a much coarser tracking cell size, likely `32f`, `64f`, or a value derived
  from tracking range / target density. For example, range `200` with `1f` cells
  can touch about `160,000` cells, while `64f` cells touches about `49` cells.
- Use `NativeParallelHashMap` / `NativeParallelMultiHashMap` with `Allocator.TempJob`, disposed after scheduled tracking job.
- Keep hash keys scope-aware so player-to-mob and mob-to-player roots stay isolated.
- Preserve tracking cooldown and initial query delay behavior.

**Test Plan**
- Existing tracking acquire test still passes.
- Add test: projectile keeps same reachable target when another target is closer.
- Add test: projectile swaps target when current target cannot be turned toward fast enough.
- Add high target-count smoke/probe to confirm reacquire path is bounded by nearby cell candidates, not full target buffer length.

**Assumptions**
- Stable hittable target is preferred over nearest target.
- If no viable target is found in nearby cells, projectile keeps current flight and retries after cooldown.
