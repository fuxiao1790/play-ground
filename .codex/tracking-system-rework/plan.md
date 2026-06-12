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
- Start with static helper methods shared with collision-style cell hashing where practical, but do not merge tracking and collision systems.
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
