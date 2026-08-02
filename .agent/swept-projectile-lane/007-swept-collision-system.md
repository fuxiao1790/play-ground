# 007 — Swept Collision System

**Depends on:** 002, 006
**Scope:** large (the feature)

## Goal

Resolve collision for the swept lane against the volume traced this frame, applying hits
nearest-first with correct impact positions.

Because swept projectiles never track (task 005), `Origin → Position` is the exact straight
path travelled this frame — the segment is not an approximation of a curve, and no
substepping or curvature correction is needed anywhere in this system.

The swept box always covers the whole step. There is no distance cap and no config singleton
to read — see task 001 for why the earlier `MaxSweepDistance` was dropped.

## New system: `SweptProjectileCollisionSystem`

`Assets/Scripts/System/Projectiles/SweptProjectileCollisionSystem.cs`. Structurally a
sibling of `ProjectileCollisionSystem` — same group, same ordering attributes
(`UpdateAfter(ProjectileContactGateSystem)`,
`UpdateBefore(CombatApplyFinalizeSingleSystem)`), so both lanes' hits land in the same
finalize pass and expansion sees this frame's spawn events.

### Singleton and dependency wiring — copy exactly

Reproduce [ProjectileCollisionSystem.cs:50-98](../../Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs#L50-L98)
verbatim in shape:

- Combine `TargetSpatialHashSingleton.BuildHandle` into `state.Dependency` before scheduling.
- Fetch the four lanes with `GetSingletonRW` (**not** `TryGet`) — a missing lane is a broken
  world and must throw. The comment at
  [:53-55](../../Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs#L53-L55)
  states this rule; keep an equivalent one here.
- Combine the scheduled handle into all four `ProducerHandle`s **and** into
  `TargetSpatialHashSingleton.ConsumerHandle`.

Two collision systems writing the same queues is already the established shape (projectile,
impact AOE, and lingering AOE collision all do it), so no lane needs restructuring.

### Job shape

```csharp
[WithAll(typeof(ProjectileTag), typeof(SweptProjectileTag), typeof(Active),
         typeof(CombatCollisionActiveTag))]
[WithDisabled(typeof(ArmingTag))]
```

`Execute` takes what the discrete job takes, plus `in ProjectileSweepComponent sweep`, and
takes `ref CombatKinematicsComponent kinematics` (**not** `in`) because the impact snap
writes `Position`.

Reuse the discrete job's early-out ladder unchanged: faction `None`, expired lifetime,
`PierceRemaining < 0`, `TotalTargetCount == 0`.

### Algorithm

**1. Build the swept box.** The segment is always the full step — `sweep.Origin` to
`kinematics.Position`, never clamped.

```csharp
CombatSweepMath.BuildSweptBox(
    sweep.Origin, kinematics.Position,
    collision.Radius, collision.HalfExtents, collision.RotationRadians, collision.ShapeType,
    out float2 boxCenter, out float2 boxHalfExtents, out float boxRotation);
```

**2. Broadphase — the existing spatial hash, unchanged.** Read
`TargetSpatialHashSingleton.ProjectileCollisionCells`, the same map the discrete lane queries.
No new broadphase structure, no second hash, no extra build cost: `TargetSpatialHashSystem`
already builds this map once per frame for all consumers
([TargetSpatialHashSystem.cs:130-136](../../Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs#L130-L136)),
and the swept lane is simply one more reader of it.

Take the box's world AABB via the existing
`CombatCollisionMath.ComputeWorldBounds(boxCenter, 0f, boxHalfExtents, boxRotation, Rectangle, ...)`,
expand by `MaxTargetRadius`, and walk cells exactly as
[:157-171](../../Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs#L157-L171)
does. The expansion is what preserves correctness against center-cell-only target insertion —
do not drop it. The only difference from the discrete path is that the AABB comes from the
swept box rather than from the projectile's own bounds.

**3. Gather candidates.** For each cell entry: skip same faction, skip gated targets
(`IsGated`), AABB prefilter, then the overlap test — which is the **existing** narrowphase,
with the swept box standing in as a rectangle:

```csharp
CombatCollisionMath.Hit(
    boxCenter, 0f, boxHalfExtents, boxRotation, CombatShapeType.Rectangle,
    targetPosition.Value, target.Radius, target.HalfExtents, target.RotationRadians, target.ShapeType)
```

No new overlap code. On a hit, append `(CombatSweepMath.ClosestApproachParam(...), targetIdx)`
to a **fixed-size stack array** of `CollisionConstants.MaxSweptHitsPerFrame` entries. No
native container, nothing allocated per entity.

A target spanning several cells can be gathered twice — deduplicate by target index while
inserting; the array is ≤16 entries so a linear scan is free.

When the array is full, keep the nearest: if the new `t` beats the current worst, replace the
worst. The cap then degrades toward correctness (nearest hits always win) rather than toward
arbitrariness.

**4. Sort by `t` ascending.** Insertion sort in place; `n ≤ 16`.

**5. Apply in order.** Per hit, in `t` order:

```
impactPoint = lerp(sweep.Origin, kinematics.Position, t)
```

- `EnqueueHitEvent(entity, targetEntity, payload)` — unchanged.
- `EnqueueOnHitProjectile` / `EnqueueOnHitAoe` — **pass `impactPoint`**, not
  `kinematics.Position`. Without this a fast projectile's impact AOE spawns at the end of the
  frame's motion, far past the target it hit. The aim direction for a child projectile is
  still derived from `impactPoint → targetPosition`.
- `AddOrRefreshGate(contactGates, targetKey, RepeatHitCooldownSeconds)` — unchanged.
- `projectileHit.PierceRemaining--`. If it goes below zero:
  **set `kinematics.Position = impactPoint`**, then `Deactivate(...)` and stop processing
  further hits.

Snapping on expiry keeps the render sprite and any death VFX on the target instead of
wherever the frame's motion ended. A projectile that survives (pierce remaining) keeps its
full end-of-frame position — it really did travel that far.

### Refactor opportunity while here

`EnqueueHitEvent`, `EnqueueOnHitProjectile`, `EnqueueOnHitAoe`, `Deactivate`, `IsGated`,
`AddOrRefreshGate`, `HasHitEvent`, `TargetKey`, `DirectionFromTo`, and `HashId` are about to
exist in two nearly identical copies. Extract them into a shared internal static
(`ProjectileHitEmission`) parameterized by an explicit impact position, and have the discrete
job pass `kinematics.Position` where the swept job passes `impactPoint`.

Doing this makes the discrete lane's spawn position an explicit argument rather than an
implicit one, which is the same change in meaning the swept lane needs — one definition, two
callers. Skipping it leaves two copies of the on-hit spawn contract that must be kept in
sync by hand, which is exactly the structural warning this project's decision rule names.

## Acceptance Criteria

- The tested segment is **always** the full `sweep.Origin → kinematics.Position` span. No
  clamp, no config read, no distance branch anywhere in the job.
- Overlap uses the existing `CombatCollisionMath.Hit` with the box as a
  `CombatShapeType.Rectangle`. No new overlap code is added.
- Hits apply in ascending `t` order; a `pierce = 0` swept projectile hits the **nearest**
  target on its path, never a farther one.
- On-hit child projectiles and AOEs spawn at the impact point, not the end-of-frame position.
- A projectile expiring mid-sweep ends at the impact point.
- The candidate array is a fixed-size stack array; the swept path allocates nothing per
  entity or per frame.
- Duplicate candidates (target spanning cells) are deduplicated.
- All four `ProducerHandle`s and `ConsumerHandle` receive the scheduled handle.
- Lanes are fetched with `GetSingletonRW`, not `TryGet`.
- Broadphase reads `TargetSpatialHashSingleton.ProjectileCollisionCells` — the existing hash,
  no new structure — and expands by `MaxTargetRadius`.
- Shared hit-emission helpers have one definition used by both lanes.

## If the cell walk ever gets hot

Not a task, and not a decision to make now — a note so nobody re-derives it later.

Because the box is never clamped, the number of cells visited scales with step length, and
the AABB rectangle is a loose cover for diagonal motion: the box is thin but its axis-aligned
bounds are near-square. At `speed 300` (5 u/frame) that is ~9 cells and irrelevant. It only
matters at speeds no authored content has.

Two remedies exist, **both inside the existing spatial hash** — neither adds a structure:

1. **Visit fewer cells of the same map.** Enumerate cells along the segment instead of the
   full AABB rectangle. Same `ProjectileCollisionCells`, same keys, same
   `MaxTargetRadius` expansion; only the loop that produces `(cx, cy)` changes.
2. **Query a coarser map.** `TargetSpatialHashSingleton` already carries three granularities —
   `ProjectileCollisionCells` at `2f`, `TrackingCells` at `16f`, `AoeOccupiedCells` at `32f`
   ([CombatSpatialHash.cs:20-22](../../Assets/Scripts/System/Api/Collision/Broadphase/CombatSpatialHash.cs#L20-L22)).
   A long sweep is closer in size to an AOE query than to a point query. `AoeOccupiedCells`
   inserts each target into every cell its bounds touch, so a query returns duplicates — but
   the swept job already deduplicates by target index, so it would drop in.

Either is isolated to step 2; nothing else in the job depends on how candidates are
enumerated. Ship the rect-walk, and revisit only if a real fast skill exists and the
projectile collision marker shows it.
