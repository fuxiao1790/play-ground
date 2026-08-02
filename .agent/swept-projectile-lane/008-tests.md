# 008 — Tests

**Depends on:** 007
**Scope:** medium

Per `Docs/coding-standards.md` (Test Hooks): observe through public runtime APIs and real
gameplay effects. Do not add production counters or flags that exist only for tests.

## EditMode — `Assets/Tests/EditMode/CombatSweepMathEditModeTests.cs` (new)

Highest value per line in the plan: `CombatSweepMath` is pure, allocation-free, and has no
ECS dependency, so it can carry most of the correctness burden cheaply.

- `SupportExtent` for a circle equals radius on every axis.
- `SupportExtent` for a `0.1 × 0.15` rectangle is `0.05` along its own x-axis, `0.075` along
  its y-axis, and matches the hand-computed projection on a diagonal axis.
- `SupportExtent` is invariant under `axis → −axis`.
- A rotated rectangle's support matches the unrotated one measured on a counter-rotated axis.
- `BuildSweptBox` for a zero-length sweep reproduces the original shape exactly.
- **Tunneling regression** — the test that encodes why the feature exists: start and end both
  clearly outside a target with the target centered between them.
  `CombatCollisionMath.Hit` is false at *both* endpoints; `Hit(sweptBox, target)` is true.
- **Box is tight, not a bounding circle:** a `0.1 × 1.0` projectile flying along its long axis
  produces `boxHalfExtents.y == 0.05`. This is the guard against regressing to the
  bounding-circle sweep an earlier draft used, which would have given `0.5`.
- Box length covers the segment plus the shape's extent along travel at both ends.
- `ClosestApproachParam` returns `0.5` for a target level with the midpoint, and clamps to
  `0`/`1` for targets behind the start or beyond the end.
- Swept box overlap agrees with the discrete test for all three target shapes when the
  segment is degenerate.

## EditMode — `Assets/Tests/EditMode/SweptProjectileAuthoringEditModeTests.cs` (new)

Covers task 003. Routing is authored, so these replace what would have been threshold tests.

- **Sweep/tracking exclusivity, authored:** a `ProjectileDefinition` with both
  `sweptCollision` and `trackingEnabled` fails `OnValidate`.
- **Sweep/tracking exclusivity, via support** — the case authoring review cannot catch: a
  swept skill plus a support calling `ProjectileBehaviorContext.EnableTracking` compiles to a
  definition with `SpawnBlocked == true` and emits a `SweptProjectileCannotTrack` entry at
  `Error` severity. It must **not** compile to a swept-with-tracking-disabled definition —
  that is the silent-degrade behavior this replaced.
- **The blocked slot does not fire and refunds:** `SkillDriver` skips it and the slot's fire
  is refunded, so no mana is spent and no cooldown consumed.
- **Flag reaches the command on all four spawn paths:** direct cast, interval child, on-hit
  child, stack detonation. Parameterize over the four rather than testing only the direct
  cast — a dropped flag on a child path is the likeliest way this feature silently fails.
- Sweep is not support-modifiable: no `SkillStat` resolves it, no behavior-context setter
  exists for it.
- **Tunneling warning fires only for tracking-capable projectiles:** a tracking projectile
  above the two-tick threshold emits `TrackingProjectileMayTunnel` at `Warning` severity; the
  same speed *without* tracking emits nothing; a slow tracking projectile emits nothing.
- **Current content produces no tunneling warnings** under the shipped lint constant — the
  guard against the threshold drifting into existing skills.
- **No tunneling math in the simulation assembly:** assert `PlayGround.Sim` contains no
  reference to the smallest-target lint constant. A compile-time-only rule is worth a
  mechanical check, since the natural way to "fix" a future edge case is to sneak the math
  into the collision job.

## PlayMode — `Assets/Tests/PlayMode/SweptProjectileSimulationTests.cs` (new)

Follow the fixture style of the existing `ProjectileCollisionSimulationTests.cs`.

- **Tunneling, end to end.** A projectile fast enough that one frame's step jumps it from
  clearly before a target to clearly past it registers a hit. The same setup on the discrete
  lane (speed below threshold, scaled distances) also hits — proving the test is measuring
  tunneling, not geometry.
- **Nearest-first.** Two targets on the path, `pierce = 0`. The **near** target takes damage;
  the far one does not. Run it with the targets registered in both orders so the result
  cannot come from iteration order.
- **Pierce order.** Three targets on the path, `pierce = 1`. The two nearest are hit; the
  farthest is not.
- **Impact position.** A swept projectile with an on-hit impact AOE spawns that AOE near the
  target's position, not near the projectile's end-of-frame position. Assert against the
  distance between them.
- **Expiry snap.** After a non-piercing swept projectile expires on a hit, its final
  `CombatKinematicsComponent.Position` is at the impact point.
- **Contact gate.** A piercing swept projectile does not double-hit one target within the
  repeat-hit cooldown, including when that target spans multiple broadphase cells.
- **No distance clamp.** A very large step (simulate a hitch by driving one long `dt`) still
  registers a target near the *far* end of the segment. This is the guard against
  reintroducing `MaxSweepDistance`, which would silently restore tunneling under exactly the
  frame conditions where tunneling is most likely.

## PlayMode — lane isolation (the highest-consequence, lowest-visibility failure)

Add to a new or existing spawn-pipeline test file:

- **Discrete lane must not consume swept slots.** Spawn swept projectiles, let them expire so
  their slots sit disabled, then spawn discrete projectiles. Assert every disabled swept slot
  still carries `SweptProjectileTag` and that no entity has `SweptProjectileTag` without the
  swept archetype's full component set. This is the `WithNone<SweptProjectileTag>` guard from
  task 005; without it the bug is silent corruption, not a crash, so it must be caught
  mechanically.
- **Swept archetype has no tracking component.** Assert `SweptProjectileTag` and
  `ProjectileTrackingComponent` never coexist on any entity — the structural half of the
  exclusivity rule, complementing the compile-time half above.
- **Tracking systems skip swept projectiles.** Spawn a swept projectile in a scene with a
  valid target off its flight axis; assert its velocity direction is unchanged after several
  frames (no steering applied).
- **No double integration.** A swept projectile advances exactly `Velocity * dt` per frame —
  the guard for the `WithNone` on `ProjectileMovementSystem` in task 006.
- **Both lanes counted.** `CombatStatsSingleton` spawn counters and
  `CombatStatsDisplaySingleton.ActiveProjectiles` include swept projectiles. The pool-cleanup
  calm-down gate derives despawns from these, so an uncounted lane skews trimming.

## Existing tests to re-run and update

- `ProjectileCollisionSimulationTests` — must stay green unchanged; the discrete lane's
  behavior is not meant to change.
- `ProjectileSpawnPipelineTests`, `SpawnCommandUnificationTests` — touch the expansion output
  and will need updating for the second command list.
- `CombatPoolCleanupSystemTests` — now sees a third pooled archetype.
- `ProjectileTrackingSimulationTests` — must stay green unchanged. Homing lives entirely in
  the discrete lane now, so this is a regression guard proving the split did not disturb it.

## Acceptance Criteria

- The tunneling regression test fails against `main` (before this feature) and passes after.
- The lane-isolation test fails if `WithNone<SweptProjectileTag>` is removed from either the
  discrete dead-slot query or the discrete movement job.
- Exclusivity is covered on both halves: compile-time (the combination is blocked, errors,
  and refunds) and structural (no entity carries both the tag and the tracking component).
- All four spawn paths are covered for flag propagation.
- No test reads a production field that exists only for testing.
