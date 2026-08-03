# 006 — Sweep Origin Capture

**Depends on:** 001, 005
**Scope:** small

> **Revised 2026-08-02.** This task previously specified a `SweptProjectileMovementSystem`
> that duplicated `ProjectileMovementSystem`. That was wrong — see
> [Why this is not a movement system](#why-this-is-not-a-movement-system). If you already
> implemented the old shape, this task now includes deleting it.

## Goal

Record where each swept projectile started its step, so the collision system can sweep the
exact segment it covered.

`ProjectileMovementSystem` is **not modified** by this task.

## Why this is not a movement system

Movement is byte-for-byte identical in both lanes — same integration, same bounds recompute:

```csharp
kinematics.Position += kinematics.Velocity * DeltaTime;
ProjectileCollisionMath.ComputeWorldBounds(...);
```

The only thing the swept lane needs extra is one bookkeeping write:

```csharp
sweep.Origin = kinematics.Position;
```

That is **collision input being captured**, not a different motion model. Modelling it as a
parallel movement system produced two files identical except for query attributes and one
line — including duplicated `UpdateInGroup` / `UpdateAfter` / `UpdateBefore` attributes, which
is the real hazard: reorder movement relative to tracking or the contact gate in one file and
not the other, and the two lanes silently integrate at different points in the frame. No
compile error, no crash, just lane-dependent behavior.

Splitting the capture out instead means the integration step stays a single job over both
archetypes, exactly as it was before this feature existed.

## Changes

### `ProjectileMovementSystem` — revert to its pre-feature state

If a `[WithNone(typeof(SweptProjectileTag))]` was added to `ProjectileMovementJob`, **remove
it**. The job should match `ProjectileTag + Active` and integrate both archetypes, with no
knowledge that the swept lane exists.

> Do not confuse this with the `WithNone<SweptProjectileTag>` on
> `ProjectileSpawnApplySystem._deadSlotQuery` (task 005). **That one is still required** — it
> keeps the discrete lane from claiming swept pool slots. Only the movement-job exclusion goes
> away.

### Delete `SweptProjectileMovementSystem.cs`

Its integration body is redundant with `ProjectileMovementSystem`, and its origin write moves
to the new system below.

### New system: `SweptProjectileOriginSystem`

`Assets/Scripts/System/Projectiles/SweptProjectileOriginSystem.cs`

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ProjectileMovementSystem))]
public partial struct SweptProjectileOriginSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new CaptureOriginJob().ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(ProjectileTag), typeof(SweptProjectileTag), typeof(Active))]
    [WithDisabled(typeof(ArmingTag))]
    private partial struct CaptureOriginJob : IJobEntity
    {
        private void Execute(
            in CombatKinematicsComponent kinematics,
            ref ProjectileSweepComponent sweep) => sweep.Origin = kinematics.Position;
    }
}
```

`[UpdateBefore(typeof(ProjectileMovementSystem))]` is the only ordering constraint it needs.

## Ordering safety — verified, not assumed

Only four things write `CombatKinematicsComponent.Position`:

| Writer | When | Interaction with capture |
|---|---|---|
| `ProjectileSpawnApplySystem` / `SweptProjectileSpawnApplySystem` | spawn, after collision | seeds `Position`; next frame's capture reads it |
| `ProjectileMovementSystem` | after capture | the integration capture is pairing with |
| `SweptProjectileCollisionSystem:295` | impact snap on expiry | entity is deactivated in the same branch, so it is never captured or moved again |

`ProjectileTrackingSystem` writes `Velocity` only, never `Position`. So nothing can move a
projectile between capture and integration.

## Keep the spawn-apply origin seed

Task 005 seeds `Origin = cfg.Position` on reuse. With capture running every frame that is
redundant on the normal path, but it still covers the arming window — arming projectiles are
excluded from both capture and collision, so their `Origin` would otherwise hold a previous
pool occupant's value until the tag clears. Cheap, and it means no code path can read an
uninitialized origin.

## Design notes worth preserving

- **Bounds keep their existing meaning.** `BoundsMin/Max` remain "bounds at the current
  position" in both lanes. The travel corridor is derived inside the collision job from
  `Origin`→`Position` and never written back, so every other reader stays correct without
  knowing the swept lane exists. The swept collision job additionally *reads*
  `BoundsMin/Max` — they are one half of its broadphase query region and the input to its
  discrete test — so movement must keep writing them for swept entities too.
- **The segment is the exact path, not an approximation.** Swept projectiles never track
  (task 005: the archetype has no `ProjectileTrackingComponent`, so `ProjectileSteeringJob`
  cannot match them), so velocity direction is constant and `Origin → Position` is precisely
  the ground covered this frame.
- **Rotation must stay untouched.** The corridor is exact only because
  `collision.RotationRadians` is constant across the step. Movement reads it to recompute
  bounds and must not write it. If a future feature rotates a projectile in flight, task 002's
  exactness claim needs revisiting.

## Cost

One extra pass over swept chunks: 8 bytes read, 8 written, on a population small by
construction, with `Position` still warm in cache for the movement job immediately after.
Cheaper than the duplicated bounds recompute the old shape performed.

## Acceptance Criteria

- `ProjectileMovementSystem` is identical to its pre-feature state — one job, no
  `WithNone<SweptProjectileTag>`, integrating both archetypes.
- `SweptProjectileMovementSystem.cs` is deleted.
- `SweptProjectileOriginSystem` runs before `ProjectileMovementSystem` and writes `Origin`
  for swept, non-arming, active projectiles only.
- A swept projectile advances exactly `Velocity * dt` per frame (no double integration).
- `ProjectileSpawnApplySystem._deadSlotQuery` still carries `WithNone<SweptProjectileTag>` —
  that exclusion is unrelated and must not be removed alongside the movement one.
- No integration or bounds-computation code is duplicated anywhere.
