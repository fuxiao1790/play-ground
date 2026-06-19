# Task 03 — Projectile collision semantics alignment

**Depends on:** 01. **Not required for the malloc fix** (projectile has no
broadphase container; its per-frame malloc is the writers, out of scope). This
task makes the pierce cap rule explicit.

## Stated rules

- A projectile **still hits at `PierceRemaining == 0`**.
- It despawns once `PierceRemaining` drops to `-1` (hit → decrement → despawn when
  `< 0`).
- Authored `PierceCount = N` therefore lands `N + 1` hits.
- A projectile overlapping multiple targets emits a hit per target while pierce
  allows; all subject to the contact gate.

## Current state

`ProjectileCollisionJob.Execute` iterates inline and, after a hit, despawns when
`PierceRemaining <= 0` then decrements ([lines 305-312](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L305-L312)).
That despawns one hit too early (at 0 instead of -1) and never early-outs on an
already-exhausted projectile.

## Changes

### 1. Early-out guard

After the existing `lifetime.Remaining <= 0f` deactivation check (~line 194) and
before the `TotalTargetCount == 0` check, add:

```csharp
// Pierce is the projectile's hit cap. It may still hit at 0; below zero is exhausted.
if (projectileHit.PierceRemaining < 0)
{
    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
    EndVfxStream(ref vfxPending);
    return;
}
```

### 2. Post-hit decrement + despawn at -1

Replace the post-hit block ([lines 302-312](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L302-L312)) with:

```csharp
AddOrRefreshGate(contactGates, targetKey, projectileHit.RepeatHitCooldownSeconds);

projectileHit.PierceRemaining--;
if (projectileHit.PierceRemaining < 0)
{
    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
    EndVfxStream(ref vfxPending);
    return;
}
```

Net effect: `PierceRemaining = 0` → emits one hit, decrements to `-1`, despawns.
`PierceRemaining = N` → `N + 1` hits, then despawn.

## Gate buffer

No change beyond Task 01's explicit `[InternalBufferCapacity(16)]`. A projectile
piercing more than 16 distinct targets remains a rare one-time growth.

## Acceptance

- A projectile with `PierceRemaining == 0` emits exactly **1** gated hit, then
  despawns.
- `PierceRemaining == N` emits exactly **N + 1** gated hits, then despawns.
- Existing projectile collision tests pass (or are updated once to the N+1
  semantics — see Task 04).
