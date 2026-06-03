# Profiling Findings

## Captures

| File | Date | Scenario | Frame range |
|---|---|---|---|
| play-ground_2026-06-03_01-54-34.csv | 2026-06-03 | Mixed projectile attacks | 2.3–6.2ms (within 8.33ms budget) |
| play-ground_2026-06-03_02-08-36.csv | 2026-06-03 | Stacking debuff explosion only (AOE stress) | 20–106ms (4–13x over budget) |

---

## Capture 1 — Mixed projectile attacks (within budget)

### Hot spots

**`ProjectileContactGateJob` — main-thread wait in `SubmitProjectiles`**
- Blocked inside `ProjectileRoot.SubmitProjectiles/JobHandle.Complete/WaitForJobGroupID` every frame
- F1: 0.23ms, F2: 0.25ms, F3: 0.44ms — up to 5.7% of frame
- Job is fully offloaded (0ms main-thread self), but schedules too late relative to `LateUpdate` sync point
- At 50k-projectile target this will be the first Burst scaling wall
- Fix: audit dependency chain between `ProjectileContactGateSystem` and `SubmitProjectiles`; gate job should not need to block the full downstream `Dependency` before Complete

**`ProjectileTrackingJob` blocks `ProjectileCollisionSystem` main thread**
- F0: 0.70ms of main-thread wait inside `Despawn.Collision.FrameTime/JobHandle.Complete`
- Collision system syncs tracking before it needs its output (likely `Dependency.Complete()` called too early in `OnUpdate`)
- Fix: move sync in `ProjectileCollisionSystem.OnUpdate` to just before the job actually reads tracking output

**`ProjectileSpawnSystem` cold-spawn GC: 64 KB (frame 4 only)**
- `Projectile.Spawn.FrameTime / GC.Alloc` — 1 allocation, pool exhaustion
- ECB playback creating new archetype entity backing arrays
- Fix: pre-warm pool at startup to cover peak count

**`MobRoot.FixedUpdate` steady GC: ~2.2 KB per FixedUpdate, 40 allocs, 21 mob calls**
- Managed allocation inside per-mob loop — likely collection resize or LINQ
- Fix: find and pre-size or replace the allocating structure

**`SubmitProjectiles` self-time: 2–5% (~0.14–0.34ms)**
- `ToComponentDataArray<CombatRenderElement>` allocates managed array every frame
- Acceptable now; will not scale to 50k projectiles
- Fix: switch to persistent `NativeArray` passed to non-allocating `ToComponentDataArray` overload

### What is working

- Zero GC from all Burst jobs (collision, tracking, movement, lifetime, child-spawn, render-prepare)
- `CombatRenderPrepareJob` 0.02–0.04ms — appropriately cheap
- `ProjectileContactGateSystem` main-thread cost 0ms — work fully on workers
- No Transform access in simulation hot paths

---

## Capture 2 — Stacking debuff explosion (AOE stress, over budget)

### Attack flow context

Stack-debuff explosion: projectile hits mob → managed callback adds stack → threshold → AOE spawned at mob position → AOE pulse hits all targets → `AoeRoot.DrainEvents` replays.

Projectiles are still firing (to apply stacks). Explosion volume scales with: mob count × stack threshold rate.

### Hot spots

**`ReplayProjectileHitEvents` — 10–16ms self, 20–32% of frame, every frame**
- Stack application goes through `ReplayProjectileHitEvents` for every projectile-to-mob hit
- O(hits × listeners) on main thread; no frame budget cap
- GC inside: 3–12 KB per invocation (per-replay allocation — likely hit context object)
- F0: 10.94ms, F1: 15.89ms, F5: 13.57ms
- Fix: cap drain to per-frame hit budget; carry overflow to next frame. Eliminate per-hit GC with reused struct hit context passed by ref.

