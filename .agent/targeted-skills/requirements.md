---
name: targeted-skills-requirements
description: High-level requirements for targeted (no-physics) skills — single-hit and interval-ticking variants, chain propagation
---

# Targeted Skills — Requirements

Source: `Docs/todo.md` → *"targeted skills. no physics, single hit / interval ticking
variation used for something like chain lightning."*

Requirement spec, not an implementation plan. All twelve decisions raised during specification
are locked in [§13.1](#131-locked); nothing is open. Ready for `/plan-changes`.

---

## 1. What

- New third combat domain: **Targeted**, beside `Projectile` and `Aoe`.
- Picks victims by broadphase query. No travel, no sweep, no area volume.
- Not fakeable with existing pieces:
  - A zero-lifetime AOE hits *everything* in a circle; a chain hits *N nearest, each from
    the previous one's position*. Different selection rule, not a parameter.
  - A homing+pierce projectile approximates it but pays movement, collision, sweep, and
    render cost per link, and its timing depends on travel speed.
- Fills the `LineSegment` VFX lane (`VfxDataShape.LineSegment`), which is already wired
  end-to-end — request struct, bucketing job, GPU buffers, dispatch — with **no gameplay
  producer** today. Only `CombatVfxPreviewDriver` uses it.
- Listed under **needed for poc**, so scope stays tight.

## 2. Variants

The two variants mirror impact vs lingering AOE at **every** level — skill type, definition,
spawn kind, spawn event, expansion, apply, pool, and resolve system. Decisions 4 and 9.

| | Single hit | Interval tick |
|---|---|---|
| Condition | `lifetimeSeconds == 0` | `lifetimeSeconds > 0` |
| Skill SO | `TargetedSkill` | `LingeringTargetedSkill` |
| Definition | `TargetedDefinition` | `LingeringTargetedDefinition` |
| Spawn kind | `IntervalChildKind.Targeted` | `IntervalChildKind.LingeringTargeted` |
| Pool tag | *(absent)* | `LingeringTargetedTag` |
| AOE analogue | `AoeSkill` / impact AOE | `LingeringAoeSkill` / lingering AOE |

- **Single hit** — one chain walk, then expire and return to pool. With
  `chainDelaySeconds > 0` the instance survives until the walk finishes, not one update.
- **Interval tick** — one chain walk started per `tickIntervalSeconds` until lifetime ends.
  Walks started = `floor(lifetime / interval) + 1`.
- Both obey the spawn timing rule: an instance created this frame resolves next update.
- `armSeconds` windup reuses the existing `ArmingTag` pause overlay. Lifetime does not tick
  while arming, matching `CombatLifetimeSystem`'s existing `WithDisabled<ArmingTag>` jobs.
- **Chaining is not a variant axis.** `maxTargets == 1` and `maxTargets > 1` share one
  archetype within each variant; only `lifetimeSeconds` splits archetypes.

## 3. Targeting And Chain Walk

- Link 0: nearest eligible **centre** within `acquireRadius` of the acquisition anchor.
- Link k: nearest eligible centre within `chainRadius` of link k-1's target **position**, not
  its entity — a target that dies mid-walk does not strand the chain.
- Up to `maxTargets` links; stops early when a link finds nothing.
- A link never selects the target the previous link hit; earlier targets **may** be revisited
  (§3.4). Chain length is bounded by `maxTargets`, not by the number of distinct enemies.
- Centre distance only, not shape overlap. Accepted cost of reusing the existing hash.
- Interval variant starts a fresh walk each tick, carrying only the last target key (§3.4).
- **Tie-break is deterministic within a frame** — lowest broadphase index, no RNG, no
  dependence on thread scheduling. It is *not* reproducible across sessions: the index comes
  from `ToEntityArray` in chunk order, which shifts as entities are created and destroyed.
  Within-frame determinism is what the simulation needs; do not claim more in tests.

### 3.1 Origin and anchor are two different points

The chain is drawn **from the thing that cast it**, but aimed **where the player pointed**.
For a root cast those are not the same place, so the spawn carries both:

| | Origin | Acquisition anchor |
|---|---|---|
| Meaning | Where link 0's segment starts, and where the instance spawns. | Where link 0 searches for its first target. |
| Root cast | Caster position. | Cursor / aim world position. |
| On-impact trigger | Impact point. | Impact point. |
| Interval trigger | Source entity position. | Source entity position. |

- Only a root cast makes them diverge. Every internal producer sets both to the same point, so
  the two collapse and cost nothing.
- `SkillSpawnTranslator` already receives `origin`, `aimDir`, and `aimWorldPos` for every cast,
  so both values exist at the managed boundary today — this needs one extra `float2` on the
  targeted spawn path, not new plumbing.
- Neither point follows the caster after spawn. A player who walks away still leaves the chain
  anchored where it was cast.
- Open: should a root cast acquire around the cursor at all, or just around the caster? See
  [§13.10](#13-open-decisions).

### 3.2 The walk owns two positions

The current link has two ends, and the walk state stores **both** rather than deriving one from
the other or overloading a render field:

| Field | Meaning |
|---|---|
| `LinkSource` | Where the current link starts. The origin for link 0, then target k-1. |
| `LinkTarget` | Where the current link ends — the target just hit. Also the chain head. |

- Both live in the chain-state component. Together with the stored origin and acquisition anchor
  that is four `float2`s, which is cheap and leaves every consumer reading a field that means
  exactly what it says.
- Consumers, each taking the one it actually wants:
  - next link's search → `LinkTarget`
  - link VFX segment → `LinkSource` → `LinkTarget`
  - debug sprite → renders on `LinkTarget`, oriented along `LinkTarget - LinkSource`
- **`CombatKinematicsComponent` is a derived mirror, not the source of truth.** After a resolve
  that landed at least one link, the system writes `Position = LinkTarget` and
  `Velocity = LinkTarget - LinkSource` in one place, because that is the pair
  `CombatRenderMatrixUtility.ElementFor` reads. Walk logic never reasons in render terms.
- That mirror is the only coupling to rendering, so changing the sprite anchor later — target,
  source, or midpoint — is a one-line change in one place and touches no walk logic.
- An update that lands no link writes no mirror, so the sprite holds its last pose.
- Movement is a teleport, not interpolated travel. Nothing integrates `Velocity` — targeted
  entities are not in `ProjectileMovementSystem`'s query.
- The **origin is stored separately** from `LinkSource`, because `LinkSource` advances past it
  after link 0 and an interval-variant restart has to return to it.
- Falls out for free: a v2 "spawn an AOE at each hit" trigger reads `LinkTarget` directly.

### 3.3 Eligibility

A candidate must pass both of:

- **Enemy faction** — `target.Faction != self.Faction`, the same single inequality the narrow
  phase already uses.
- **Not `LastTargetKey`** — the target the previous link hit. One key, not a set (§3.4).

That is the whole test. There is deliberately **no liveness check** — see [§3.5](#35-why-there-is-no-dead-target-check).

`LastTargetKey` holds the hashed **target key**, never the broadphase index: indices are rebuilt
every frame, so a walk spanning frames would otherwise exclude the wrong entity. The key is the
same `TargetKey(entity)` hash the collision systems and contact gates already use.

### 3.4 One remembered target, not an exclusion set

The chain remembers exactly **one** target key, and it does two jobs at once:

- **Within a walk** — it stops link k+1 selecting link k's own target. Without it the walk is
  degenerate: link 1 searches from target 0's position and the nearest centre to that position
  is target 0 itself, at distance zero, so the chain would hit one enemy `maxTargets` times.
- **Across an interval re-walk** — it is *not* cleared when a tick restarts the walk, so link 0
  of the new walk skips the enemy the previous walk ended on. A ticking chain moves around
  instead of reading as a stuck beam. That is decision 2, and it needs no separate field.

Consequences, all intended:

- **A chain may revisit an earlier target.** With two enemies in range and `maxTargets = 6`, the
  walk goes A→B→A→B→A→B — six links across two enemies, each at its falloff step. Chain length
  is bounded by `maxTargets`, not by how many distinct enemies are nearby.
- **Fallback is automatic.** With a single enemy in range, link 0 of a re-walk finds nothing
  else and hits it anyway, so a lone target still takes every tick.
- Storage is one `int`. No `FixedList`, no `DynamicBuffer`, no per-walk clear beyond a single
  assignment — and no way to overflow it.

### 3.5 Why there is no dead-target check

A chain can link an enemy that other damage has already lethally hit this frame. That is not
fixable at resolve time, and the spec does not try:

- Damage is aggregated and applied in `CombatApplyFinalizeSingleSystem`, which runs **after**
  every collision and resolve system. During a resolve, `Health.Current` still reflects the end
  of the *previous* frame, so a target that ten other sources have already queued lethal hits
  against still reads as full health.
- This is not an oversight in the resolve — it is ADR-006's aggregation model working as
  designed. Reading `Health` mid-frame would give a stale answer at a real cost
  (`ComponentLookup` random access per candidate) and still not prevent overkill.
- Projectiles and AOEs already behave this way. Chains are consistent with them.

Accepted consequence: chains can waste links on targets that are about to die. If that ever
becomes a visible problem, the fix belongs in **target-proxy lifetime**, not here — an actor
that deregisters its proxy promptly on death removes the candidate from the spatial hash for
every consumer at once, instead of every consumer separately re-deriving liveness.

### 3.6 Multiplicity — several chains from one cast

`count` is this domain's version of projectile `Count` and AOE `EchoCount`, and it takes the
identical path: **spawn request → spawn event → expansion explodes multiplicity → one command per
entity.** No targeted-specific mechanism, no new plumbing — the contract in
`Docs/contracts/spawn-events-and-commands.md` already describes exactly this.

Two knobs, easy to confuse:

| Field | Meaning |
|---|---|
| `maxTargets` | Links within **one** chain. One entity walking A→B→C→D. |
| `count` | How many **separate** chains one cast creates. N independent entities. |

`count = 3, maxTargets = 4` is three forks of up to four links each — up to twelve hits.

**There is no scatter.** A targeted skill has no position to fuzz — it picks a target and hits
it. Projectile `spreadDegrees` and AOE `scatterRadius` exist because those domains place a shape
in the world; a chain places nothing. So neither the definition nor the interval trigger carries
a scatter field, and expansion does not displace anything per instance.

**Forks differ by acquisition rank, not by position.** Selection is deterministic nearest-first,
so without something to separate them N chains would pick the same first target and walk the same
path — N chains rendering and damaging as one. Instead, expansion stamps each command with its
instance index `i`, and **link 0 of fork `i` selects the `i`-th nearest eligible target** rather
than the nearest. Fork 0 takes the closest enemy, fork 1 the next, fork 2 the one after.

- No randomness, no position fuzz, no new field on the skill — just the instance index expansion
  already computes when it fans commands.
- Links 1..n are unaffected. Only the entry point differs; after link 0 every fork chains
  normally by nearest-from-current.
- **Fallback:** if fewer than `i+1` eligible targets are in `acquireRadius`, fork `i` wraps to
  rank `i mod eligibleCount`. With one enemy and `count = 3`, all three forks hit it — the same
  stacking behaviour three projectiles into one target already produce.
- Forks stay independent entities with their own `LastTargetKey`. Nothing coordinates them after
  link 0, so they can converge on the same target later in their walks. Accepted —
  cross-instance coordination is what the parallel-resolve design avoids.
- Rank selection costs nothing extra: the radius query already visits every candidate to find the
  nearest, so tracking the `i`-th nearest is the same scan with a small fixed-size running set.
- Per-instance `SourceId`, deterministic tick index, and jitter seed are derived in expansion from
  the event's seed plus the instance index, the same way projectile and AOE fan-out already does.
- `count` multiplies concurrent walking instances, so it feeds directly into the scene-level
  budget concern in §10 — `count` on an interval-triggered chain is the sharpest multiplier this
  feature can produce.

> **Flagging this one.** "No scatter" was the instruction; rank-offset is my inference for how
> forks then differ. The alternative is that `count > 1` produces genuinely identical chains and
> is purely a damage multiplier on the same victims. That is coherent and simpler, but it makes
> `count` and `damage` the same lever with extra entities, so rank-offset is spec'd. Say if you
> want the simpler behaviour.

### 3.5 Per-link delay

- `chainDelaySeconds` staggers the walk: link k fires `chainDelaySeconds` after link k-1.
- **`chainDelaySeconds == 0` resolves every link in one tick.** This is not a special case
  in the code — one drain loop covers both:

  ```text
  linksThisUpdate = 0
  linkGate -= dt

  while linkIndex < maxTargets and linkGate <= 0 and iterations < maxTargets:
      // link 0 searches around the acquisition anchor; later links search from the head
      searchFrom = linkIndex == 0 ? acquireAnchor : linkTarget
      radius     = linkIndex == 0 ? acquireRadius : chainRadius
      pick nearest eligible target within radius of searchFrom   // exit walk if none

      linkSource = linkTarget          // previous head becomes this link's tail
      linkTarget = target centre       // new head
      lastTargetKey = target key       // the whole exclusion state; survives walk restarts

      emit hit       (DamageScale = pow(falloff, linkIndex))
      emit link VFX  (linkSource -> linkTarget)   // source -> target, one per link

      linkIndex++
      linksThisUpdate++
      linkGate += chainDelaySeconds     // += 0 keeps the gate open, loop drains fully
      iterations++

  if linksThisUpdate > 0:
      // the one place walk state meets render state
      kinematics.Position = linkTarget
      kinematics.Velocity = linkTarget - linkSource
  ```

  `linkTarget` is seeded to the origin at spawn, so link 0's `linkSource = linkTarget` assignment
  correctly starts the first segment at the caster.

  Note the asymmetry on link 0: the VFX segment is drawn from the origin (the caster) while the
  search is centred on `acquireAnchor` (the cursor). Every later link draws and searches from
  the same point.

- Iteration cap per update is `maxTargets`, mirroring the bounded catch-up the timed-spawn
  loop already uses.
- At low frame rate (`dt > chainDelaySeconds`) several links fire in one update. Correct and
  consistent with `TimedSpawnSystem` catch-up.
- With delay > 0 the walk samples target positions **at each link's own time**, so moving
  targets change where the chain goes. That is the intended difference from delay 0.
- A walk in flight when the next interval tick fires is **restarted**, not overlapped: link index
  reset and position returned to the stored origin. `LastTargetKey` deliberately survives the
  restart (§3.4). Keeps the tick rhythm predictable and the per-entity state fixed-size; the
  reasoning is worked through in [§13.3](#133-answering-decision-8--mid-walk-tick-collision).

## 4. Damage

- Link k damage = `Damage * pow(chainDamageFalloff, k)`.
- Rides the existing `CombatHitPayload` → finalize → `CombatTickResult` path. Crit rolled
  per link, unchanged.
- Presentation, damage numbers, and health bars need no targeted-specific code.
- Stacks apply on every link, **unscaled** by falloff (detonation damage already sums
  contributions; scaling both double-dips).

## 5. Visual

### 5.1 Sprite (debug affordance)

**The sprite is a debugging and authoring aid, not the shipped visual.** The `LineSegment` VFX
in [§5.2](#52-link-vfx) is what players see. Shipping content is expected to author no sprite at
all; the sprite exists so a chain's source, direction, and timing are visible while tuning.

That framing decides two things:

- **No stretching.** The sprite keeps its authored size and merely faces the target. A sprite
  that spanned source → target would have to anchor at the segment **midpoint** with
  `VisualScale.x` set to the link length, because the render quad is centred on `Position` —
  which contradicts rendering at the source, and duplicates what the `LineSegment` VFX already
  does better.
- **Not worth extra machinery.** Everything below reuses existing render components and the
  existing matrix path. If a requirement here would need a new render system, drop the
  requirement instead.

Behaviour:

- A sprite is **optional**. Supplied → the instance renders through the existing batched sprite
  path. Not supplied → `RenderTypeId = 0`, which `CombatRenderMatrixUtility.ElementFor` already
  treats as a degenerate (invisible) instance. VFX-only chains are the normal case.
- When supplied, the sprite renders **on the hit target, oriented along the link that reached
  it**. That is the derived mirror from [§3.2](#32-the-walk-owns-two-positions):
  - `CombatRenderComponent.AlignToVelocity = 1`
  - `kinematics.Position` = `LinkTarget`
  - `kinematics.Velocity` = `LinkTarget - LinkSource`
- **This needs no new render machinery.** `ElementFor` already normalises `Velocity` and
  composes it with the authored base rotation when `AlignToVelocity` is set — the same
  mechanism projectiles use to face their travel direction.
- **`Velocity` on a targeted entity is a render direction, not motion.** Nothing integrates it;
  targeted entities are absent from `ProjectileMovementSystem`'s query. Any future system that
  reads `Velocity` across domains must require a domain tag, per the existing rule.
- The walk never reads these back. `LinkSource`/`LinkTarget` remain authoritative, so a wrong or
  stale mirror can only ever be a visual bug, never a gameplay one.
- Multiple links resolved in one update → the sprite lands on the **last** target hit, oriented
  along that last link. With `chainDelaySeconds == 0` that is the final target of the whole
  walk; intermediate hops are not drawn, which is fine — the `LineSegment` VFX shows them all.
- An update where the delay gate has not expired leaves position and velocity untouched, so the
  sprite holds its last pose rather than snapping to a default facing (a zero `Velocity` would
  make `ElementFor` fall back to facing `+X`).
- Sprite size is always the authored size — no stretching, no scaling by link length.

**Rendering is the same path projectiles and AOEs use** (decision 13), with no targeted-specific
handling anywhere:

- `TargetedTag` is added to `CombatBatchedRenderSystem.renderQuery`'s `WithAny`. Both variants
  carry that tag, so one addition covers them; `LingeringTargetedTag` is a pool discriminator and
  the render query never looks at it.
- Render components stay on the archetype unconditionally rather than being split into a separate
  renderable archetype, and no `TargetedRenderableTag` is introduced.
- The query uses `IgnoreComponentEnabledState`, so pooled (disabled) targeted entities sit in the
  instance buffer as degenerate quads, exactly as pooled projectiles and AOEs already do. Known
  and accepted: it means a VFX-only chain still occupies an instance slot. That is the existing
  cost model for every domain, not a new cost this feature introduces, and matching it is worth
  more than shaving slots off one domain.

### 5.2 Link VFX (the shipped visual)

- **One `LineSegmentVfxSpawn` per link, always — source → target.** Start = the instance's
  position before the hop, end = the hit target's centre. A `maxTargets = 6` walk that lands
  6 links emits 6 separate segments — never one polyline for the whole chain, and never one
  segment for the first link only.
- Link 0's segment starts at the **origin**: caster → first target for a root cast, impact
  point → first target for an on-hit trigger. The chain is always visibly attached to whatever
  produced it.
- Later segments run target k → target k+1, joined end-to-end with no gaps.

  ```text
  caster ──▶ target0 ──▶ target1 ──▶ target2
     seg0        seg1        seg2
  ```
- Optional circular impact flash per hit target.
- With `chainDelaySeconds > 0`, links emit across several frames, so the arc visibly travels.
  Per-frame segment count is no longer the whole chain.
- A link that finds no target emits nothing — no dangling segment into empty space.
- VFX is visual only, never authoritative for damage.

## 6. Authoring

- New `SkillDefinitionTags.Targeted`; `Any` widens to `Projectile | Aoe | Targeted`.
- **Authoring mirrors projectiles and AOEs**: the definition is `prefab` + `behavior`, exactly
  like `ProjectileDefinition` (`BasicAttackPrefab`) and `AoeDefinition` (`BasicAoePrefab`).
- **Two skill types, mirroring `AoeSkill` / `LingeringAoeSkill`** (decision 4). Both derive
  `TargetedSkillBase`, the way both AOE skills derive `AoeSkillBase`. Menu paths
  `PlayGround/Skills/Targeted Skill` and `.../Lingering Targeted Skill`.

```text
TargetedDefinition                     (single hit)
 ├─ prefab:    TargetedPrefab   → sprite, material, VFX assets. No hurtbox.
 └─ behavior:  damage, manaCost, directDamageEnabled,
               count,                                   ← multiplicity (§3.6)
               acquireRadius, maxTargets, chainRadius,  ← one chain's reach and length
               chainDamageFalloff, chainDelaySeconds, armSeconds

LingeringTargetedDefinition            (interval tick)
 ├─ prefab:    TargetedPrefab
 └─ behavior:  ...all of the above, plus
               lifetimeSeconds, tickIntervalSeconds
```

`count` fills the role `AoeDefinition.echoCount` and `ProjectileDefinition.count` fill, named
`count` because the copies are independent chains, not echoes of one shape. It has **no** paired
spread or scatter field — those exist for domains that place a shape in the world (§3.6).

- Following the AOE precedent, the single-hit definition does **not** expose `lifetimeSeconds`
  or `tickIntervalSeconds`; the runtime receives `0` for both. Exactly how `AoeDefinition`
  compiles as a pulse.
- Both share one `TargetedPrefab` type — the prefab carries no timing, so it needs no split.
- `RuntimeTargetedDefinition` covers both, with `LifetimeSeconds == 0` selecting the
  single-hit lane, mirroring `AoeVariant.AoeChildKindFor(lifetimeSeconds)`.

### 6.1 Radius stats

- `acquireRadius` and `chainRadius` fold through the existing **`AreaSize`** stat (decision 5),
  so the player's `areaSizeMultiplier` scales a chain's reach with no new stat kind.
- `IncreasedAoeSupport` and `ConcentratedEffectSupport` add `Targeted` to their
  `SupportedSkillTags` and then work unchanged.
- Both radii scale together — one stat drives them. Accepted; a build that wants longer jumps
  but not a wider initial grab has no way to express that in v1.

### 6.2 `TargetedPrefab`

Follows `BasicAoePrefab`'s shape, minus collision:

- **`Visual` child `SpriteRenderer` — optional.** Same rule `BasicAoePrefab` already uses: if a
  sprite renderer with a sprite is present it must be on a child named `Visual`, and its
  material must be non-null, textured, GPU-instanced, and on a supported shader. Absent or
  sprite-less → the skill is VFX-only.
- **No `Hurtbox` child, and none is baked.** A targeted skill resolves by query, not by
  geometry, so there is no collision shape to extract. `TargetedPrefab` exposes no `Radius`,
  `HalfExtents`, `RotationRadians`, or `ShapeType`, and the targeted archetype carries neither
  `CombatCollisionComponent` nor `CombatCollisionActiveTag`.
- `IsValidTemplate` **fails** if a `Hurtbox` child is found — it means the prefab was copied
  from a projectile or AOE template and the physics assumption came with it.
- VFX asset slots follow `BasicAoePrefab`'s pattern (asset + `VfxDataShape` per slot): a **link**
  effect that must be `VfxDataShape.LineSegment`, plus optional impact / spawn / expire /
  arming effects.
- Registration reuses the existing path: `CombatRoot` registers the sprite and returns a render
  id, and registers each VFX asset through `CombatVfxRoot`. Nothing targeted-specific.
- All numerics fold through the shared `StatFold`. No new stat kinds.
- `AddedDamageSupport` and `IncreasedRateSupport` work free once `Any` widens.
- Projectile-only supports stay projectile-only: no-op + warning on a targeted set.
- **`MultipleChainsSupport`** — the third member of the `MultipleProjectilesSupport` /
  `MultipleAoesSupport` family, adding `count` and modifying `ManaCost` through the same three
  `IManaModifiers` interfaces. Unlike its two siblings it contributes no geometry field, because
  there is none (§3.6). Ships in v1: multiplicity without a support to scale it would leave
  targeted skills outside the build lever both other domains have.
- Chain **length** (`maxTargets`) still has no support in v1 — it is authored on the skill.
  That is the deferred one, not multiplicity.

## 7. Wiring

- **Player-cast root** — existing gate: `SkillDriver` → `SkillSpawnTranslator` →
  `CombatRoot` → `ExternalSpawnRequest` → mana deduction. Rejection refunds cooldown.
- **`OnImpactTargetedTrigger`** (new) — projectile or AOE hit fires the chain from the
  impact point.
- **`TargetedIntervalSpawnTrigger`** (new) — energy-accrual child from a projectile or
  lingering AOE. Pulse AOE source → warning, no-op. Inherits `IntervalSpawnTrigger` and carries
  one field, `count`, **additive** with the child definition's own count and floored to 1 —
  matching how `AoeIntervalSpawnTrigger.echoCount` and
  `OnImpactProjectileTrigger.spawnCount` add to their children.
- It carries **no** geometry field. `AoeIntervalSpawnTrigger` pairs `echoCount` with an
  authoritative `scatterRadius` and `ProjectileIntervalSpawnTrigger` pairs its count with
  `sideSpreadDegrees`, because both place a shape or a direction. A chain places neither (§3.6),
  so there is nothing for the trigger to own.
- `OnImpactTargetedTrigger` carries **no** fields, mirroring `OnImpactAoeTrigger`. Chain count is
  whatever the effect set authors.
- **`StackTrigger`** — free once `Any` widens; chain becomes a stack applicator.
- Targeted as a trigger **source** is v2, but v1 must not block it.
- Works for any faction; mob casters get it free. Player content is the v1 deliverable.

## 8. Runtime constraints

- Own `TargetedTag`; every targeted system query requires it. Faction from identity, never
  from scope membership.
- A real pooled entity is required even for instant hits — finalize resolves damage through
  `ComponentLookup<CombatHitPayload>[hit.Source]`.
- Reuses shared machinery: `Active` disable-in-place pooling, `CombatKinematicsComponent`,
  `CombatLifetimeComponent`, `ArmingTag`, `CombatHitPayload`, and the canonical
  event → expansion → command → apply spawn path.

### 8.1 Variant split

Decision 9 — mirror the impact/lingering AOE structure exactly:

- `TargetedTag` on both variants; `LingeringTargetedTag` present only on the interval variant,
  the way `LingeringAoeTag` marks lingering AOEs and impact AOEs omit it.
- Two reuse pools, distinguished by that tag. `CombatPoolCleanupSystem` goes from four pools to
  **six** (discrete projectile, continuous projectile, impact AOE, lingering AOE, targeted,
  lingering targeted).
- Two resolve systems sharing one `TargetedResolveCore`, the way `ImpactAoeCollisionSystem` and
  `LingeringAoeCollisionSystem` both call `AoeCollisionCore.RunCollision`. The interval system
  adds the tick gate and the last-target carry-over; everything else is shared.
- The interval variant carries the tick-gate component and `PreviousWalkLastTarget`; the
  single-hit variant carries neither. That component-set difference is what justifies the split
  under the project's own precedent.
- **Chaining does not split anything.** Both variants carry the full walk state regardless of
  `maxTargets`, so a `maxTargets == 1` skill pays for state it does not use. Accepted: splitting
  on that axis too would mean four pools for one feature.

### 8.2 Walk state

- **Per-entity and persistent across frames** once `chainDelaySeconds > 0`: `LinkSource`,
  `LinkTarget`, the stored origin, the acquisition anchor, `LastTargetKey`, link index, and link
  gate remaining. Four `float2`s, two `int`s, one `float`. Same for both variants.
- The walk state is **authoritative**; `CombatKinematicsComponent` is a derived render mirror
  written once per resolve (§3.2). Storing four `float2`s rather than deriving positions from
  render fields is deliberate — it keeps every field's meaning literal and confines
  render coupling to a single assignment.
- **Exclusion is one `int`** (`LastTargetKey`, §3.4) — no set, no buffer, no list. Two earlier
  calls in this doc are superseded:
  - *Reuse `ProjectileContactGateElement`* — wrong on the mechanism.
    `ProjectileContactGateSystem` queries `ProjectileTag`, so it would never age a targeted
    entity's gate, and walk exclusion is not a cooldown anyway.
  - *`FixedList64Bytes<int>` of every target hit this walk* — correct but unnecessary. Only the
    immediately previous target has to be excluded, because the degenerate case the exclusion
    exists to prevent is a link selecting its own source at distance zero.
  - Dropping to one `int` also removes overflow as a concept, removes the per-walk clear, and
    lets `PreviousWalkLastTarget` collapse into the same field.
- **No `Health` lookup in the resolve** (§3.5). The resolve reads only the broadphase snapshot
  arrays and its own chain state — no random-access `ComponentLookup` at all.
- The archetype carries the shared render components (`CombatRenderComponent`,
  `CombatRenderAuthoring`, `CombatRenderKindId`) and `TargetedTag` joins
  `CombatBatchedRenderSystem.renderQuery`, so an authored sprite renders with no
  targeted-specific render code. `RenderTypeId = 0` when no sprite is authored (§5.1).
- The archetype carries **no** `CombatCollisionComponent` and **no** `CombatCollisionActiveTag`.
  There is no hurtbox, no baked shape, and no participation in any collision system.
- The targeted spawn event/command carries **origin and acquisition anchor as two `float2`s**.
  Internal producers set both to the same point; only root casts diverge (§3.1).
- Single-hit with `chainDelaySeconds > 0` must still carry a computed fail-safe lifetime
  (`maxTargets * chainDelaySeconds` plus margin) so a walk that never terminates cannot leak
  a pooled entity.
- `CombatLifetimeSystem` gains targeted jobs, one per variant, matching its existing
  one-job-per-domain shape.
- **Spawn and despawn counts must feed `CombatStatsSingleton`.** The pool-cleanup calm-down
  gate derives despawns as `spawns − Δactive` across the whole scene, so a new entity kind that
  churns without incrementing those counters skews the gate for *every* pool, not just its own.
- **Triggered chains land one frame after their cause.** Apply runs after collision, so a
  projectile impact in frame N creates the targeted entity in N and resolves it in N+1. Identical
  to impact AOEs, so it is consistent rather than a defect — but for delay-0 chain lightning it
  is worth knowing before it gets reported as input lag.
- Reuses the existing tracking spatial hash for radius queries. **Zero new native containers,
  zero new spatial structures.**
- Resolve system slot: after `TargetSpatialHashSystem` and arming, before
  `CombatApplyFinalizeSingleSystem` and before spawn expansion — the AOE collision slot.
- Combines the hash `BuildHandle`, publishes into `ConsumerHandle`, writes lanes via
  `AsParallelWriter`, chains `ProducerHandle` on the main thread. No reaching into other
  systems' fields.
- Lanes are read directly and must throw if missing — a missing lane is a broken world.

## 9. Contract changes

- `SkillDefinitionTags.Targeted` added, `Any` widened. Intended behaviour change: the three
  current `Any` consumers (`StackTrigger`, `AddedDamageSupport`, `IncreasedRateSupport`) now
  accept targeted skills.
- `IntervalChildKind.Targeted` **and** `IntervalChildKind.LingeringTargeted` added (decision 9).
  Audit every existing `switch` over that enum — current sites are
  `ExternalSpawnGateSystem.AppendInternalSpawn`, `TimedSpawnSystem`, `AoeCollisionCore`,
  `ProjectileDiscreteCollisionSystem`, and `CombatRoot`.
- Two new spawn-event structs (`TargetedSpawnEvent`, `LingeringTargetedSpawnEvent`), two lanes,
  two expansion systems, two apply systems, and a `TargetedSpawnTemplate` registry map.
- Expansion follows the documented contract exactly: dereference the event's template key, apply
  the per-instance frame, and explode template multiplicity (`count`, `scatterRadius`, seed) into
  **one command per spawned entity**. Root casts reach it through `ExternalSpawnRequest` and the
  mana gate; internal producers enqueue the event directly. Identical to both existing domains.
- **This takes the identical-spawn-event count from three to five** (decision 7 defers the
  consolidation). That is the largest single piece of debt this feature adds, and it is
  deliberate — recorded in `Docs/todo.md` so the refactor is not lost.
- `CombatHitEvent` gains `float DamageScale` (default `1`), applied before the crit roll.
  Needed because one source entity emits N links at N damages while `CombatHitPayload` is
  one per entity. Rejected alternatives: one entity per link (6-jump chain becomes 6 pooled
  entities per cast); mutating the payload per link (data race under `ScheduleParallel`).
  It is a damage field on a damage contract, so it does not violate the standards rule
  against widening damage events with spawn-routing data.

## 10. Performance

- Burst-compiled, `ScheduleParallel`, no managed allocations, no LINQ, no closure captures.
- Exclusion is one `int` in the chain state — nothing to allocate, clear, or overflow (§8.2).
- The resolve reads only broadphase snapshot arrays and its own chain state. No
  `ComponentLookup`, no random access (§3.5).
- Idle interval frames cost one gate decrement and return — no query work.
- Cost per resolve = `maxTargets × cellsScanned × candidatesPerCell`, bounded by caps rather
  than by trusting authored values:
  - `MaxChainTargets` (suggest 32)
  - `MaxTargetedSearchRadius`
  - `MinTickInterval` (suggest 0.02s)
- **The per-resolve caps do not bound the scene.** `TargetedIntervalSpawnTrigger` on a
  projectile volley can put hundreds of walking chains in flight at once, each running
  `maxTargets` ring scans per tick. That multiplier is sharper than anything AOEs produce, and
  today only the energy threshold throttles it. Needs either a scene-level concurrent-instance
  cap or a measured ceiling documented from a stress test before content leans on it.
- Link VFX volume scales the same way: N concurrent chains × M links per tick into the
  `LineSegment` lane. Budget it with the same stress test.
- Targeted counters into `CombatStatsSingleton`, surfaced via `CombatStatsDisplaySingleton`.
- No `unsafe`. Native/ECS handles disposed by their owner. `ECS Lifecycle:` comments on
  every new component, tag, and buffer.

## 11. Validation

- Clamp + warn: `maxTargets < 1`, radius above cap, `tickInterval <= 0`, `maxTargets` above
  `MaxChainTargets`.
- **Error** on `acquireRadius <= 0` — blocks the spawn instead of firing a no-op.
- Warn on `maxTargets > 1` with `chainRadius <= 0` (chain can never jump).
- Warn on `chainDamageFalloff <= 0` with `maxTargets > 1` (all links after the first deal zero).
- Clamp `chainDelaySeconds < 0` to 0; clamp `count < 1` to 1.
- Warn when `maxTargets * chainDelaySeconds > tickIntervalSeconds` — later links are cut off
  by the next tick restarting the walk, so the authored chain length is never reached.
- Warn when `maxTargets * chainDelaySeconds > lifetimeSeconds` on the single-hit variant —
  the instance expires before the walk finishes.
- Warn on `tickInterval > lifetime` — fires exactly once, ticking silently never appears.
  Same trap class as the interval-spawn threshold-vs-lifetime bug.
- Warn on an interval trigger whose energy threshold exceeds what the source can accrue over
  its lifetime — child never spawns.
- Warn on a missing link VFX or one that does not decode to `LineSegment` shape. This is the
  shipped visual, so its absence means an invisible skill in play — a sprite does not excuse it.
- **Error** when `TargetedPrefab` has a `Hurtbox` child — a physics shape on a non-physics
  skill means the prefab was copied from a projectile or AOE template.
- Warn on the existing `BasicAoePrefab` material rules when a sprite *is* supplied (missing
  material, no texture, instancing disabled, unsupported shader) — reuse, do not re-invent.
- Warn on a pulse-AOE source for `TargetedIntervalSpawnTrigger`.
- Warn on projectile-only supports on a targeted set (existing tag-mismatch path).

## 12. Tests

**EditMode** — chain order and count, chain break past `chainRadius`, empty acquire,
faction filter, per-link falloff, deterministic tie-break, single-hit lifecycle and pool
return, interval tick count, `tickInterval > lifetime` fires exactly once (regression lock),
arming delay, cap clamping without buffer overrun, retarget between ticks.

Delay-specific EditMode:
- `chainDelaySeconds == 0` — all links resolve in **one** update (locks the user-facing rule).
- `chainDelaySeconds > 0` — exactly one link per elapsed delay; link k lands at `k * delay`.
- Catch-up — a single update with `dt > chainDelaySeconds` fires several links, capped at
  `maxTargets` per update.
- Target dies mid-walk — the walk continues from its last position rather than aborting.
- Moving target mid-walk — the next link is chosen from the target's position at that link's
  time, not at walk start.
- Entity position tracks the head — after link k the instance's
  `CombatKinematicsComponent.Position` equals target k's centre, and link k+1's search is
  centred there, not on the anchor.
- Segment count and topology — a walk landing N links emits exactly N `LineSegmentVfxSpawn`,
  link 0's start equals the **origin** and link k's start equals link k-1's end (segments join
  end-to-end, no gaps, no duplicates).
- Origin vs anchor — a root cast with the caster and the cursor in different places draws
  segment 0 from the **caster**, while link 0's target is the one nearest the **cursor**.
- Walk positions — after link k resolves, `LinkSource` equals target k-1 (the origin for k = 0)
  and `LinkTarget` equals target k.
- Render mirror, one link per update — `Position` equals `LinkTarget` and `Velocity` equals
  `LinkTarget - LinkSource`.
- Render mirror, many links per update — `Position` equals the **last** target hit that update,
  not the first, and `Velocity` is that last link's direction.
- Sprite pose, no link this update — position and velocity are unchanged from the previous
  update (no snap to `+X` facing).
- No sprite authored — `RenderTypeId == 0` and the instance contributes no render instance,
  while hits and link VFX still fire.
- No collision — a targeted instance never appears in any collision system's query and carries
  no `CombatCollisionComponent`.
- A walk that finds no target for link k emits k segments, not k+1.
- Walk restart — an interval tick arriving mid-walk resets the link index and returns the
  position to the stored origin, while `LastTargetKey` survives so the new walk does not start
  on the enemy the old one ended on.
- Fail-safe lifetime — a single-hit instance whose walk cannot terminate still expires and
  returns to the pool.
- No self-link — link k+1 never selects link k's own target, even though that target's centre is
  the nearest point to the search origin. This is the degenerate case `LastTargetKey` exists to
  prevent, and without it the chain hits one enemy `maxTargets` times.
- Revisiting is allowed — two enemies in range with `maxTargets = 6` produce A→B→A→B→A→B, six
  links with falloff applied by link index, not by distinct target.
- Last-target carry-over — across two interval ticks with two enemies in range, tick 2's link 0
  hits the *other* enemy, not the one tick 1 ended on.
- Carry-over fallback — with only one enemy in range, every tick still hits it.
- Exclusion keys on target key, not index — a walk spanning frames during which other proxies
  are created or destroyed still excludes the right entity.
- Variant routing — a `lifetimeSeconds == 0` skill materialises in the single-hit pool without
  `LingeringTargetedTag`; a `> 0` skill materialises in the interval pool with it.
- Multiplicity fan-out — `count = 3` produces exactly three commands and three entities from one
  event, each with a distinct `SourceId`, instance index, and jitter seed.
- Fork rank — with `count = 3` and four enemies at increasing distance, fork 0 opens on the
  nearest, fork 1 on the second nearest, fork 2 on the third. No positional displacement is
  applied to any of them.
- Fork wrap — with `count = 3` and one enemy in range, all three forks open on it.
- Trigger multiplicity is additive — `TargetedIntervalSpawnTrigger.count = 2` on a child whose own
  `count` is 3 spawns 5 chains per energy tick.

**PlayMode** — player cast damages a mob with no projectile/AOE entity created; chain across
three mobs from one cast; mana gate rejection refunds cooldown; on-impact trigger fires from
the impact point; interval trigger spawns children from a lingering AOE; stack applicator
detonates at threshold; dispatched `LineSegment` count summed over the walk equals the
resolved link count; 200 casts leave entity count bounded; mob-cast chain hits the player and
never other mobs.

Tests prove gameplay behaviour, not that code ran. No production-only test hooks.

## 13. Decisions

### 13.1 Locked

| # | Decision | Where it lands |
|---|---|---|
| 1 | **Empty cast spends.** Mana is deducted at `ExternalSpawnGateSystem` like every other root cast; only insufficient mana refunds the cooldown. A cast that acquires nothing has already paid. | §7 |
| 2 | **Fresh walk each tick, with a last-target carry-over** so consecutive ticks do not re-zap the same enemy. | §3.4 |
| 4 | **Mirror the AOE SO shape:** two skill types, not one with a variant enum. | §6 |
| 5 | **Radii fold through `AreaSize`**, scaled by the player's area-size stats. | §6 |
| 6 | **No line of sight.** Walls are not implemented in the game yet. | §14 |
| 7 | **Do not consolidate spawn-event structs now.** Add the fourth and fifth, note the debt in `Docs/todo.md`, refactor later. | §9 |
| 9 | **Split single-hit from interval the way impact and lingering AOE are split** — separate tag, separate pool, shared resolve core. Chaining vs non-chaining is *not* split. | §8.1 |
| 10 | **Link 0 searches around the cursor**, captured at cast time. The anchor does not follow the cursor afterwards. | §3.1 |
| 11 | **No dead-target check.** Unsolvable at resolve time — damage aggregates in finalize, which runs after every resolve, so mid-frame `Health` is stale. Chains can overkill, same as projectiles and AOEs. | §3.5 |
| 12 | **Remember one target key, not an exclusion set.** Enough to stop a link selecting its own source; revisiting earlier targets is allowed. | §3.4 |
| 13 | **Same rendering path as projectiles and AOEs.** `TargetedTag` joins `renderQuery`; no targeted-specific render handling, no separate renderable tag, no gizmo path. | §5.1 |
| 3 | **Multiplicity is in v1, via the canonical spawn path** — external/internal spawn → event → expansion explodes `count` into N commands, one command per entity. No targeted-specific mechanism. | §3.6, §9 |
| 14 | **No scatter, no spread, anywhere.** A chain places no shape in the world, so neither the definition, the interval trigger, nor `MultipleChainsSupport` carries a geometry field. Forks differ by acquisition rank instead. | §3.6 |
| — | **No sprite stretching.** Debug affordance at authored size; the `LineSegment` VFX is the shipped visual. | §5.1 |

Decision 9 as written asked about chaining vs non-chaining; the answer picked the AOE-style
split, which is the *variant* axis. Read as: **split on `lifetimeSeconds > 0`, do not split on
`maxTargets > 1`.** Say so if that inverts the intent.

### 13.2 Still open

None. Every decision raised during specification has been made.

### 13.3 Answering decision 8 — mid-walk tick collision

The question only exists for the **interval** variant, and only when `chainDelaySeconds > 0`.
Two independent timers run at once:

- `tickIntervalSeconds` — how often a **new walk starts**
- `chainDelaySeconds` — how long each **link within a walk** takes

Nothing stops the second from outlasting the first. Concretely, with `lifetimeSeconds = 3`,
`tickIntervalSeconds = 0.5`, `maxTargets = 6`, `chainDelaySeconds = 0.2`:

```text
one walk needs 6 x 0.2 = 1.2s to finish
a new walk is due every 0.5s
=> at t=0.5 the first walk has only reached link 2 of 6
```

Three possible behaviours:

| Option | Result | Cost |
|---|---|---|
| **Restart** *(chosen)* | Abandon the in-flight walk, begin again from the origin. The chain never gets past link 2, but ticks land on the authored 0.5s rhythm. | none |
| Skip | Wait for the walk to finish before starting the next. Effective tick rate silently becomes 1.2s, not the authored 0.5s. | none, but the authored number lies |
| Overlap | Both walks run concurrently. Full chains *and* correct rhythm. | N concurrent walk states per entity — breaks the fixed-size model this spec relies on |

Restart is chosen because it never silently changes an authored value, and the authoring mistake
that causes truncation (`maxTargets * chainDelaySeconds > tickIntervalSeconds`) already has a
validation warning in §11. Tune by shortening the chain, shortening the delay, or lengthening
the tick.

## 14. Out of scope (v1)

- Line of sight / wall occlusion.
- Targeted → targeted chaining.
- Targeted acting as an interval energy **source** for child spawns.
- Swept-line beam damage (that's a moving collision volume, not a query).
- Retargeting or steering an in-flight instance toward a moved anchor.
- New AOE shape kinds, ECS sound, damage-number VFX graph — separate todo entries.

## 15. Acceptance criteria

1. A `TargetedSkill` can be authored, slotted, bound to input, and cast — damaging the
   nearest enemy with no projectile or AOE entity created.
1b. `TargetedPrefab` authoring reads like `BasicAttackPrefab`/`BasicAoePrefab`: optional `Visual`
   sprite renderer, VFX slots, and **no hurtbox** — a prefab carrying one fails validation.
1c. With a debug sprite authored, it renders at its authored size **on the hit target** (the last
   one, when several links land in one update), oriented along the link that reached it. With no
   sprite — the expected shipping case — the skill renders nothing and the `LineSegment` VFX
   carries the whole visual.
2. `maxTargets > 1` with enemies inside `chainRadius` and `chainDelaySeconds == 0` damages the
   whole chain in one frame, with falloff applied.
2b. `count > 1` spawns that many independent chains from one cast through the normal
   event → expansion → one-command-per-entity path, fork `i` opening on the `i`-th nearest enemy
   with no positional scatter anywhere in the feature.
3. `chainDelaySeconds > 0` staggers the same chain one link per delay, the instance's position
   lands on each target in turn as it goes, and the walk survives a target dying partway
   through.
4. `lifetimeSeconds > 0` starts a fresh walk per tick for the authored duration, at the count
   in §2, each walk restarting from the stored anchor.
5. Every landed link emits its own `LineSegment` VFX at that link's own time, source → target:
   caster → first target, then target k → target k+1, joined end-to-end.
6. Damage, crit, and death flow through the existing presentation path with no
   targeted-specific presentation code.
7. `StackTrigger` on a targeted set applies stacks to every linked target.
8. `OnImpactTargetedTrigger` fires a chain from a projectile's impact point.
9. `TargetedIntervalSpawnTrigger` spawns targeted children from a lingering AOE.
10. Every targeted system query requires `TargetedTag`.
11. No new persistent native container and no new spatial hash were added; exclusion is a single
    `int` in the chain state and the resolve makes no random-access component lookups.
12. Targeted entities pool by disable-in-place across **two** pools (single-hit and interval,
    split like impact/lingering AOE) and are trimmed by `CombatPoolCleanupSystem`. A walk that
    cannot terminate still expires the entity.
12b. A link never selects the target the previous link hit, and an interval chain does not
    re-hit the enemy it ended the previous tick on unless it is the only one in range.
13. All §11 validation cases surface in `SkillDriver.ValidationWarnings`.
14. All §12 tests pass.
15. Docs updated in the same change: `skill-system.md`, `skill-modifiers.md`,
    `spawn-events-and-commands.md`, `combat-hit-and-tick-results.md`,
    `reference/simulation/index.md`, `folder-structure.md`.
16. The targeted-skills line in `Docs/todo.md` is struck.
