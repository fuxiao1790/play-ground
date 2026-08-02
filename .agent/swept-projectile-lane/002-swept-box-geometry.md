# 002 — Swept Box Geometry

**Depends on:** 001
**Scope:** small (much smaller than the time-of-impact design it replaces)

## Goal

Build the swept volume as an **oriented box**: the projectile's silhouette width
perpendicular to travel, extruded along the segment from last tick's position to this
tick's, capped at both ends by the shape's extent along travel.

```
        previous tick                              this tick
             ┌───────────────────────────────────────┐
    ─ ─ ─ ─ ─┤◄── perpendicular support (half-width)  ├─ ─ ─ ─ ─►  travel axis
             └───────────────────────────────────────┘
             ↑                                       ↑
      start − alongSupport                    end + alongSupport
```

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

So the swept region is a pure translation of a fixed shape along a fixed direction. The box
is its exact bounding box — the only slack is at the ends, where the true swept hull has
slanted caps and the box squares them off. Conservative, sub-collider-sized, and not
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

// Builds the oriented box covering the shape's translation from segmentStart to segmentEnd.
// Outputs are in the same (center, halfExtents, rotation) form CombatCollisionMath.Hit takes
// for CombatShapeType.Rectangle, so the caller feeds them straight in.
public static void BuildSweptBox(
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

### `BuildSweptBox`

```
dir  = normalize(segmentEnd - segmentStart)      // degenerate → see below
perp = (-dir.y, dir.x)
alongSupport = SupportExtent(..., dir)
perpSupport  = SupportExtent(..., perp)

boxCenter          = (segmentStart + segmentEnd) * 0.5
boxRotationRadians = atan2(dir.y, dir.x)
boxHalfExtents     = ( distance(segmentStart, segmentEnd) * 0.5 + alongSupport,
                       perpSupport )
```

`boxHalfExtents.x` lies along the box's local x-axis, which is `Rotate((1,0), boxRotation)` —
matching `CombatCollisionMath`'s rectangle convention exactly, so the box drops into
`Hit(...)` with no adapter.

**Degenerate segment** (`|end − start| ≤ Epsilon`, e.g. a stationary or just-spawned
projectile): do not normalize. Return the projectile's own shape unchanged — center
`segmentStart`, its own `halfExtents`/`radius`/`rotation` — so the test degrades exactly to
the discrete one.

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

## Do not cache the support extents

Both supports are genuinely constant for a swept projectile's whole life — direction is fixed
(no tracking) and rotation is fixed (never written after spawn) — so they *could* be computed
once in spawn apply and stored on `ProjectileSweepComponent`. **Don't.**

The saving is small: one `sincos` plus four dot products per projectile per tick, order 30–40
cycles. At 10k concurrent swept projectiles that is well under 0.1 ms spread across workers,
against a job already doing a cell walk and per-candidate SAT.

The cost is not small. Two more fields seeded on every pooled reuse, in a lane whose
highest-risk failure mode is already "a pooled slot carries stale state from its previous
occupant" (task 005). Worse, it converts two documented invariants from *claims about
exactness* into *correctness dependencies of stored data*: if anything ever rotates a
projectile in flight or lets a swept projectile steer, a live recomputation silently becomes
right while a cached value silently becomes wrong. Pure functions fail loudly under that
change; caches do not.

These projectiles are fast and short-lived, so the per-instance amortization window is a
handful of ticks anyway.

## Known micro-inefficiency, deliberately accepted

`BuildSweptBox` returns `boxRotationRadians = atan2(dir.y, dir.x)`, and
`CombatCollisionMath.Hit` immediately calls `Rotate(..., rotation)` internally, doing a
`sincos` to recover the same direction vector. That round-trip is the most expensive thing in
the swept geometry path — `atan2` costs more than both `SupportExtent` calls combined.

Two clean fixes exist if profiling ever justifies one:

1. Add axis-taking overloads to `CombatCollisionMath` so a rectangle can be described by its
   axis pair instead of an angle, removing the round-trip for every caller, not just this one.
2. Cache the angle on the entity — it too is lifetime-constant.

Prefer (1) if it comes up: it fixes the cause rather than memoizing around it, and it does not
add poolable state. Neither is in scope now — note it in a comment and move on.

## Acceptance Criteria

- No allocations, no managed types; compiles inside a Burst job.
- Support extents are computed per tick from the live component values, not stored.
- Capsule geometry derives from the same convention as `CombatCollisionMath`, not a copy.
- `BuildSweptBox` output feeds `CombatCollisionMath.Hit(..., CombatShapeType.Rectangle, ...)`
  directly, with no adapter or coordinate fix-up at the call site.
- Degenerate segment reproduces the discrete shape exactly.
- **No new narrowphase overlap code is added** — overlap stays in `CombatCollisionMath`.

## Test Notes (implemented in 008, designed here)

Pure functions, no ECS, cheap EditMode coverage:

- `SupportExtent` for a circle equals radius on every axis.
- `SupportExtent` for a `0.1 × 0.15` rectangle is `0.05` along its own x-axis and `0.075`
  along its y-axis, and the diagonal case matches the hand-computed projection.
- `SupportExtent` is invariant under `axis → −axis`.
- A rotated rectangle's support matches the unrotated one measured on a counter-rotated axis.
- `BuildSweptBox` for a zero-length sweep returns the original shape.
- **Tunneling regression:** start and end both clearly outside a target, target centered
  between them. `CombatCollisionMath.Hit` is false at *both* endpoints;
  `Hit(sweptBox, target)` is true. This is the test that encodes why the feature exists.
- Box width matches the projectile silhouette: a `0.1 × 1.0` projectile flying along its long
  axis produces `boxHalfExtents.y == 0.05`, not `0.5`.
- `ClosestApproachParam` returns `0.5` for a target level with the segment midpoint, and
  clamps to `0`/`1` for targets behind the start or beyond the end.
