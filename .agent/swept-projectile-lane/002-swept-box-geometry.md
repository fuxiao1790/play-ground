# 002 — Swept Box Geometry

**Depends on:** 001
**Scope:** small (much smaller than the time-of-impact design it replaces)

> **Revised 2026-08-02.** The box no longer extends past the endpoints by the shape's extent
> along travel. It is the **travel corridor only**, and the swept lane runs it *in addition
> to* the ordinary discrete test rather than instead of it. See
> [The corridor covers the gap; discrete covers the ends](#the-corridor-covers-the-gap-discrete-covers-the-ends).

## Goal

Build the travel corridor as an **oriented box**: the projectile's silhouette width
perpendicular to travel, extruded along the segment from last tick's position to this tick's.
The box spans centre-to-centre — it does **not** include the projectile's footprint at either
end.

```
        previous tick                              this tick
             ┌───────────────────────────────────────┐
    ─ ─ ─ ─ ─┤◄── perpendicular support (half-width)  ├─ ─ ─ ─ ─►  travel axis
             └───────────────────────────────────────┘
             ↑                                       ↑
          Origin                                  Position
      (covered by last tick's                (covered by this tick's
        discrete check)                         discrete check)
```

## The corridor covers the gap; discrete covers the ends

The swept lane performs **two** tests per candidate and hits on either:

1. The ordinary discrete test — the projectile's own shape at `Position`, exactly what the
   discrete lane does.
2. The corridor test — this box.

Neither alone is sufficient, and together they need no end caps:

- The footprint at `Position` is covered by **this** tick's discrete test.
- The footprint at `Origin` was covered by **last** tick's discrete test.
- The space between them — the only space a discrete-only test can skip — is the corridor.

That is why the box stops at the endpoint centres. Extending it by the shape's extent along
travel, as an earlier draft specified, would duplicate coverage the discrete tests already
provide and widen the broadphase query for nothing.

It also removes a special case: a zero-length step no longer needs the box to degrade
gracefully into the projectile's own shape, because the discrete test is always running
anyway. The corridor is simply skipped when the step is degenerate.

**Consequence for `SupportExtent`:** only the perpendicular support is needed for the box.
The along-travel support is no longer computed.

### Known edge: the spawn frame

A target overlapping the projectile's spawn point but *behind* it relative to travel is not
tested, because there is no previous tick to have discretely covered `Origin`. This is
**identical to the discrete lane's existing behavior** — `ProjectileSpawnApplySystem` runs
after `ProjectileCollisionSystem` in the frame, so a freshly spawned projectile's first
collision test is also at its post-move position. Not a regression, and not worth special
handling; `SeedContactGateTargetId` already covers the on-hit-spawn case that would otherwise
notice.

## Why this replaces the swept-capsule / time-of-impact design

The earlier draft swept a *bounding circle* and solved analytic time-of-impact against each
target shape — three new quadratic solvers, plus a documented over-report for non-circular
projectiles. The box is both **tighter** and **cheaper**:

- **Tighter.** A `0.1 × 0.15` projectile travelling along its long axis gets a box of
  half-width `0.05`. The bounding-circle capsule would have used `0.090` — nearly double.
  The "long thin projectile breaks the approximation" caveat from the earlier draft
  disappears entirely: a `0.1 × 1.0` lance flying point-first sweeps a `0.1`-wide box, not a
  `1.0`-wide capsule.
- **Cheaper, and no new narrowphase at all.** The box is a `CombatShapeType.Rectangle`, and
  `CombatCollisionMath` already handles rectangle-versus-everything:
  `RectangleRectangle` (SAT), `CircleRectangle`, `RectangleCapsule`
  ([CombatCollisionMath.cs:88-95](../../Assets/Scripts/System/Api/Collision/Narrowphase/CombatCollisionMath.cs#L88-L95)).
  Both the discrete test and the corridor test are existing calls.

## Why this replaces the swept-capsule / time-of-impact design

The earlier draft swept a *bounding circle* and solved analytic time-of-impact against each
target shape — three new quadratic solvers, plus a documented over-report for non-circular
projectiles. The box is both **tighter** and **cheaper**:

- **Tighter.** A `0.1 × 0.15` projectile travelling along its long axis gets a box of
  half-width `0.05`. The bounding-circle capsule would have used `0.090` — nearly double.
  The "long thin projectile breaks the approximation" caveat from the earlier draft
  disappears entirely: a `0.1 × 1.0` lance flying point-first sweeps a `0.1`-wide box, not a
  `1.0`-wide capsule.
- **Cheaper, and no new narrowphase at all.** The box is a `CombatShapeType.Rectangle`, and
  `CombatCollisionMath` already handles rectangle-versus-everything:
  `RectangleRectangle` (SAT), `CircleRectangle`, `RectangleCapsule`
  ([CombatCollisionMath.cs:88-95](../../Assets/Scripts/System/Api/Collision/Narrowphase/CombatCollisionMath.cs#L88-L95)).
  The overlap test is one existing call.

## Two invariants make the box exact

Both verified in code, and both are consequences of decisions already made:

1. **The travel direction is constant across the step.** Swept projectiles never track
   (task 005), and `ProjectileMovementSystem` never writes `Velocity`.
2. **The shape's rotation is constant across the step.** `CombatCollisionComponent.RotationRadians`
   is read by movement ([ProjectileMovementSystem.cs:48](../../Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs#L48))
   but never written after spawn.

So the corridor is a pure translation of a fixed shape along a fixed direction. The box
is its exact bounding box — the only slack is at the ends, where the true swept hull has
slanted caps and the box squares them off. That slack now overlaps the discrete tests
anyway. Conservative, sub-collider-sized, and not
observable in gameplay.

## New file: `Assets/Scripts/System/Api/Collision/Narrowphase/CombatSweepMath.cs`

Namespace `PlayGround.System.Combat.Collision.Narrowphase`. Public static, Burst-compatible,
no allocations.

```csharp
// Half-extent of a shape projected onto a unit axis — how far the shape reaches from its
// center in that direction. Same axis-projection math RectangleBounds already uses, generalized
// from the world axes to an arbitrary one.
public static float SupportExtent(
    float radius,
    float2 halfExtents,
    float rotationRadians,
    CombatShapeType shapeType,
    float2 unitAxis);

// Builds the travel corridor between segmentStart and segmentEnd — centre to centre, with no
// end caps. The projectile's footprint at each endpoint is covered by that tick's discrete
// test, so the corridor deliberately stops short.
//
// Returns false for a degenerate segment; the caller then skips the corridor test entirely
// and relies on the discrete test alone. Outputs are in the same
// (center, halfExtents, rotation) form CombatCollisionMath.Hit takes for
// CombatShapeType.Rectangle, so the caller feeds them straight in.
public static bool TryBuildTravelCorridor(
    float2 segmentStart,
    float2 segmentEnd,
    float radius,
    float2 halfExtents,
    float rotationRadians,
    CombatShapeType shapeType,
    out float2 boxCenter,
    out float2 boxHalfExtents,
    out float boxRotationRadians);

// Normalized position along the segment where the target center is nearest the path.
// Doubles as the hit-ordering key and the impact position source. Clamped to [0, 1].
public static float ClosestApproachParam(
    float2 segmentStart,
    float2 segmentEnd,
    float2 targetPosition);
```

### `SupportExtent`

```
Circle    → radius
Rectangle → |halfExtents.x * dot(xAxis, axis)| + |halfExtents.y * dot(yAxis, axis)|
Capsule   → radius + |halfExtents.x * dot(capsuleAxis, axis)|
```

where `xAxis = Rotate((1,0), rotation)`, `yAxis = Rotate((0,1), rotation)`, and `capsuleAxis`
comes from the **same convention `CombatCollisionMath.GetCapsuleSegment` uses** —
`Rotate((0,1), rotation)`, half-length `halfExtents.x`. Do not re-derive it; if that helper
is private, make it internal rather than duplicating the convention across two files.

### `TryBuildTravelCorridor`

```
delta = segmentEnd - segmentStart
dist  = length(delta)
if (dist <= Epsilon) return false;                // caller skips the corridor test

dir  = delta / dist
perp = (-dir.y, dir.x)
perpSupport = SupportExtent(..., perp)

boxCenter          = (segmentStart + segmentEnd) * 0.5
boxRotationRadians = atan2(dir.y, dir.x)
boxHalfExtents     = ( dist * 0.5, perpSupport )
```

`boxHalfExtents.x` is **exactly half the travel distance** — no `+ alongSupport`. The corridor
runs centre to centre; the footprints at each end belong to the discrete tests.

`boxHalfExtents.x` lies along the box's local x-axis, which is `Rotate((1,0), boxRotation)` —
matching `CombatCollisionMath`'s rectangle convention exactly, so the box drops into
`Hit(...)` with no adapter.

**Degenerate segment** (`|end − start| ≤ Epsilon`, e.g. a stationary or arming-released
projectile): return `false` without normalizing. The caller runs only the discrete test, which
is exactly right — there is no corridor to check. No fallback shape, no special case in the
math.

### `ClosestApproachParam`

```
ab = segmentEnd - segmentStart
t  = clamp(dot(targetPosition - segmentStart, ab) / lengthsq(ab), 0, 1)
```

Zero-length segment returns `0`.

**This is closest approach, not first contact.** They differ by a fraction of the target
radius — first contact is slightly earlier. That difference does not matter for either
consumer:

- **Ordering:** sorting by closest approach gives the same near-to-far order as first contact
  except for targets of very different sizes at near-identical distances, where either order
  is defensible.
- **Impact position:** "the point on the path nearest the target center" is arguably the more
  natural impact point than "where the hulls first grazed", and it is what a player reads as
  the hit location.

Getting true first-contact would mean reinstating the three per-shape quadratic solvers this
task exists to delete. Four ops beats that.

## Do not cache the perpendicular support

It is genuinely constant for a swept projectile's whole life — direction is fixed (no
tracking) and rotation is fixed (never written after spawn) — so it *could* be computed once
in spawn apply and stored on `ProjectileSweepComponent`. **Don't.**

The saving is small: one `sincos` plus two dot products per projectile per tick, order 20–30
cycles. At 10k concurrent swept projectiles that is well under 0.1 ms spread across workers,
against a job already doing a cell walk and per-candidate SAT.

The cost is not small. One more field seeded on every pooled reuse, in a lane whose
highest-risk failure mode is already "a pooled slot carries stale state from its previous
occupant" (task 005). Worse, it converts two documented invariants from *claims about
exactness* into *correctness dependencies of stored data*: if anything ever rotates a
projectile in flight or lets a swept projectile steer, a live recomputation silently becomes
right while a cached value silently becomes wrong. Pure functions fail loudly under that
change; caches do not.

These projectiles are fast and short-lived, so the per-instance amortization window is a
handful of ticks anyway.

## Known micro-inefficiency, deliberately accepted

`TryBuildTravelCorridor` returns `boxRotationRadians = atan2(dir.y, dir.x)`, and
`CombatCollisionMath.Hit` immediately calls `Rotate(..., rotation)` internally, doing a
`sincos` to recover the same direction vector. That round-trip is the most expensive thing in
the swept geometry path — `atan2` costs more than the `SupportExtent` call it accompanies.

Two clean fixes exist if profiling ever justifies one:

1. Add axis-taking overloads to `CombatCollisionMath` so a rectangle can be described by its
   axis pair instead of an angle, removing the round-trip for every caller, not just this one.
2. Cache the angle on the entity — it too is lifetime-constant.

Prefer (1) if it comes up: it fixes the cause rather than memoizing around it, and it does not
add poolable state. Neither is in scope now — note it in a comment and move on.

## Acceptance Criteria

- No allocations, no managed types; compiles inside a Burst job.
- The support extent is computed per tick from the live component values, not stored.
- Capsule geometry derives from the same convention as `CombatCollisionMath`, not a copy.
- `TryBuildTravelCorridor` output feeds
  `CombatCollisionMath.Hit(..., CombatShapeType.Rectangle, ...)` directly, with no adapter or
  coordinate fix-up at the call site.
- **`boxHalfExtents.x == dist * 0.5` exactly** — no along-travel extension. The corridor ends
  at the endpoint centres.
- Degenerate segment returns `false`; no fallback shape is produced.
- **No new narrowphase overlap code is added** — overlap stays in `CombatCollisionMath`.

## Test Notes (implemented in 008, designed here)

Pure functions, no ECS, cheap EditMode coverage:

- `SupportExtent` for a circle equals radius on every axis.
- `SupportExtent` for a `0.1 × 0.15` rectangle is `0.05` along its own x-axis and `0.075`
  along its y-axis, and the diagonal case matches the hand-computed projection.
- `SupportExtent` is invariant under `axis → −axis`.
- A rotated rectangle's support matches the unrotated one measured on a counter-rotated axis.
- `TryBuildTravelCorridor` returns `false` for a zero-length sweep and writes no box.
- **Tunneling regression:** start and end both clearly outside a target, target centered
  between them. `CombatCollisionMath.Hit` is false at *both* endpoints;
  `Hit(corridor, target)` is true. This is the test that encodes why the feature exists.
- Box width matches the projectile silhouette: a `0.1 × 1.0` projectile flying along its long
  axis produces `boxHalfExtents.y == 0.05`, not `0.5`.
- **Corridor length is exactly the travel distance:** `boxHalfExtents.x == dist * 0.5`. Guards
  against reintroducing the along-travel extension.
- **Corridor alone does not cover the endpoints:** a target overlapping the projectile only at
  `Position` — beside the corridor, not on it — is missed by `Hit(corridor, ...)` and caught by
  the discrete test. This is the test that proves the two checks are complementary rather than
  the corridor being a superset.
- `ClosestApproachParam` returns `0.5` for a target level with the segment midpoint, and
  clamps to `0`/`1` for targets behind the start or beyond the end.
