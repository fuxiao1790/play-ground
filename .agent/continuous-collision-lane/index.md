---
name: continuous-collision-lane
description: Add a second projectile archetype/lane whose collision is resolved against the swept volume of the frame's motion, so high-speed projectiles cannot tunnel past targets. Membership is authored per skill, and is mutually exclusive with target tracking.
---

# Continuous Collision Lane

## Summary

Projectile collision today is discrete: [ProjectileMovementSystem.cs:43](../../Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs#L43)
integrates `Position += Velocity * dt` and rebuilds the AABB at the new position;
[ProjectileDiscreteCollisionSystem.cs:194-216](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L194-L216)
narrowphases at that one position. Nothing tests the space the projectile crossed, so a fast
enough projectile passes cleanly through a target.

This plan adds a **second projectile archetype and lane** discriminated by
`ProjectileContinuousTag`. Its collision runs the ordinary discrete test **plus** a continuous one
against an **oriented box** covering the frame's motion: the projectile's silhouette width
perpendicular to travel, extruded along the segment from last tick's position to this tick's.
The box is the travel corridor only — it stops at the endpoint centres, because this tick's
discrete test covers the footprint at `Position` and last tick's covered `Origin`. Hits from
either test apply nearest-first.

Three things define the shape of the design:

- **Membership is authored**, via a `continuousCollision` flag on `ProjectileDefinition`. It is
  not derived from speed at runtime.
- **Sweep and target tracking are mutually exclusive.** Tracking is only available on
  discrete (slow) projectiles. This is enforced structurally — the continuous archetype does not
  carry `ProjectileTrackingComponent` at all — and resolved at skill-compile time when a
  support tries to create the combination.
- **The box always spans the full step.** There is no distance cap and no sweep config
  singleton. Cost is controlled in the broadphase, never by dropping coverage.

Everything that queries `ProjectileTag` (contact gates, lifetime, stats) and everything that
queries the common render components (render prepare) picks up the new archetype with **no
change**. The tracking systems skip it automatically. Only collision, movement, spawn apply,
expansion fan-out, and the authoring chain are touched.

## Rationale For Major Architectural Decisions

- **Separate archetype/lane, not an enableable overlay on one archetype.** User-directed,
  and the exclusivity rule independently justifies it: with tracking excluded, the two
  archetypes have genuinely different component sets (swept has `ProjectileContinuousTag` +
  `ProjectileContinuousStepComponent` and *lacks* `ProjectileTrackingComponent`), which is what
  archetypes are for. An enableable overlay would have to keep the tracking component on
  swept entities and disable it — carrying ~40 bytes of dead state and leaving the
  exclusivity rule as a convention rather than a structural fact. The continuous archetype is
  net *smaller* than the discrete one.
- **The AOE lanes are the same design, already shipped.** [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs)
  runs two archetypes discriminated by `LingeringAoeTag`, with two dead-slot queries
  (`WithNone<LingeringAoeTag>` at :65, `WithAll<LingeringAoeTag>` at :297). Each lane's pool
  is self-contained, so there is no command↔slot matching problem. This plan copies that
  structure, including its shared-materialization discipline.
- **Authored, not derived.** Routing on a speed threshold would mean the lane depends on a
  runtime-modified stat (`SkillStat.ProjectileSpeed` is support-modifiable), making content
  behavior depend on a computation authors cannot see. An authored flag is inspectable,
  reviewable, and testable. The cost is that a designer can author a fast projectile and
  forget the flag; that is accepted — skills are authored deliberately for this. The one case
  that gets a linter warning is a **tracking-capable** projectile that is too fast, because
  tracking forbids sweep, so that author has no fix available at runtime.
- **Exclusivity is enforced where both values are final.** `ProjectileBehaviorContext.EnableTracking`
  ([BehaviorContexts.cs:41-46](../../Assets/Scripts/Skills/Modifiers/BehaviorContexts.cs#L41-L46))
  lets a support turn tracking on during skill compilation, so the conflict is reachable
  from two individually-valid pieces of content and cannot be prevented by authoring review
  alone. `SkillSetCompiler.BuildRuntime` is the one point after modifiers where both final
  values are known.
- **Sweep + tracking is an error that rejects the spawn**, not a silent degrade. The
  combination is detected in the compiler and marks the definition `SpawnBlocked`;
  `SkillDriver` then refuses to fire the slot and refunds the cast through the existing
  `ReceiveSpawnRejected` semantics. Silently dropping tracking would leave a player wondering
  why their homing support does nothing; failing loudly matches
  `Docs/coding-standards.md` (Fail Fast Validation).
- **All speed/tunneling math lives in the compiler, never in ECS.** The check runs once per
  compiled skill and produces a warning; no simulation system computes a threshold, reads a
  target-size constant, or re-evaluates anything per spawn. This is what keeps "authored, not
  derived" true in practice rather than just at the flag.
- **The continuous check sits on top of the discrete check, not in place of it.** The corridor
  box spans endpoint centre to endpoint centre and carries no end caps, so it covers exactly
  the space a discrete-only test can skip — nothing more. The footprints at each end are
  covered by that tick's own discrete test. Two consequences: the corridor never duplicates
  coverage the discrete test already provides, and a degenerate (zero-length) step needs no
  special case, because the discrete test is running regardless.
- **The swept volume is an oriented box, not a swept bounding circle.** An earlier draft swept
  the projectile's bounding circle and solved analytic time-of-impact per target shape — three
  new quadratic solvers plus a documented over-report for non-circular projectiles. The box is
  both tighter and cheaper. Tighter: a `0.1 × 0.15` projectile travelling along its long axis
  sweeps a half-width of `0.05` where the bounding circle used `0.090`, and the "long thin
  projectile breaks the approximation" caveat disappears entirely. Cheaper: the box *is* a
  `CombatShapeType.Rectangle`, and `CombatCollisionMath` already implements
  rectangle-versus-everything
  ([CombatCollisionMath.cs:88-95](../../Assets/Scripts/System/Api/Collision/Narrowphase/CombatCollisionMath.cs#L88-L95)),
  so the overlap test adds **no new narrowphase code at all**.
- **The box is exact, not conservative,** because both inputs are constant across a step:
  travel direction (continuous projectiles never track, and movement never writes `Velocity`) and
  shape rotation (`CombatCollisionComponent.RotationRadians` is never written after spawn).
  The only slack is squared-off end caps where the true swept hull is slanted.
- **Hit ordering and impact position come from closest approach, not true first contact.**
  Two existing behaviors require *some* ordering and *some* impact point:
  `EnqueueOnHitProjectile`/`EnqueueOnHitAoe` spawn children at `kinematics.Position`
  ([ProjectileDiscreteCollisionSystem.cs:279](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L279), :316, :329),
  which under any swept test would land far past the target hit; and `PierceRemaining--`
  ([:230](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L230)) applies
  in hash order, so a `pierce = 1` projectile could damage the farthest of three targets and
  expire before the nearest. Closest approach is a four-op projection that satisfies both,
  where true first contact would mean reinstating the per-shape solvers. The two differ by a
  fraction of the target radius and produce the same near-to-far order in all but pathological
  cases.
- **No distance cap on the swept box.** An earlier draft clamped the tested segment to a
  `MaxSweepDistance` to bound broadphase cost during a frame hitch. Dropped: a hitch produces
  the longest step, which is when a projectile is *most* likely to skip a target, and that is
  exactly when the clamp would have stopped testing. A collision feature whose coverage
  degrades under load is not a collision feature. The cost concern is real but belongs to cell
  enumeration inside the existing spatial hash, not to coverage; see the note at the end of
  [007](007-continuous-collision-system.md).
- **Bounds semantics stay identical in both lanes.** `CombatCollisionComponent.BoundsMin/Max`
  keep meaning "bounds at the current position"; the swept AABB is derived inside the
  collision job and never written back, so every other reader stays correct without knowing
  the lane exists.

## Constraints & Invariants The Change Must Respect

| Invariant | Source |
|---|---|
| Systems consuming cross-feature components must also require a domain tag (`ProjectileTag`/`AoeTag`) | `Docs/coding-standards.md` — Hybrid ECS/Scene Rule; `Docs/layers/ecs-simulation.md` Forbidden Dependencies |
| Do not reach into another system's fields; cross-system data flows through singletons with explicit `JobHandle` fields | `Docs/coding-standards.md` — System Encapsulation |
| Producer jobs complete before expansion drains queues; apply runs after expansion and after current-frame collision | `Docs/contracts/spawn-events-and-commands.md` — Ordering |
| Events are registry-link + instance frame; commands are resolved allocation intent; expansion owns the dereference | `Docs/contracts/spawn-events-and-commands.md` — Guarantees |
| Combat hot paths must be allocation-light | `Docs/coding-standards.md` — Allocation Rule |
| Every scalable combat system needs an obvious budget and fallback path | `Docs/coding-standards.md` — Performance Budget Rule |
| Required serialized fields validate once at setup; do not hide bad setup with no-op behavior | `Docs/coding-standards.md` — Fail Fast Validation |
| `ECS Lifecycle:` comments update in the same change that alters a lifecycle | `Docs/coding-standards.md` — ECS Lifecycle Comments |
| Created `EntityQuery` / native containers are disposed by their owner | `Docs/coding-standards.md` — Native And ECS Handle Ownership |
| Entity reuse is faction- and render-kind-agnostic **within the matching archetype** | `Docs/contracts/spawn-events-and-commands.md` — Notes/TODOs |
| A support can enable tracking after authoring, so exclusivity cannot be enforced at the asset alone | [BehaviorContexts.cs:41-46](../../Assets/Scripts/Skills/Modifiers/BehaviorContexts.cs#L41-L46) |
| `IJobEntity` matches only archetypes carrying every `Execute` parameter — the mechanism that makes tracking self-exclude | [ProjectileTrackingSystem.cs:71](../../Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs#L71), [:389](../../Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs#L389) |
| `TargetSpatialHashSingleton` consumers combine into `ConsumerHandle` after depending on `BuildHandle` | [TargetSpatialHashSystem.cs:88-92](../../Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs#L88-L92) |
| Targets are inserted into `ProjectileCollisionCells` at their **center cell only**; queries must expand by `MaxTargetRadius` | [TargetSpatialHashSystem.cs:311-314](../../Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs#L311-L314) + [ProjectileDiscreteCollisionSystem.cs:157-163](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L157-L163) |
| Spawn and hit-dispatch lanes are created unconditionally; a missing lane must throw, not be skipped | [ProjectileDiscreteCollisionSystem.cs:53-63](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L53-L63); memory note *fail-loud singletons* |
| A job taking `EnabledRefRW<ArmingTag>` scheduled with an explicit `EntityQuery` must list `ArmingTag` in that query | `Docs/reference/simulation/ecs-notes.md` (Arming section) |

## Mechanisms Reused vs. Introduced

**Reused:**
- The **two-archetype lane split**, copied from the impact/lingering AOE pair, including the
  `WithNone<Tag>` / `WithAll<Tag>` dead-slot discipline and the shared-materialization
  utility whose stated purpose at
  [AoeSpawnApplySystem.cs:555-558](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L555-L558)
  is exactly to stop two lanes duplicating lifecycle logic.
- `SpawnPoolTopUp.EnsureDisabledSlots` — unchanged; already parameterized by archetype and query.
- `TargetSpatialHashSingleton.ProjectileCollisionCells` — the continuous lane is one more reader of
  the broadphase the discrete lane already uses. No new hash, no extra per-frame build, no
  change to `TargetSpatialHashSystem` beyond the swept job joining `ConsumerHandle`.
- The event→command→apply pipeline (ADR-003) and the existing `ProjectileSpawnEvent` queue.
  One event type, one queue; only the command output fans out.
- `SkillLoadoutValidator` / `SkillValidationWarning` — the project's existing channel for
  reporting content conflicts, consumed at [SkillDriver.cs:192](../../Assets/Scripts/Skills/SkillDriver.cs#L192).
- The `int` 0/1 flag convention already used by `HasTimedSpawner` in `ProjectileSpawnCommand`.
- `CombatCollisionMath.Hit` and `ComputeWorldBounds` — used twice per candidate: once for the
  ordinary discrete test, once with the corridor passed as an ordinary rectangle. The feature
  adds no overlap or bounds code of its own.
- Verified untouched: `CombatPoolCleanupSystem`, `CombatStatsGatherSystem`,
  `CombatRenderPrepareSystem`, `CombatLifetimeSystem`, `ProjectileContactGateSystem`,
  `ProjectileTrackingSystem`.

**Introduced (justified):**
- `ProjectileContinuousTag` — archetype discriminator; analogue of `LingeringAoeTag`.
- `ProjectileContinuousStepComponent { float2 Origin; }` — swept-only. Needed because spawn apply has
  no declared order against movement, so `Position - Velocity * dt` can name a point behind
  the muzzle on the spawn frame.
- `CombatSweepMath` — three small pure functions (`SupportExtent`, `TryBuildTravelCorridor`,
  `ClosestApproachParam`). Geometry construction only; **no overlap code**, which stays in
  `CombatCollisionMath`.
- `ProjectileDefinition.continuousCollision` and its chain down to `ProjectileSpawnCommand`.
- `ProjectileContinuousSpawnApplySystem`, `ProjectileContinuousOriginSystem`,
  `ProjectileContinuousCollisionSystem`. The collision system is not duplication; it is the feature.
  Movement is **not** duplicated — `ProjectileMovementSystem` serves both archetypes unchanged.
- A second `NativeList<ProjectileSpawnCommand> ContinuousCommands` on the existing
  `ProjectileSpawnEventSingleton` — deliberately not a second singleton, see Design Validation.

## Design Validation

- **Domain-tag rule.** Every new job declares
  `WithAll(typeof(ProjectileTag), typeof(ProjectileContinuousTag), ...)`. No new system keys off
  `Active`, `CombatCollisionComponent`, or scope membership alone.
- **Exclusivity holds on both halves.** *Structurally:* the continuous archetype omits
  `ProjectileTrackingComponent`, and both tracking jobs take it as an `Execute` parameter, so
  `IJobEntity` cannot match continuous chunks — no filter, no convention, no way to regress by
  editing a query. *At compile time:* `BuildRuntime` drops tracking when sweep is set, after
  modifiers have run, which is the only point where a support-enabled tracking flag is visible.
- **Discrete lane does not steal swept slots.** `ProjectileTag` matches both archetypes, so
  `ProjectileDiscreteSpawnApplySystem._deadSlotQuery`
  ([:69-72](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs#L69-L72))
  **must** gain `.WithNone<ProjectileContinuousTag>()`. Without it the discrete job fetches a
  `ComponentTypeHandle<ProjectileTrackingComponent>` against chunks that have no such
  component — silent corruption, not a crash. Highest-risk edit in the plan; own acceptance
  criterion in task 005, mechanical test in 008.
- **One event queue, two command lists.** The AOE lanes split at the event level because
  `IntervalChildKind` is known to producers. Sweep's flag lives in a template dereferenced
  *inside* expansion ([ProjectileSpawnExpansionSystem.cs:173](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L173)),
  so splitting at the event level would force producers to know a value they do not have.
  Splitting at `WriteCommand` ([:248](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L248))
  puts it where all four spawn paths already converge. This is the one place the AOE pattern
  deliberately does not map, recorded rather than left implicit.
- **The continuous segment is exact.** Because continuous projectiles never track, velocity direction is
  constant and `Origin → Position` is precisely the ground covered. No curvature error, no
  substepping.
- **Ordering.** `ProjectileContinuousCollisionSystem` carries the same attributes as the discrete
  one, so both lanes' hits land in the same finalize pass and expansion still sees this
  frame's spawn events.
- **Job dependency correctness.** The continuous collision job writes the same four lanes via
  `ParallelWriter` and combines its handle into each `ProducerHandle` plus
  `TargetSpatialHashSingleton.ConsumerHandle`, identical to
  [ProjectileDiscreteCollisionSystem.cs:85-96](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L85-L96).
  Multiple collision systems writing these queues is already the established shape.
- **Allocation rule.** The candidate list is a fixed-size stack array
  (`CollisionConstants.MaxContinuousHitsPerFrame = 16`), insertion-sorted in place. Nothing
  allocated per entity or per frame.
- **Performance budget.** The budget bounds per-entity *work* (`MaxContinuousHitsPerFrame`, keeping
  the nearest candidates when it binds) and never swept *coverage*. This satisfies
  `Docs/coding-standards.md` (Performance Budget Rule) while avoiding the failure mode a
  distance cap would have introduced.
- **Broadphase reuse.** The continuous lane adds no broadphase of its own: it reads
  `TargetSpatialHashSingleton.ProjectileCollisionCells`, the same map the discrete lane
  queries, already built once per frame by `TargetSpatialHashSystem` for all consumers. Its
  query region is the union of the corridor AABB and the projectile's own bounds at
  `Position` — matching the two tests it runs — then expanded by `MaxTargetRadius` exactly as
  the discrete path does, preserving the center-cell-insertion invariant.
- **Reuse over reinvention.** The swept overlap test is a call into the existing
  `CombatCollisionMath.Hit` with `CombatShapeType.Rectangle`. Task 002 adds geometry
  construction only. This is the strongest form of the plan-changes reuse rule: the mechanism
  that already solves "does this rectangle overlap that shape" is used as-is rather than
  paralleled by a continuous-specific narrowphase.
- **Arming.** Both new jobs carry `WithDisabled(typeof(ArmingTag))`. Arming entities are
  excluded from movement, so the first unarmed frame writes `Origin = Position` naturally.
  Spawn apply also seeds `Origin = cmd.Position`, correct regardless of apply/movement order.

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive** (enableable `ContinuousCollisionTag` on the single existing archetype):
- *Resulting data flow:* one archetype, one pool, one expansion output; the collision job
  branches internally or a second job filters on an enabled bit.
- *New concepts/types introduced:* one enableable tag, one `float2` on every projectile, one
  branch in the hot path.
- *Copies/translations added:* none.
- *Long-term cost:* every projectile carries sweep state it mostly does not use **and**
  tracking state that continuous projectiles must never use — the exclusivity rule degrades from a
  structural fact to a convention enforced by a disabled bit. The swept job walks chunks that
  are overwhelmingly discrete. One collision system accumulates two collision models.

**Refactor** (chosen — second archetype and lane):
- *Resulting data flow:* two archetypes with genuinely different component sets, two
  self-contained pools, one event queue fanned out at the single existing convergence point.
- *Existing concepts/types changed:* `ProjectileDiscreteSpawnApplySystem` gains
  `.WithNone<ProjectileContinuousTag>()` and has its materialization extracted into a shared
  utility; `ProjectileSpawnEventSingleton` gains a second command list; `ProjectileDefinition`
  and its compile chain gain the flag. `ProjectileMovementSystem` is **not** changed — the
  continuous lane's extra state is captured by a separate pre-movement system instead.
- *Copies/translations removed or avoided:* no runtime branch between collision models; no
  slot-matching logic; no dead tracking state on swept entities; exclusivity needs no runtime
  guard in the simulation at all.
- *Long-term benefit:* the continuous lane can evolve its own broadphase (a DDA segment walk is
  the obvious next step) and its own per-entity state without touching the discrete path.
  Matches the shape the codebase already uses for impact vs lingering AOE.

**Decision:** choose refactor (lane split).
**Reason:** user-directed, and the sweep/tracking exclusivity makes it the structurally
honest option — the two shapes really do carry different data, so expressing that as one
archetype with disabled components would be modelling a difference as a flag. Strictly the
split adds a data path rather than collapsing one; it is preferred because each path is
single-purpose and it conforms to an existing in-repo mechanism instead of introducing a
second way to express "projectile variant".

## Default Decision Rule

If two representations or data paths describe the same domain concept, refactor toward one
source of truth unless there is a concrete compatibility or migration reason not to.

Applied here: sweep membership is represented **once** at each level and never mirrored —
`ProjectileDefinition.continuousCollision` when authored, `ProjectileSpawnCommand.ContinuousCollision`
in flight, and the archetype tag once materialized. No per-entity "is swept" bool alongside
the tag, no enum, no runtime derivation. Exclusivity likewise has one enforcement point per
layer: `BuildRuntime` at compile, archetype composition at runtime.

## Revision Log

Design changes made after the plan was first written. Implementation is underway, so anything
here may contradict a version of a task file you already read — check this section before
trusting notes taken earlier.

### 2026-08-02 — Lane naming: `Discrete` / `Continuous`

**Tasks 010, 011, 012 (new).** No design change — vocabulary only.

The lanes were named asymmetrically: one was the unnamed default
(`ProjectileCollisionSystem`, `ProjectileSpawnApplySystem`, `Commands`) and the other the
special case (`SweptProjectileCollisionSystem`, `SweptCommands`). That is the same
default-plus-exception shape rejected when this design started. Both lanes now carry an
explicit name.

**The rule:** `Projectile` + `{Discrete | Continuous}` + role, for anything belonging to
exactly one lane. **No lane word** for anything both lanes use — `ProjectileMovementSystem`,
`ProjectileSpawnExpansionSystem`, `ProjectileContactGateSystem`, `ProjectileTag` are unchanged.
The lane word sits *after* `Projectile` so the lane's files stay together in a folder where
everything sorts under `P`, and so `grep Continuous` returns the lane and nothing else.

| Was | Now |
|---|---|
| `ProjectileCollisionSystem` / `SweptProjectileCollisionSystem` | `ProjectileDiscreteCollisionSystem` / `ProjectileContinuousCollisionSystem` |
| `ProjectileSpawnApplySystem` / `SweptProjectileSpawnApplySystem` | `ProjectileDiscreteSpawnApplySystem` / `ProjectileContinuousSpawnApplySystem` |
| `SweptProjectileOriginSystem` | `ProjectileContinuousOriginSystem` |
| `SweptProjectileTag` | `ProjectileContinuousTag` |
| `ProjectileSweepComponent` | `ProjectileContinuousStepComponent` (field `Origin` unchanged) |
| `Commands` / `SweptCommands` | `DiscreteCommands` / `ContinuousCommands` |
| `MaxSweptHitsPerFrame` | `MaxContinuousHitsPerFrame` |
| `sweptCollision` → `SweptCollision` chain | `continuousCollision` → `ContinuousCollision` |
| `SweptProjectileCannotTrack` | `ContinuousCollisionCannotTrack` |

`CombatSweepMath` and its members are **not** renamed. The distinction the rename encodes is
that **`Continuous` names the lane and `Sweep` names the geometry** — swept volumes are
standard vocabulary, the file is pure and lane-agnostic, and
`CombatContinuousCollisionMath` beside `CombatCollisionMath` would read as *the* collision
math with an adjective. Open question 12 records this as cheap to overrule.

Symmetric names do not make the data symmetric: absence of `ProjectileContinuousTag` still
means discrete, and the `WithNone` on the discrete dead-slot query stays. Adding a real
`ProjectileDiscreteTag` was considered and rejected — open question 13.

Note the rest of this plan still reads in the old vocabulary; task 012 does that pass, and the
Revision Log entries below deliberately keep the old names because they describe code that no
longer exists.

### 2026-08-02 — Corridor-only box; continuous check runs on top of discrete

**Tasks 002, 007, 008.**

The box was specified as extending past each endpoint by the shape's extent along travel
(`dist/2 + alongSupport`), and as the swept lane's *only* overlap test. Both were wrong. The
box is the **travel corridor only** — centre to centre, no end caps — and the swept lane runs
the ordinary discrete test **in addition to** it.

| Was | Now |
|---|---|
| `boxHalfExtents.x = dist * 0.5 + alongSupport` | `boxHalfExtents.x = dist * 0.5` |
| `BuildSweptBox`, degenerate step → falls back to the projectile's own shape | `TryBuildTravelCorridor`, degenerate step → returns `false`, corridor skipped |
| One overlap test per candidate (corridor only) | Two: discrete at `Position`, then corridor; hit on either |
| Broadphase query = corridor AABB | Broadphase query = union of corridor AABB and `collision.BoundsMin/Max` |
| `SupportExtent` called twice (along + perpendicular) | Called once (perpendicular only) |

Coverage is complete across ticks without end caps: this tick's discrete test covers the
footprint at `Position`, last tick's covered `Origin`, and the corridor covers the gap between.
Extending the box would have duplicated coverage the discrete tests already provide and
widened the broadphase query for nothing.

Known edge, documented in 002 and not worth handling: on the spawn frame there is no previous
tick, so a target overlapping the spawn point *behind* the travel direction is untested. This
is identical to the discrete lane's existing behavior — spawn apply runs after collision, so a
fresh projectile's first test is also at its post-move position.

New guard in 008: a target overlapping the projectile only at its end-of-frame position, beside
the corridor. Deleting the discrete branch would drop those hits while every tunneling test
still passed.

### 2026-08-02 — Movement is shared; origin capture is its own system

**Task 006.** Renamed `006-swept-movement-system.md` → [`006-sweep-origin-capture.md`](006-step-origin-capture.md).

Previously specified a `SweptProjectileMovementSystem` duplicating `ProjectileMovementSystem`.
Movement is identical in both lanes — the only difference was one bookkeeping write
(`sweep.Origin = kinematics.Position`), which is collision input, not a motion model. The old
shape produced two files identical except for query attributes and one line, including
duplicated ordering attributes: reorder movement in one and not the other and the lanes
silently integrate at different points in the frame.

| Was | Now |
|---|---|
| `SweptProjectileMovementSystem` duplicating integrate + bounds | **deleted** |
| `ProjectileMovementJob` gains `WithNone<SweptProjectileTag>` | **reverted** — unchanged from pre-feature, serves both archetypes |
| — | new `SweptProjectileOriginSystem`, `UpdateBefore(ProjectileMovementSystem)`, writes `Origin` only |

Removes the double-integration footgun entirely — there is no exclusion left to forget. **The
`WithNone<SweptProjectileTag>` on `ProjectileSpawnApplySystem._deadSlotQuery` (task 005) is
unrelated and still required.**

Also touched: `001` (lifecycle comment), `005` (origin-seed rationale), `008` (test rationale
plus a new capture-ordering test), `009` (file list).

### Earlier revisions

| Change | Tasks touched | Why |
|---|---|---|
| Membership became **authored** (`sweptCollision` flag), not derived from speed | 001, 003, 004 | Speed is support-modifiable; deriving the lane would make content behavior depend on a computation authors cannot see. Removed `ProjectileSweepPolicy` and all threshold math from the runtime. |
| Sweep and tracking made **mutually exclusive** | 003, 005 | Swept archetype drops `ProjectileTrackingComponent` entirely, so the tracking jobs cannot match it. Exclusivity is structural, not a filter. |
| Conflict resolution: silent "sweep wins" → **error + reject the spawn** | 003 | A homing support socketed into a swept skill quietly doing nothing is worse than failing loudly. Compiler marks `SpawnBlocked`; `SkillDriver` refuses to fire and refunds. |
| Swept volume: swept **bounding circle + time-of-impact** → **oriented box** | 002, 007 | Tighter (silhouette width, not bounding radius) and cheaper — the box is a `CombatShapeType.Rectangle`, so overlap reuses `CombatCollisionMath` and adds no narrowphase code. Deleted three quadratic solvers. |
| Hit ordering: true first contact → **closest approach along the segment** | 002, 007 | Four-op projection instead of per-shape solvers; same near-to-far order in all but pathological cases. |
| `MaxSweepDistance` cap **removed**, config singleton deleted | 001, 007, 009 | A hitch produces the longest step, which is when tunneling is most likely — exactly when the clamp would have stopped testing. Cost belongs to cell enumeration, not coverage. |
| Support extents **not cached** at spawn | 002 | Lifetime-constant and cacheable, but the saving is <0.1 ms at 10k projectiles against two more fields to seed on every pooled reuse, and it would turn "rotation never changes" into a correctness dependency of stored data. |
| Broadphase wording clarified — **existing spatial hash**, no new structure | 007, index | The swept lane reads `TargetSpatialHashSingleton.ProjectileCollisionCells`, same as the discrete lane. Dropped "swap the broadphase" as an open question; remedies (segment-walk enumeration, or the coarser `AoeOccupiedCells`) both live inside the existing hash family. |

## Task List

| # | Task | Depends on |
|---|---|---|
| 001 | [Continuous archetype types](001-continuous-types.md) | — |
| 002 | [Corridor geometry](002-corridor-geometry.md) | 001 |
| 003 | [Authoring flag and sweep/tracking exclusivity](003-authoring-flag-and-exclusivity.md) | 001 |
| 004 | [Expansion command fan-out](004-expansion-command-fanout.md) | 001, 003 |
| 005 | [Spawn apply lane split](005-spawn-apply-lane-split.md) | 001, 004 |
| 006 | [Step origin capture](006-step-origin-capture.md) | 001, 005 |
| 007 | [Continuous collision system](007-continuous-collision-system.md) | 002, 006 |
| 008 | [Tests](008-tests.md) | 007 |
| 009 | [Docs update](009-docs-update.md) | 007 |
| 010 | [Lane naming: simulation](010-lane-naming-simulation.md) | 001–009 |
| 011 | [Lane naming: authoring chain](011-lane-naming-authoring.md) | — |
| 012 | [Lane naming: tests and docs](012-lane-naming-tests-and-docs.md) | 010, 011 |

## Open Questions, Dependencies, Considerations

**Resolved by the author:**

1. **Sweep + tracking → error event, reject the spawn.** Not a silent degrade. Detected in
   `SkillSetCompiler.BuildRuntime`, marks the definition `SpawnBlocked`, and `SkillDriver`
   refuses to fire the slot and refunds the cast. Task 003. One implementation detail I chose
   and am flagging: the refund is raised locally in `SkillDriver` rather than round-tripping
   an `ExternalSpawnRequest` into ECS purely to be rejected, since the conflict is fully known
   at compile time and the existing `SpawnRejectedEvent` lane exists for *dynamic* rejections
   like insufficient mana. Player-visible result is identical either way.
2. **Existing skills are not retrofitted.** `MagicBolt` (speed 30) and `MagicBolt2` (25) do
   cross the tunneling threshold and stay as they are — a known, accepted, authored state.
   Skills will be authored for sweep deliberately.
3. **The advisory warning is scoped to tracking-capable projectiles only.** That is the
   combination with no fix available: tracking forbids sweep, so the author cannot resolve it
   by ticking the flag and must slow the projectile or drop the homing. A fast *non*-tracking
   projectile is not warned — its fix is obvious and per (2) that is an authoring decision.
   No current content trips it; the check is forward-looking.
4. **The tunneling math is a compile-time check, not a runtime one.** "Can the projectile
   travel two ticks without overlapping what it could have hit" — two ticks so one long or
   dropped frame is covered. Runs once per compiled skill in `SkillSetCompiler`, never in ECS
   and never per spawn. Its target-size input is an editor-side lint constant that must not be
   reachable from `PlayGround.Sim`. Full formulation and the content table in
   [003](003-authoring-flag-and-exclusivity.md).

**Nothing open.**

Broadphase cost at extreme speeds was previously listed here as an open question. It is not a
decision — the swept lane reads `TargetSpatialHashSingleton.ProjectileCollisionCells`, the
same map the discrete lane already queries, and if the cell walk ever gets hot both remedies
live inside that existing hash family (visit cells along the segment instead of the full AABB
rectangle, or query the coarser `AoeOccupiedCells`). Recorded as a note at the end of
[007](007-continuous-collision-system.md); revisit only when a real fast skill exists and the
profiler shows it.

**Decided by default** (cheap to flip now, expensive later):

6. **Multi-hit along the sweep is ordered nearest-first, always.** Not configurable — pierce
   already exists, so a non-piercing swept projectile must hit the *nearest* target.
7. **On expiry mid-sweep, `kinematics.Position` snaps to the final impact point**, so render
   and on-hit child spawns land on the target. A surviving projectile keeps its full
   end-of-frame position.
8. **The swept box is never clamped**, and nothing in the swept path alters movement.
9. **Sweep is not support-modifiable** — no `SkillStat`, no behavior-context setter. Making it
   modifiable would reintroduce runtime-variable lane membership.
10. **The box's support extents are recomputed every tick, not cached at spawn.** They are
    lifetime-constant and could be stored, but the saving is under 0.1 ms at 10k projectiles
    while the cost is two more fields to seed correctly on every pooled reuse — and it would
    turn "rotation never changes" from an exactness claim into a correctness dependency of
    stored data. Pure recomputation fails loudly if that invariant ever breaks; a cache fails
    silently. See [002](002-corridor-geometry.md).
11. **`Instant` (hitscan) remains reserved, not built.** Nothing here blocks adding it as a
    third lane; the naming leaves the word free — and the `Projectile{Lane}Role` convention
    from task 010 extends to it directly (`ProjectileInstantCollisionSystem`).
12. **`CombatSweepMath` keeps its name.** `Continuous` names the lane; `Sweep` names the
    geometry. Renaming it to `CombatContinuousCollisionMath` would put an adjective on the
    file sitting beside `CombatCollisionMath`, and "swept volume" is standard geometry
    vocabulary rather than this codebase's lane vocabulary. Flip it by renaming one file, one
    class, and its test file — cheap now, cheap later. See [010](010-lane-naming-simulation.md).
13. **No `ProjectileDiscreteTag`.** The names become symmetric; the data stays asymmetric —
    absence of `ProjectileContinuousTag` means discrete. A real discrete tag would add a
    component to the highest-count archetype in the game, change every discrete query, and
    change the pooled archetype (invalidating existing slots), all to remove one `WithNone`.
    Recorded so it is not re-derived after seeing the symmetric names.
