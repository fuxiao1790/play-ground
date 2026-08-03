# 009 — Docs Update

**Depends on:** 007
**Scope:** small

Docs in `Docs/` are design references that must match code after the change. Update only
what this feature actually alters — do not restate rules that already live in an owning doc.

## `Docs/folder-structure.md` — Current Projectile Runtime Map

Add entries alongside the existing projectile files:

- `SweptProjectileSpawnApplySystem.cs`: swept-lane apply; reuse disabled `Active` slots in
  the swept archetype before cold creation.
- `SweptProjectileOriginSystem.cs`: records each swept projectile's step-start position before
  `ProjectileMovementSystem` integrates. Movement itself is shared — there is no swept-lane
  movement system.
- `SweptProjectileCollisionSystem.cs`: swept-lane collision; nearest-first ordered hits
  against the oriented box covering this frame's motion.

(No sweep config file — the swept box always spans the full step; there is no tunable.)

Under the Combat Runtime Map, add:

- `Api/Collision/Narrowphase/CombatSweepMath.cs`: swept-box geometry — shape support extents,
  swept-box construction, and closest-approach parameter. Overlap testing stays in
  `CombatCollisionMath`; this file adds no narrowphase.

## `Docs/contracts/spawn-events-and-commands.md`

The command section currently implies one command list per domain. Amend:

- Note that `ProjectileSpawnCommand` is fanned out by expansion into **two** lists on
  `ProjectileSpawnEventSingleton` — discrete and swept — switching on the authored
  `SweptCollision` flag carried in the command.
- State explicitly that this splits at the **command** level, not the event level, because
  the discriminator is not known to producers (interval and on-hit children resolve speed
  from templates inside expansion). This is the one place the AOE lane pattern deliberately
  does not apply, and the doc should say so rather than leave a reader to infer it.
- Extend the existing Notes/TODOs line about faction-agnostic reuse: reuse is faction- and
  render-kind-agnostic **within an archetype**, and the projectile archetypes are now two.

## `Docs/reference/simulation/ecs-notes.md` — Project Combat Pool Cleanup

That section enumerates the reuse pools ("projectile, impact AOE, and lingering AOE") and
explains how they are distinguished. Update it to four pools, with the swept projectile pool
distinguished by `SweptProjectileTag` — the same way the text already explains
`LingeringAoeTag`. Note that the discrete lane's dead-slot query excludes the tag, since that
exclusion is the load-bearing invariant of the split.

## `Docs/reference/simulation/projectile-system.md`

Add a section covering:

- The two lanes and that membership is **authored** via `ProjectileDefinition.sweptCollision`,
  fixed at skill-compile time, never derived from speed at runtime.
- **Sweep and tracking are mutually exclusive**, enforced structurally (the swept archetype
  has no `ProjectileTrackingComponent`, so the tracking jobs cannot match it) and at compile
  time — the combination is an **error** that blocks the spawn and refunds the cast, never a
  silent drop of tracking. Include the reason a compile check is needed at all:
  `ProjectileBehaviorContext.EnableTracking` lets a support turn tracking on after authoring.
- **Authoring guidance** — when to tick the flag, with the measured numbers: a standard
  `0.1 × 0.15` projectile against the smallest target (Bat, `0.35`) can skip a gap once it
  travels more than `0.8` units in two ticks. Note `MagicBolt` (speed 30) sits past that today
  and is deliberately left as-is.
- **All tunneling math is compile-time.** State plainly that no simulation system computes a
  speed threshold or knows a target size, and that the linter's smallest-target constant is
  editor-side only. A future maintainer's instinct will be to "just check it in the collision
  job"; the doc should pre-empt that.
- The swept volume: an **oriented box** whose half-width is the projectile's silhouette
  support perpendicular to travel and whose length spans the step plus the shape's extent
  along travel at each end. Note that it is exact because both the travel direction and the
  shape's rotation are constant across a step, and that it is tested with the existing
  rectangle narrowphase rather than any new overlap code.
- That the box **always spans the full step** — there is no distance cap, because clamping
  would restore tunneling precisely during the long steps where tunneling is most likely.
  Cost is controlled in the broadphase (cell walk), never by dropping coverage.
- That hit ordering and impact position come from closest approach along the segment, not
  true first contact, and why the difference does not matter for either consumer.
- That `CombatCollisionComponent.BoundsMin/Max` keep "bounds at current position" semantics
  in both lanes.

## `Docs/reference/game-logic/skill-system.md`

Content authors read this doc, not the simulation reference. Document:

- `sweptCollision` as an authored projectile field alongside `trackingEnabled`.
- That the two **cannot** be combined: the skill will not fire, the cast is refunded, and the
  loadout shows an error. Including via a homing support socketed into a swept skill.
- The two validation codes and what each means:
  `SweptProjectileCannotTrack` (error, blocks) and `TrackingProjectileMayTunnel` (warning,
  advisory — a homing projectile fast enough to pass through small targets, which cannot be
  fixed with sweep and needs a lower speed or no homing).

## `Docs/contracts/skill-loadout-editing.md`

If `SkillValidationWarning` gains a `Severity` field, the contract doc covering loadout
editing must record it — an error-severity entry now means "this slot will not fire", which is
a behavior guarantee consumers can rely on, not just display text.

## `Docs/coding-standards.md` — Performance Budget Rule

Add the swept lane to the list of examples, in the existing style: swept projectile collision
caps the per-frame candidate set at `MaxSweptHitsPerFrame`, keeping the nearest candidates
when the cap binds. Note explicitly that the budget bounds per-entity *work* and never the
swept *coverage* — a collision feature whose coverage degrades under load stops doing its job
exactly when it is needed.

## Acceptance Criteria

- Every new file appears in `Docs/folder-structure.md`.
- The command-level (not event-level) split and its reason are recorded in the contract doc.
- The pool-cleanup doc lists four pools and names the discriminating tag.
- No doc restates a rule owned by another doc; cross-link instead.
- `ECS Lifecycle:` comments in code match what the docs describe.

## Not doing

**No new ADR.** This change conforms to ADR-003 (event/command spawn pipeline) and ADR-005
(enableable pooling) rather than deciding anything they do not already cover. The lane split
follows the existing impact/lingering AOE precedent, so it is an application of a decision
already made, not a new one. If review disagrees and wants the two-archetype-per-domain
pattern written up as its own record, that is a separate task.
