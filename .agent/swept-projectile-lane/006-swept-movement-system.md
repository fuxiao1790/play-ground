# 006 — Swept Movement System

**Depends on:** 001, 005
**Scope:** small

## Goal

Record where each swept projectile started the frame, then integrate exactly as the
discrete lane does. Keep the discrete movement job off the swept archetype.

## Changes

### `ProjectileMovementSystem` — exclude the swept archetype

The existing job is `[WithAll(typeof(ProjectileTag), typeof(Active))]`
([:33](../../Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs#L33)), which now
matches both archetypes. Add:

```csharp
[WithNone(typeof(SweptProjectileTag))]
```

Without this, swept projectiles would be integrated twice per frame — once by each system —
which is a double-speed bug, not merely redundant work.

### New system: `SweptProjectileMovementSystem`

`Assets/Scripts/System/Projectiles/SweptProjectileMovementSystem.cs`. Same attributes as
`ProjectileMovementSystem` (`SimulationSystemGroup`, `UpdateAfter(ProjectileTrackingSystem)`,
`UpdateBefore(ProjectileContactGateSystem)`), same `[BurstCompile]`, same
`[WithDisabled(typeof(ArmingTag))]`.

```csharp
[WithAll(typeof(ProjectileTag), typeof(SweptProjectileTag), typeof(Active))]
[WithDisabled(typeof(ArmingTag))]
private partial struct SweptProjectileMovementJob : IJobEntity
{
    public float DeltaTime;

    private void Execute(
        ref CombatKinematicsComponent kinematics,
        ref CombatCollisionComponent collision,
        ref ProjectileSweepComponent sweep)
    {
        // Origin is written before integration, so the collision job later this frame
        // sweeps exactly the segment this step covered.
        sweep.Origin = kinematics.Position;

        kinematics.Position += kinematics.Velocity * DeltaTime;
        ProjectileCollisionMath.ComputeWorldBounds(
            kinematics.Position,
            collision.Radius,
            collision.HalfExtents,
            collision.RotationRadians,
            collision.ShapeType,
            out collision.BoundsMin,
            out collision.BoundsMax);
    }
}
```

## Design notes worth preserving

- **Bounds keep their existing meaning.** `BoundsMin/Max` remain "bounds at the current
  position" in both lanes. The swept box is derived inside the collision job from
  `Origin`→`Position` and never written back. Every other reader of those fields stays
  correct without knowing the swept lane exists.
- **Rotation must stay untouched.** The swept box is exact only because
  `collision.RotationRadians` is constant across the step — this job reads it (to recompute
  bounds) and must not write it. If a future feature ever rotates a projectile in flight, the
  swept box becomes an approximation and task 002's exactness claim needs revisiting.
- **The segment is the exact path, not an approximation.** Swept projectiles never track
  (task 005: the archetype has no `ProjectileTrackingComponent`, so
  `ProjectileSteeringJob` cannot match them), so velocity direction is constant for the
  whole life of the projectile and `Origin → Position` is precisely the ground covered
  this frame. There is no curvature error to reason about.
- **Arming needs no special handling.** Arming entities are excluded from movement, so the
  first unarmed frame writes `Origin = Position` (the pre-move position) naturally. There is
  no stale-origin window on arming release.
- **Why store `Origin` at all** rather than reconstructing `Position - Velocity * DeltaTime`
  in the collision job: spawn apply declares no order against movement, so on the spawn frame
  the reconstruction can name a point behind the muzzle that the projectile never occupied —
  producing phantom hits on targets behind the caster. 8 bytes on swept entities only is the
  cheaper correct answer.

## Acceptance Criteria

- `ProjectileMovementSystem` excludes `SweptProjectileTag`; verified that a swept projectile
  advances exactly `Velocity * dt` per frame, not twice that.
- `SweptProjectileMovementSystem` writes `Origin` **before** integrating.
- Bounds computation is identical to the discrete lane (same helper, same arguments).
- Both movement systems are `[BurstCompile]` and schedule with `ScheduleParallel`.
