---
name: projectile-pipeline
description: Drop the on-hit field/branch from ProjectileHitComponent, ProjectileHitPayload, and both projectile collision systems
---

# 004 — Projectile Pipeline

## Goal
Remove the on-hit spawn field from the projectile ECS component and hit-payload wrapper, delete
the on-hit emission call from both projectile collision jobs (discrete and continuous), and drop
the now-unused spawn-event lane wiring those jobs only needed for on-hit emission.

## Dependencies
Task 003 complete (`SpawnTemplateRefEmit.AcquireProjectile`/`ReleaseProjectile` already shrunk).

## Files to Modify
- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Projectiles/ProjectileRuntimeEvents.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs` (`ProjectileLifetimeJob` — discovered
  mid-implementation; missed by initial research grep, caught as a compile-fix dependency of task
  003's `SpawnTemplateRefEmit.ReleaseProjectile` signature shrink)

## Step-by-Step

1. **`ProjectileEcsComponents.cs`**
   - `ProjectileHitComponent`: delete the `public OnHitSpawnRef OnHitSpawn;` field. Component keeps
     `PierceRemaining`, `RepeatHitCooldownSeconds`.

2. **`ProjectileRuntimeEvents.cs`**
   - `ProjectileHitPayload`: delete the `OnHitSpawn` property and the `onHitSpawn` constructor
     parameter. Keep the wrapper itself (do not collapse it into `CombatHitPayload` — see
     index.md's "Explicitly deferred" note; that is out of scope here).
   - Constructor becomes `ProjectileHitPayload(CombatHitPayload hitPayload)`.

3. **`ProjectileSpawnRequest.cs`**
   - `ProjectileSpawnRequest`'s long constructor: delete the `OnHitSpawnRef onHitSpawn = default`
     parameter, and update the `HitPayload = new ProjectileHitPayload(new CombatHitPayload {...}, onHitSpawn)`
     call to drop the second argument (matches task's new `ProjectileHitPayload` constructor).
   - `ProjectileChildSpawnConfig`'s constructor: delete the `OnHitSpawnRef onHitSpawn = default`
     parameter and the `OnHitSpawn { get; }` property if present (research found the param at
     ~line 182 — confirm whether it is actually consumed anywhere; if the property was never
     read outside `SkillDriver`'s removed `BuildOnHitSpawnRef` call sites, delete both).

4. **`ProjectileDiscreteCollisionSystem.cs`**
   - `ProjectileHitEmission`: delete the `EnqueueOnHitSpawn` method entirely, and its call site
     inside `ProjectileCollisionJob.Execute` (the `ProjectileHitEmission.EnqueueOnHitSpawn(...)`
     call right after `EnqueueHitEvent`).
   - `ProjectileCollisionJob`: delete the `ProjectileEventWriter`, `ImpactAoeEventWriter`,
     `LingeringAoeEventWriter`, `TargetedEventWriter` fields — they were only used by
     `EnqueueOnHitSpawn`. Keep `HitWriter` and `SpawnTemplateDeltas`.
   - `OnUpdate`: delete the `projectileLane`/`impactAoeLane`/`lingeringAoeLane`/`targetedLane`
     singleton `RefRW` acquisitions and their four `ProducerHandle = JobHandle.CombineDependencies(...)`
     lines after scheduling. Keep the `hitDispatch` lane and its combine.
   - `ProjectileHitEmission.Deactivate`: its call to `SpawnTemplateRefEmit.ReleaseProjectile` drops
     the `in projectileHit` argument per task 003's shrunk signature — update the call.

5. **`ProjectileContinuousCollisionSystem.cs`**
   - Mirror every change from step 4 (this system duplicates the same job shape: delete the
     `EnqueueOnHitSpawn` call, the four writer fields/lane acquisitions/combines, and fix the
     `ReleaseProjectile` call). `ProjectileHitEmission` is shared with the discrete system (defined
     once in `ProjectileDiscreteCollisionSystem.cs`) — do not duplicate it here.

6. **`ProjectileDiscreteSpawnApplySystem.cs`**
   - `ProjectileSpawnJob.Execute` (via `ProjectileSpawnApplyUtility.WriteCommon`): delete the
     `OnHitSpawn = cfg.HitPayload.OnHitSpawn` line when constructing `hits[index] = new ProjectileHitComponent {...}`.
   - `ProjectileSpawnApplyUtility.NeedsCollision(in ProjectileHitPayload payload)`: delete the
     `|| payload.OnHitSpawn.Enabled` term. Condition becomes
     `payload.DirectDamageEnabled || payload.StackEffect.Enabled`.
   - `SpawnTemplateRefEmit.AcquireProjectile(...)` call in `WriteCommon`: drop the `hits[index]`
     argument per task 003's shrunk signature.

## Behavior to Preserve
- Pierce, repeat-hit cooldown, direct damage, stack-effect hit dispatch — all unchanged.
- `TimedSpawnComponent`-based despawn refcounting — unchanged.

## Behavior to Change
- A projectile with `DirectDamageEnabled == false` and `StackEffect.Enabled == false` no longer
  gains collision-active state from a (never-populated) on-hit spawn flag. Since no live content
  sets `OnHitSpawn.Enabled` today, this is a no-op in practice.

## Acceptance Criteria
- No reference to `OnHitSpawn`/`OnHitSpawnRef`/`EnqueueOnHitSpawn` remains in any file listed above.
- Both collision systems compile with only `HitWriter` and `SpawnTemplateDeltas` as their
  queue-writer fields.

## Validation
- `grep -n "OnHitSpawn" Assets/Scripts/System/Projectiles/*.cs` returns nothing.
- Compile check deferred to the user.