**`AoeSpawnSystem / Aoe.Spawn.FrameTime` — 8–10ms self, 15–21% of frame, every frame**
- 8–10ms of main-thread managed work in `OnUpdate` (not job wait)
- Many simultaneous stack threshold triggers → many AOE spawn requests queued per frame
- Managed iteration over AOE entities/buffers instead of Burst job
- Also holds unnecessary dependency on `ProjectileCollisionJob` (3.67ms wait — AOE spawn does not need projectile collision output)
- Fix: move active-AOE state updates and spawn/recycle logic into `IJobChunk` (Burst). Decouple from projectile collision `Dependency` — AOE spawn should only depend on AOE-scope buffer writers.

**`AoeRoot.DrainEvents` + `Physics2D.Collider2D.DestroyShapes` (frame 5)**
- `DrainEvents` calls `Physics2D.Collider2D.DestroyShapes` (4 calls) and `TextureStreamingManager.RemoveRenderer` (2 calls)
- AOE cleanup is destroying live `Collider2D` and renderer objects — one-live-trigger-per-AOE path still active on AOE despawn
- Baked shape path exists (`CombatTargetShapeUtility`, `AoeConfig.sizeMultiplier`); prefab collider should be used only during registration baking, then disabled so drain does not destroy physics shapes
- Fix: remove collider/renderer destruction from `DrainEvents`; pool visual objects (SetActive false), do not Destroy

**`MobRoot.FixedUpdate` — 2–3ms, 80–120 KB GC, 351 calls, 120 allocs per FixedUpdate**
- Linear scale from capture 1 (21 mobs, 40 allocs, 2.3 KB) → now 351 mobs
- Feeds late-frame GC pauses; frame 57 GC spike to 0.7 MB is this + replay + DrainEvents
- Fix: same as capture 1 — find and eliminate per-mob allocation

**`ProjectileCollisionJob` — 3.67ms (was 0.07–0.17ms in capture 1)**
- Scales with projectile count; within the AOE stress scenario projectiles still fire for stack application
- `AoeSpawnSystem` unnecessarily waits on this job before running
- `ProjectileTrackingJob → CollisionSystem` sync still present (2.5ms, frame 0)

**`SubmitProjectiles` self — 2.1ms (was 0.14ms in capture 1)**
- `ToComponentDataArray` managed allocation now 2ms at stress-test scale
- Fix: same as capture 1 — persistent NativeArray reuse

### GC attribution (frame 57 peak: 0.7 MB)

| Source | Cost |
|---|---|
| `MobRoot.FixedUpdate` | 80–120 KB / frame |
| `ReplayProjectileHitEvents` | 3–12 KB / invocation |
| `AoeRoot.DrainEvents` | 0–12 KB (despawn frames) |
| `ProjectileSpawnSystem` ECB | 64 KB burst (pool exhaustion) |

---

## Priority order (combined)

| # | Item | Impact | Capture |
|---|---|---|---|
| 1 | `AoeSpawnSystem` managed → Burst | −8–10ms/frame | C2 |
| 2 | `ReplayProjectileHitEvents` frame budget cap + per-hit GC elimination | −10–16ms/frame | C2 |
| 3 | Remove `Collider2D` / renderer destruction from `AoeRoot.DrainEvents` | removes Physics2D cost + unblocks AOE pooling | C2 |
| 4 | Decouple `AoeSpawnSystem` from `ProjectileCollisionJob` dependency | −3–4ms AOE spawn wait | C2 |
| 5 | `MobRoot.FixedUpdate` allocation (per-mob managed alloc) | −80–120 KB GC/frame | C1+C2 |
| 6 | `ProjectileContactGateJob` dependency latency in `SubmitProjectiles` | −0.2–0.44ms | C1 |
| 7 | `ProjectileTrackingJob → CollisionSystem` sync point | −0.7–2.5ms | C1+C2 |
| 8 | `SubmitProjectiles` persistent NativeArray (ToComponentDataArray) | −0.14ms→2ms | C1+C2 |
| 9 | Pool pre-warm (ProjectileSpawnSystem ECB cold-spawn) | eliminates 64 KB spike | C1 |
