---
name: spawning-core-types
description: Delete OnHitSpawnRef and shrink SpawnTemplateRefEmit/SpawnTemplateValidation to drop the on-hit lane
---

# 003 — Spawning Core Types

## Goal
Delete the `OnHitSpawnRef` struct and remove the on-hit terms from the generic spawn-template
refcount emitter and validator that every domain (Projectile/AOE/Targeted) shares.

## Dependencies
Task 002 complete (no more producers of `OnHitSpawnRef` values from the Skills layer).

## Files to Modify
- `Assets/Scripts/System/Spawning/IntervalChildTemplates.cs`
- `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`

## Step-by-Step

1. **`IntervalChildTemplates.cs`**
   - Delete the `OnHitSpawnRef` struct (`Kind`, `TemplateKey`, `Enabled`). Keep `IntervalChildKind`
     — it is shared vocabulary also used by `TimedSpawnComponent`.

2. **`SpawnTemplateComponents.cs` — `SpawnTemplateRefEmit`**
   - `EmitProjectile`: delete the `Enqueue(hit.OnHitSpawn.Kind, hit.OnHitSpawn.TemplateKey, delta, deltas);`
     line. The `hit` parameter (`in ProjectileHitComponent hit`) becomes unused once this line is
     gone — remove the parameter from `EmitProjectile`'s signature.
   - `AcquireProjectile`/`ReleaseProjectile`: drop the `in ProjectileHitComponent hit` parameter
     they forward to `EmitProjectile`; update their signatures accordingly.
   - `EmitAoe`: delete the `Enqueue(hit.OnHitSpawn.Kind, hit.OnHitSpawn.TemplateKey, delta, deltas);`
     line; drop the now-unused `in AoeHitSpawnComponent hit` parameter.
   - `AcquireAoe`/`ReleaseAoe`: drop the `in AoeHitSpawnComponent hit` parameter they forward to
     `EmitAoe`.
   - Leave `AcquireTargeted`/`ReleaseTargeted` untouched — they never took a hit-spawn parameter
     (targeted entities carry no on-hit ref; see index.md grounding).

3. **`SpawnTemplateComponents.cs` — `SpawnTemplateValidation`**
   - Delete the `EnsureValidChildKind(OnHitSpawnRef onHitSpawn)` overload entirely.
   - Keep `EnsureValidChildKind(TimedSpawnComponent timedSpawn)` and the private
     `EnsureValidChildKind(IntervalChildKind kind)`.

## Behavior to Preserve
- `TimedSpawnComponent`/interval refcounting and validation — untouched.
- `StackEffect.DetonationKey` refcounting in `EmitProjectile`/`EmitAoe` — untouched.

## Behavior to Change
- `AcquireProjectile`/`ReleaseProjectile`/`AcquireAoe`/`ReleaseAoe` have new (shorter) signatures.
  Every call site is updated in tasks 004 and 005, which touch the same files that call them.

## Acceptance Criteria
- `OnHitSpawnRef` no longer exists anywhere in `Assets/Scripts/System/Spawning/`.
- `SpawnTemplateRefEmit`'s four Acquire/Release wrappers compile with the shrunk parameter list
  (their call sites are fixed in tasks 004/005 — expect this file alone to not yet compile against
  stale callers until those land; that is expected mid-plan state, not a defect in this task).

## Validation
- `grep -n "OnHitSpawnRef" Assets/Scripts/System/Spawning/IntervalChildTemplates.cs Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs` returns nothing.
- Full-repo compile check deferred to the user until tasks 004–006 also land (this task alone
  intentionally leaves callers in Projectiles/Aoes stale — do not attempt to make this task
  independently compiling by improvising in caller files; that is tasks 004/005's job).
