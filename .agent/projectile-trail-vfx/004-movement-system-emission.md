# 004 — Movement System Trail Emission (Distance-Gated)

## Scope

`ProjectileMovementSystem` is where both projectile lanes integrate
`CombatKinematicsComponent.Position` once per frame
(`Docs/reference/simulation/projectile-system.md`: "Movement is shared data
math... `ProjectileMovementSystem` integrates both lanes"). This task makes it
also emit `LineSegmentVfxEvent`s for trailed projectiles — **gated on distance
traveled since the last emitted segment, not on simulation tick.** Emitting
once per tick was rejected: at high frame rates or for slow-moving
projectiles it produces many near-zero-length segments (excessive queue
growth and dispatch/upload cost for no visual gain), and segment density would
track frame rate rather than the projectile's actual path (unpredictable —
the same skill would look different at 60 vs 144 FPS). Gating on distance
makes segment spacing a function of authored `StepDistance` alone, independent
of tick rate, the same way Unity's built-in `TrailRenderer.minVertexDistance`
works.

## Changes

### `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`

Add imports:

```csharp
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
```

(check which are already implicitly available via other usings before adding
duplicates).

Change `OnUpdate` from:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var job = new ProjectileMovementJob
    {
        DeltaTime = SystemAPI.Time.DeltaTime
    };

    state.Dependency = job.ScheduleParallel(state.Dependency);
}
```

to:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    RefRW<CombatAoeVfxDispatchSingleton> vfx =
        SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();

    var job = new ProjectileMovementJob
    {
        DeltaTime = SystemAPI.Time.DeltaTime,
        LineSegmentVfxPending = vfx.ValueRO.PendingLineSegmentSpawns.AsParallelWriter()
    };

    JobHandle handle = job.ScheduleParallel(state.Dependency);
    vfx.ValueRW.ProducerHandle = JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, handle);
    state.Dependency = handle;
}
```

This is the exact `GetSingletonRW` + `AsParallelWriter` + `CombineDependencies`
pattern `TargetedResolveSystem.OnUpdate` already uses for the same singleton
(`Assets/Scripts/System/Targeted/TargetedResolveSystem.cs:65-94`) — no `TryGet`
guard, per [[fail-loud-singletons]].

Change `ProjectileMovementJob` — note the trail component is now `ref`, not
`in`, because the job mutates `LastEmitPosition`:

```csharp
[BurstCompile]
[WithAll(typeof(ProjectileTag), typeof(Active))]
[WithDisabled(typeof(ArmingTag))]
private partial struct ProjectileMovementJob : IJobEntity
{
    public float DeltaTime;
    public NativeQueue<LineSegmentVfxEvent>.ParallelWriter LineSegmentVfxPending;

    private void Execute(
        ref CombatKinematicsComponent kinematics,
        ref CombatCollisionComponent collision,
        ref ProjectileTrailVfxComponent trailVfx)
    {
        kinematics.Position += kinematics.Velocity * DeltaTime;
        CombatCollisionMath.ComputeWorldBounds(
            kinematics.Position,
            collision.Radius,
            collision.HalfExtents,
            collision.RotationRadians,
            collision.ShapeType,
            out collision.BoundsMin,
            out collision.BoundsMax);

        if (trailVfx.TrailId <= 0)
        {
            return;
        }

        float distanceSq = math.lengthsq(kinematics.Position - trailVfx.LastEmitPosition);
        float stepSq = trailVfx.StepDistance * trailVfx.StepDistance;
        if (distanceSq < stepSq)
        {
            return;
        }

        VfxEmit.EnqueueLineSegment(
            trailVfx.TrailId,
            trailVfx.LastEmitPosition,
            kinematics.Position,
            trailVfx.Width,
            LineSegmentVfxPending);
        trailVfx.LastEmitPosition = kinematics.Position;
    }
}
```

Notes on this shape:

- The `TrailId <= 0` check is first and returns early, so an untrailed
  projectile (the common case today) pays exactly one branch per frame beyond
  the existing integration — no distance math, no queue touch. This matches
  the cost model already accepted for AOE/Targeted's optional VFX slots.
- Distance comparison uses `math.lengthsq` against `StepDistance * StepDistance`,
  never `math.distance`/`sqrt` — only a threshold comparison is needed, not the
  actual distance value (see index.md Constraints).
- `LastEmitPosition` only advances when a segment is actually emitted, so
  segments are always **at least** `StepDistance` apart, never shorter
  (variable length, like Unity's `TrailRenderer.minVertexDistance` — this plan
  does not attempt to interpolate an exact point at `StepDistance` and carry a
  remainder forward; the extra precision has no authored consumer and would
  add state for no visual benefit).
- `VfxEmit.EnqueueLineSegment` still independently no-ops on an id that does
  not decode to `VfxDataShape.LineSegment` (`Assets/Scripts/System/Vfx/VfxEmit.cs:15`);
  this job's own `TrailId <= 0` check is a redundant-but-cheap fast path in
  front of it, not a replacement for it.

## Why Not `ProjectileContinuousStepComponent`

The continuous lane already captures a start-of-frame position in
`ProjectileContinuousStepComponent.Origin` via `ProjectileContinuousOriginSystem`,
but that component:

- only exists on the continuous archetype (the discrete archetype has no
  equivalent),
- is reseeded to the *current* position every single frame (it is a one-frame
  collision-sweep input, not an accumulator), so it cannot answer "how far
  since the last **emitted trail segment**" — that requires state that
  persists across many frames until a segment actually fires, and
- is a collision input (`CombatSweepMath.TryBuildTravelCorridor`), not a
  visual one; overloading it for trail bookkeeping would couple two unrelated
  concerns.

`ProjectileTrailVfxComponent.LastEmitPosition` is therefore its own field,
living exactly as long as the trail concept needs it, on both lanes uniformly.

## Acceptance Criteria

- A trailed projectile emits its first `LineSegmentVfxEvent` only after having
  moved at least `StepDistance` from its spawn position (since
  `LastEmitPosition` is seeded to `cfg.Position` in task 003), not on its first
  simulated frame.
- Across N frames of movement covering total distance `D`, a trailed
  projectile emits `floor(D / StepDistance)` events (approximately — exact
  count depends on per-frame step size relative to `StepDistance`), not N
  events.
- A stationary or arming-gated trailed projectile emits nothing (no distance
  accrues; arming projectiles are already excluded from this job by
  `[WithDisabled(typeof(ArmingTag))]`).
- Projectiles with `TrailId == 0` enqueue nothing and never touch
  `LastEmitPosition` (verify queue count is unchanged for a population of
  untrailed projectiles across several ticks).
- `CombatAoeVfxDispatchSingleton.ProducerHandle` correctly includes this job's
  handle — i.e. `CombatAoeVfxDispatchSystem`'s `ProducerHandle.Complete()` in
  `PresentationSystemGroup` is safe to call without a race (same guarantee
  `TargetedResolveSystem` already provides for its own emissions).
- No change to collision, tracking, or lifetime behavior — this task only adds
  a read/write of one new required component and a conditional VFX enqueue
  call to the existing movement job.

## Dependencies

Depends on 003 (`ProjectileTrailVfxComponent`, with `StepDistance`/
`LastEmitPosition`, must exist on both archetypes before this job can require
it — landing this before 003 would make every projectile entity fail to match
the query and stop moving). Feeds 005 (this is the change that turns
`ProjectileTrailVfxComponent` into a required query term for
`ProjectileMovementJob`).
