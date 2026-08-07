---
name: targeted-skills-requirements
description: Requirements for targeted (no-physics) chain skills — one variant whose lifetime is its walk
---

# Targeted Skills — Requirements

Source: `Docs/todo.md` → *"targeted skills. no physics, single hit / interval ticking
variation used for something like chain lightning."*

**Revised after the first implementation shipped.** The original spec had two variants
(single-hit and interval-ticking) and three timing fields (`lifetimeSeconds`,
`tickIntervalSeconds`, `chainDelaySeconds`) that authors had to reconcile against each other by
hand. That was the main source of skills that compiled fine and silently did nothing. §16 records
what was cut and why. Everything below describes the current design.

---

## 1. What

- Third combat domain: **Targeted**, beside `Projectile` and `Aoe`.
- Picks victims by broadphase query. No travel, no sweep, no area volume.
- Not fakeable with existing pieces:
  - A zero-lifetime AOE hits *everything* in a circle; a chain hits *N nearest, each from
    the previous one's position*. Different selection rule, not a parameter.
  - A homing+pierce projectile approximates it but pays movement, collision, sweep, and
    render cost per link, and its timing depends on travel speed.
- Fills the `LineSegment` VFX lane (`VfxDataShape.LineSegment`), which is already wired
  end-to-end — request struct, bucketing job, GPU buffers, dispatch.

## 2. One variant

There is exactly one targeted archetype, one skill type, one definition, one spawn kind, one
event, one expansion system, one apply system, one pool, and one resolve system.

**A chain's lifetime is its walk.** The instance expires the moment it uses its last chain, or
the moment a link finds no target. Nothing else ends it in normal play.

- `armSeconds` windup reuses the existing `ArmingTag` pause overlay.
- Both the spawn timing rule and arming behave as they do for every other domain: an instance
  created this frame resolves next update, and lifetime does not tick while arming.
- **Chaining is not a variant axis.** `chainCount == 1` and `chainCount > 1` share one archetype.

### 2.1 Repeating chains are composed, not authored

A chain that re-zaps over time is a `TargetedIntervalSpawnTrigger` hung off a projectile or
lingering AOE: the source accrues energy and spawns a fresh chain per tick. That is the same
mechanism every other repeating child uses, and it gives the author one timing concept (the
trigger's energy rate) instead of two that interact.

## 3. Authored shape

Four numbers describe a chain:

| Field | Meaning |
|---|---|
| `echoCount` | How many **separate** chains one cast creates. N independent entities. |
| `chainCount` | How many links **one** chain walks. One entity walking A→B→C→D. |
| `chainDistance` | How far each hop reaches — **link 0 included**. |
| `chainDelay` | Seconds between links. `0` resolves the whole walk in one update. |

Plus the fields every domain has: `damage`, `manaCost`, `directDamageEnabled`,
`chainDamageFalloff`, `armSeconds`, `prefab`.

`echoCount = 3, chainCount = 4` is three forks of up to four links each — up to twelve hits.

**One distance, not two.** An earlier draft had `acquireRadius` for link 0 and `chainRadius` for
later links. Collapsing them costs a build the ability to grab far and chain near, and buys back
a knob that existed only to be confused with its neighbour. If that expressiveness is wanted
later it comes back as one field, not as a variant.

## 4. Targeting and the chain walk

- Link 0: nearest eligible target within `chainDistance` of the acquisition anchor.
- Link k: nearest eligible target within `chainDistance` of link k-1's target **position**, not
  its entity — a target that dies mid-walk does not strand the chain.
- Up to `chainCount` links; the walk ends early when a link finds nothing, and the instance ends
  with it.
- Eligibility is **shape overlap**, not centre distance: a candidate qualifies when the search
  circle overlaps its collision shape, using the same narrow-phase test AOEs use. Ranking among
  qualifying candidates is by centre distance.
- **Tie-break is deterministic within a frame** — lowest broadphase index, no RNG, no dependence
  on thread scheduling. It is *not* reproducible across sessions: the index comes from
  `ToEntityArray` in chunk order, which shifts as entities are created and destroyed. Within-frame
  determinism is what the simulation needs; do not claim more in tests.

### 4.1 Origin and anchor are two different points

The chain is drawn **from the thing that cast it**, but aimed **where the player pointed**.

| | Origin | Acquisition anchor |
|---|---|---|
| Meaning | Where link 0's segment starts, and where the instance spawns. | Where link 0 searches. |
| Root cast | Caster position. | Cursor / aim world position. |
| On-impact trigger | Impact point. | Impact point. |
| Interval trigger | Source entity position. | Source entity position. |

Only a root cast makes them diverge; every internal producer sets both to the same point.
Neither point follows the caster after spawn.

### 4.2 The walk owns two positions

| Field | Meaning |
|---|---|
| `LinkSource` | Where the current link starts. The origin for link 0, then target k-1. |
| `LinkTarget` | Where the current link ends — the target just hit. Also the chain head. |

- Consumers each take the one they want: next link's search → `LinkTarget`; link VFX segment →
  `LinkSource` → `LinkTarget`; debug sprite → renders on `LinkTarget`, oriented along
  `LinkTarget - LinkSource`.
- **`CombatKinematicsComponent` is a derived mirror, not the source of truth.** After an update
  that landed at least one link, the resolve writes `Position = LinkTarget` and
  `Velocity = LinkTarget - LinkSource` in one place, because that is the pair
  `CombatRenderMatrixUtility.ElementFor` reads. Walk logic never reasons in render terms.
- An update that lands no link writes no mirror, so the sprite holds its last pose.
- Movement is a teleport. Nothing integrates `Velocity` — targeted entities are not in
  `ProjectileMovementSystem`'s query.

### 4.3 Eligibility

A candidate must pass both of:

- **Enemy faction** — `target.Faction != self.Faction`.
- **Not `LastTargetKey`** — the target the previous link hit. One key, not a set.

That is the whole test. There is deliberately **no liveness check** — see [§4.5](#45-why-there-is-no-dead-target-check).

`LastTargetKey` holds the hashed **target key**, never the broadphase index: indices are rebuilt
every frame, so a walk spanning frames would otherwise exclude the wrong entity.

### 4.4 One remembered target, not an exclusion set

Remembering the previous target stops link k+1 selecting link k's own target. Without it the walk
is degenerate: link 1 searches from target 0's position and the nearest thing to that position is
target 0 itself, at distance zero, so the chain would hit one enemy `chainCount` times.

Consequences, all intended:

- **A chain may revisit an earlier target.** With two enemies in range and `chainCount = 6`, the
  walk goes A→B→A→B→A→B — six links across two enemies, each at its falloff step. Chain length is
  bounded by `chainCount`, not by how many distinct enemies are nearby.
- Storage is one `int`. No `FixedList`, no `DynamicBuffer`, no per-walk clear, no way to overflow.

### 4.5 Why there is no dead-target check

A chain can link an enemy that other damage has already lethally hit this frame. That is not
fixable at resolve time:

- Damage is aggregated and applied in `CombatApplyFinalizeSingleSystem`, which runs **after**
  every collision and resolve system. During a resolve, `Health.Current` still reflects the end of
  the *previous* frame.
- This is ADR-006's aggregation model working as designed. Reading `Health` mid-frame would give a
  stale answer at a real cost (`ComponentLookup` random access per candidate) and still not
  prevent overkill.
- Projectiles and AOEs already behave this way.

If it ever becomes a visible problem the fix belongs in **target-proxy lifetime**, not here.

### 4.6 Forks differ by acquisition rank

Selection is deterministic nearest-first, so without something to separate them N echoes would
pick the same first target and walk the same path. Expansion stamps each command with its instance
index `i`, and **link 0 of fork `i` selects the `i`-th nearest eligible target**.

- **There is no scatter.** A chain places no shape in the world, so neither the definition nor the
  interval trigger nor `MultipleChainsSupport` carries a geometry field.
- **Fallback:** if fewer than `i+1` eligible targets are in range, fork `i` wraps to
  `i mod eligibleCount`. With one enemy and `echoCount = 3`, all three forks hit it.
- Links 1..n are unaffected; only the entry point differs.
- Forks stay independent entities with their own `LastTargetKey`. Nothing coordinates them after
  link 0 — cross-instance coordination is what the parallel-resolve design avoids.
- Rank selection costs nothing extra: the radius query already visits every candidate, so tracking
  the `i`-th nearest is the same scan with a small fixed-size running set.
- Because a target occupies several hash cells, one scan can encounter it more than once. The
  candidate set dedupes by target index — harmless for a plain nearest search, but it would
  corrupt rank selection if left in.

### 4.7 Per-link delay

`chainDelay` staggers the walk: link k fires `chainDelay` after link k-1. **`chainDelay == 0`
resolves every link in one update.** This is not a special case in the code — one drain loop
covers both:

```text
linksThisUpdate = 0
linkGate -= dt

while linkIndex < chainCount and linkGate <= 0:
    searchFrom = linkIndex == 0 ? acquireAnchor : linkTarget
    pick nearest eligible target within chainDistance of searchFrom   // exit walk if none

    linkSource = linkTarget          // previous head becomes this link's tail
    linkTarget = target centre       // new head
    lastTargetKey = target key

    emit hit       (DamageScale = pow(falloff, linkIndex))
    emit link VFX  (linkSource -> linkTarget)

    linkIndex++
    linksThisUpdate++
    linkGate += chainDelay           // += 0 keeps the gate open, loop drains fully

if linksThisUpdate > 0:
    kinematics.Position = linkTarget
    kinematics.Velocity = linkTarget - linkSource

if linkIndex >= chainCount or (the loop was entitled to run and landed nothing):
    expire        // disable Active, emit the expire VFX
```

`linkTarget` is seeded to the origin at spawn, so link 0's `linkSource = linkTarget` assignment
correctly starts the first segment at the caster.

Note the asymmetry on link 0: the VFX segment is drawn from the origin (the caster) while the
search is centred on `acquireAnchor` (the cursor). Every later link draws and searches from the
same point.

- Iteration cap per update is `chainCount`, mirroring the bounded catch-up `TimedSpawnSystem` uses.
- At low frame rate (`dt > chainDelay`) several links fire in one update. Correct and consistent.
- With delay > 0 the walk samples target positions **at each link's own time**, so moving targets
  change where the chain goes. That is the intended difference from delay 0.

## 5. Damage

- Link k damage = `Damage * pow(chainDamageFalloff, k)`, carried on `CombatHitEvent.DamageScale`.
- Rides the existing `CombatHitPayload` → finalize → `CombatTickResult` path. Crit rolled per
  link, unchanged. Presentation needs no targeted-specific code.
- Stacks apply on every link, **unscaled** by falloff (detonation damage already sums
  contributions; scaling both double-dips).

## 6. Visual

### 6.1 Sprite (debug affordance)

**The sprite is a debugging and authoring aid, not the shipped visual.** The `LineSegment` VFX in
[§6.2](#62-link-vfx-the-shipped-visual) is what players see. Shipping content is expected to
author no sprite at all.

- **No stretching.** The sprite keeps its authored size and merely faces the target.
- A sprite is **optional**. Supplied → the instance renders through the existing batched sprite
  path. Not supplied → `RenderTypeId = 0`, which `CombatRenderMatrixUtility.ElementFor` already
  treats as a degenerate (invisible) instance.
- When supplied, it renders **on the hit target, oriented along the link that reached it**, via
  the derived mirror from [§4.2](#42-the-walk-owns-two-positions) plus
  `CombatRenderComponent.AlignToVelocity = 1`. **No new render machinery.**
- **`Velocity` on a targeted entity is a render direction, not motion.**
- Multiple links in one update → the sprite lands on the **last** target hit.
- The walk never reads these back, so a wrong mirror can only ever be a visual bug.

**Rendering is the same path projectiles and AOEs use**: `TargetedTag` joins
`CombatBatchedRenderSystem.renderQuery`'s `WithAny`, render components stay on the archetype
unconditionally, and the query's `IgnoreComponentEnabledState` means pooled targeted entities sit
in the instance buffer as degenerate quads exactly as pooled projectiles and AOEs already do.

### 6.2 Link VFX (the shipped visual)

- **One `LineSegmentVfxSpawn` per link, always — source → target.** A `chainCount = 6` walk that
  lands 6 links emits 6 separate segments — never one polyline, never one segment for link 0 only.
- Link 0's segment starts at the **origin**, so the chain is always visibly attached to whatever
  produced it. Later segments run target k → target k+1, joined end-to-end.

  ```text
  caster ──▶ target0 ──▶ target1 ──▶ target2
     seg0        seg1        seg2
  ```
- Optional circular impact flash per hit target.
- With `chainDelay > 0`, links emit across several frames, so the arc visibly travels.
- A link that finds no target emits nothing — no dangling segment into empty space.
- VFX is visual only, never authoritative for damage.

## 7. Authoring

- `SkillDefinitionTags.Targeted`; `Any` widens to `Projectile | Aoe | Targeted`.
- **One skill type**, `TargetedSkill`, deriving `TargetedSkillBase`. Menu
  `PlayGround/Skills/Targeted Skill`.

```text
TargetedDefinition
 ├─ prefab:    TargetedPrefab   → sprite, material, VFX assets. No hurtbox.
 └─ behavior:  damage, manaCost, directDamageEnabled,
               echoCount,                                ← multiplicity (§4.6)
               chainCount, chainDistance, chainDelay,     ← one chain's length, reach, pacing
               chainDamageFalloff, armSeconds
```

### 7.1 Radius stats

- `chainDistance` folds through the existing **`AreaSize`** stat, so `areaSizeMultiplier` scales a
  chain's reach with no new stat kind.
- `IncreasedAoeSupport` and `ConcentratedEffectSupport` add `Targeted` to their
  `SupportedSkillTags` and then work unchanged.
- `vfxEffectSize` and `linkWidth` are **not** folded — they are visual-only and must not scale
  with `AreaSize`, or a build that widens a chain's reach would silently inflate its flashes.

### 7.2 `TargetedPrefab`

Follows `BasicAoePrefab`'s shape, minus collision:

- **`Visual` child `SpriteRenderer` — optional.** Same rule `BasicAoePrefab` uses: if present with
  a sprite it must be on a child named `Visual`, with a non-null, textured, GPU-instanced material
  on a supported shader.
- **No `Hurtbox` child, and none is baked.** `TargetedPrefab` exposes no `Radius`, `HalfExtents`,
  `RotationRadians`, or `ShapeType`, and the archetype carries neither `CombatCollisionComponent`
  nor `CombatCollisionActiveTag`. `IsValidTemplate` **fails** if a `Hurtbox` child is found — it
  means the prefab was copied from a projectile or AOE template.
- VFX asset slots follow `BasicAoePrefab`'s pattern: a **link** effect that must be
  `VfxDataShape.LineSegment`, plus optional hit / spawn / expire / arming effects, an authored
  `vfxEffectSize` (a chain has no area to derive one from), and an authored `linkWidth`.

### 7.3 Supports

- `AddedDamageSupport` and `IncreasedRateSupport` work free once `Any` widens.
- **`MultipleChainsSupport`** — the third member of the `MultipleProjectilesSupport` /
  `MultipleAoesSupport` family, adding `echoCount` and modifying `ManaCost` through the same three
  `IManaModifiers` interfaces. Unlike its siblings it contributes no geometry field.
- Chain **length** (`chainCount`) still has no support — it is authored on the skill.
- Projectile-only supports stay projectile-only: no-op + warning on a targeted set.

## 8. Wiring

- **Player-cast root** — existing gate: `SkillDriver` → `SkillSpawnTranslator` → `CombatRoot` →
  `ExternalSpawnRequest` → mana deduction. Rejection refunds cooldown.
- **`OnImpactTargetedTrigger`** — projectile or AOE hit fires the chain from the impact point.
  Carries no fields, mirroring `OnImpactAoeTrigger`.
- **`TargetedIntervalSpawnTrigger`** — energy-accrual child from a projectile or lingering AOE.
  Pulse AOE source → warning, no-op. Carries one field, `echoCount`, **additive** with the child
  definition's own and floored to 1. No geometry field.
- **`StackTrigger`** — free once `Any` widens; chain becomes a stack applicator.
- Targeted is **never an interval source** — it has no duration of its own to accrue over.
- Works for any faction; mob casters get it free.

## 9. Runtime constraints

- Own `TargetedTag`; every targeted system query requires it. Faction from identity, never from
  scope membership.
- A real pooled entity is required even for instant hits — finalize resolves damage through
  `ComponentLookup<CombatHitPayload>[hit.Source]`.
- Reuses shared machinery: `Active` disable-in-place pooling, `CombatKinematicsComponent`,
  `CombatLifetimeComponent`, `ArmingTag`, `CombatHitPayload`, and the canonical
  event → expansion → command → apply spawn path.
- **One pool.** `CombatPoolCleanupSystem` goes from four pools to **five**.

### 9.1 Walk state

- **Per-entity and persistent across frames** once `chainDelay > 0`: `LinkSource`, `LinkTarget`,
  the stored origin, the acquisition anchor, `LastTargetKey`, link index, and link gate remaining.
  Four `float2`s, two `int`s, one `float`.
- The walk state is **authoritative**; `CombatKinematicsComponent` is a derived render mirror
  written once per resolve.
- **Exclusion is one `int`** — no set, no buffer, no list.
- **No `Health` lookup in the resolve.** It reads only the broadphase snapshot arrays and its own
  chain state — no random-access `ComponentLookup` at all.
- Reuses **`TargetSpatialHashSingleton.AoeOccupiedCells`** — the same broadphase AOE area queries
  use. Targets are inserted into every cell their bounds overlap, so a radius query finds them by
  shape rather than by centre. **Zero new native containers, zero new spatial structures.**
- Resolve slot: after `TargetSpatialHashSystem` and arming, before
  `CombatApplyFinalizeSingleSystem` and before spawn expansion — the AOE collision slot.
- Combines the hash `BuildHandle`, publishes into `ConsumerHandle`, writes lanes via
  `AsParallelWriter`, chains `ProducerHandle` on the main thread.
- Lanes are read directly and must throw if missing — a missing lane is a broken world.

### 9.2 Lifetime

- The resolve expires the instance when the walk ends: disable `Active`, emit the expire VFX.
  That is the normal and effectively only despawn path.
- `CombatLifetimeComponent` is still on the archetype, stamped by the compiler with
  `chainCount * chainDelay + margin` via `RuntimeTargetedDefinition.LifetimeFor`. **It is never
  authored.** It exists so an instance that somehow stops walking cannot leak a pooled slot; in
  normal play the resolve always fires first.
- **Spawn and despawn counts must feed `CombatStatsSingleton`.** The pool-cleanup calm-down gate
  derives despawns as `spawns − Δactive` across the whole scene, so a new entity kind that churns
  without incrementing those counters skews the gate for *every* pool.
- **Triggered chains land one frame after their cause.** Apply runs after collision, so a
  projectile impact in frame N creates the targeted entity in N and resolves it in N+1. Identical
  to impact AOEs — but worth knowing before it gets reported as input lag.

## 10. Contract changes

- `SkillDefinitionTags.Targeted` added, `Any` widened. Intended behaviour change: the three
  current `Any` consumers (`StackTrigger`, `AddedDamageSupport`, `IncreasedRateSupport`) now accept
  targeted skills.
- `IntervalChildKind` gains **one** value, `Targeted`. Audit every existing `switch` — current
  sites are `ExternalSpawnGateSystem.AppendInternalSpawn`, `TimedSpawnSystem`, `AoeCollisionCore`,
  `ProjectileDiscreteCollisionSystem`, and `CombatRoot`.
- One new spawn-event struct (`TargetedSpawnEvent`), one lane, one expansion system, one apply
  system, and a `TargetedSpawnTemplate` registry map.
- Expansion follows the documented contract exactly: dereference the event's template key, apply
  the per-instance frame, and explode `echoCount` into **one command per spawned entity**.
- **This takes the identical-spawn-event count from three to four.** Deliberate; recorded in
  `Docs/todo.md` so the consolidation refactor is not lost.
- `CombatHitEvent` gains `float DamageScale` (default `1`), applied before the crit roll. Needed
  because one source entity emits N links at N damages while `CombatHitPayload` is one per entity.
  Rejected alternatives: one entity per link (a 6-jump chain becomes 6 pooled entities per cast);
  mutating the payload per link (data race under `ScheduleParallel`).

## 11. Performance

- Burst-compiled, `ScheduleParallel`, no managed allocations, no LINQ, no closure captures.
- The resolve reads only broadphase snapshot arrays and its own chain state.
- **No search-radius cap.** The occupied-cells hash is keyed at a cell size derived from target
  extents and only stores cells targets actually occupy, so query cost tracks the number of targets
  in the region rather than the square of the radius.
- One cap, about an authored count rather than search: `MaxChainCount` (32), which bounds links
  per resolve and the rank set.
- **The per-resolve cap does not bound the scene.** `TargetedIntervalSpawnTrigger` on a projectile
  volley can put hundreds of walking chains in flight at once. Today only the energy threshold
  throttles it. Needs either a scene-level concurrent-instance cap or a measured ceiling from a
  stress test before content leans on it. Link VFX volume scales the same way.
- Targeted counters into `CombatStatsSingleton`, surfaced via `CombatStatsDisplaySingleton`.
- No `unsafe`. Native/ECS handles disposed by their owner. `ECS Lifecycle:` comments on every new
  component, tag, and buffer.

## 12. Validation

Everything the removed variant needed warnings for is now structurally impossible: with no
authored lifetime and no tick interval there is nothing for `chainCount * chainDelay` to be
truncated by. What remains:

- Clamp + warn: `chainCount` outside `[1, MaxChainCount]`, `echoCount < 1`, `chainDelay < 0`.
  `chainDistance` is **not** clamped — there is no radius cap (§11).
- **Error** on `chainDistance <= 0` — link 0 could never acquire anything, so blocking beats
  firing a silent no-op.
- Warn on `chainDamageFalloff <= 0` with `chainCount > 1` (all links after the first deal zero).
- Warn on an interval trigger whose energy threshold exceeds what the source can accrue over its
  lifetime — the child never spawns. This is the documented interval trap.
- Warn on a missing link VFX or one that does not decode to `LineSegment`. This is the shipped
  visual, so its absence means an invisible skill in play — a debug sprite does not excuse it.
- **Error** when `TargetedPrefab` has a `Hurtbox` child.
- Warn on a spawn / hit / expire / arming effect assigned with `vfxEffectSize <= 0`, or a link VFX
  assigned with `linkWidth <= 0` — those resolve to nothing visible.
- Warn on the existing `BasicAoePrefab` material rules when a sprite *is* supplied — reuse, do not
  re-invent.
- Warn on a pulse-AOE source for `TargetedIntervalSpawnTrigger`.
- Warn on projectile-only supports on a targeted set (existing tag-mismatch path).

A fully valid targeted loadout must produce **zero** warnings. That test is what stops the warning
set becoming noise everyone ignores.

## 13. Tests

**EditMode** — chain order and count; chain break past `chainDistance`; empty acquire; faction
filter; per-link falloff; deterministic tie-break; arming delay; cap clamping without buffer
overrun.

Lifetime and delay:
- `chainDelay == 0` — all links resolve in **one** update.
- `chainDelay > 0` — exactly one link per elapsed delay; link k lands at `k * delay`.
- Catch-up — a single update with `dt > chainDelay` fires several links, capped at `chainCount`.
- **Walk end is the lifetime** — a chain whose last link lands expires that update even with a
  huge `CombatLifetimeComponent.Remaining`, and a chain whose link finds nothing expires
  immediately having emitted no hit.
- Compiled lifetime is derived, never authored — it always exceeds `chainCount * chainDelay`, and
  is still positive when `chainDelay == 0`.
- `TargetedDefinition` exposes no `lifetimeSeconds`, no `tickIntervalSeconds`, no `acquireRadius`.
- Target dies mid-walk — the walk continues from its last position rather than aborting.
- Moving target mid-walk — the next link is chosen from the target's position at that link's time.

Walk and render:
- Entity position tracks the head — after link k, `Position` equals target k's centre, and link
  k+1's search is centred there, not on the anchor.
- Walk positions — after link k, `LinkSource` equals target k-1 (the origin for k = 0) and
  `LinkTarget` equals target k.
- Render mirror, one link per update — `Position` equals `LinkTarget`, `Velocity` equals
  `LinkTarget - LinkSource`.
- Render mirror, many links per update — `Position` equals the **last** target hit that update.
- Sprite pose, no link this update — position and velocity unchanged (no snap to `+X` facing).
- No sprite authored — `RenderTypeId == 0` and no render instance, while hits and link VFX fire.
- No collision — a targeted instance never appears in any collision system's query.

Chain rules:
- Segment count and topology — a walk landing N links emits exactly N `LineSegmentVfxSpawn`,
  link 0's start equals the **origin**, link k's start equals link k-1's end.
- Origin vs anchor — a root cast with caster and cursor apart draws segment 0 from the **caster**
  while link 0's target is the one nearest the **cursor**.
- A walk that finds no target for link k emits k segments, not k+1.
- No self-link — link k+1 never selects link k's own target.
- Revisiting is allowed — two enemies with `chainCount = 6` produce A→B→A→B→A→B, falloff applied
  by link index, not by distinct target.
- Exclusion keys on target key, not index — a walk spanning frames during which other proxies are
  created or destroyed still excludes the right entity.

Multiplicity:
- `echoCount = 3` produces exactly three commands and three entities from one event, each with a
  distinct `SourceId`, instance index, and jitter seed.
- Fork rank — with `echoCount = 3` and four enemies at increasing distance, fork 0 opens on the
  nearest, fork 1 the second, fork 2 the third, with **no** positional displacement anywhere.
- Fork wrap — with `echoCount = 3` and one enemy in range, all three forks open on it.
- Trigger multiplicity is additive — `TargetedIntervalSpawnTrigger.echoCount = 2` on a child whose
  own `echoCount` is 3 spawns 5 chains per energy tick.

**PlayMode** — player cast damages a mob with no projectile/AOE entity created; chain across three
mobs from one cast; a chain alternates between two targets and expires with its walk; mana gate
rejection refunds cooldown; on-impact trigger fires from the impact point; interval trigger spawns
children from a lingering AOE; stack applicator detonates at threshold; dispatched `LineSegment`
count equals the resolved link count; 200 casts leave entity count bounded; mob-cast chain hits the
player and never other mobs.

Tests prove gameplay behaviour, not that code ran. No production-only test hooks.

## 14. Decisions

| # | Decision | Where it lands |
|---|---|---|
| 1 | **Empty cast spends.** Mana is deducted at `ExternalSpawnGateSystem` like every other root cast; only insufficient mana refunds the cooldown. | §8 |
| 2 | **One variant.** A chain's lifetime is its walk; repeating chains come from an interval trigger on a projectile or lingering AOE. | §2 |
| 3 | **One distance.** `chainDistance` governs link 0 and every later hop. | §3 |
| 4 | **Lifetime is derived, never authored.** The compiler stamps a fail-safe from the walk's worst case; the resolve is what actually expires the instance. | §9.2 |
| 5 | **Radii fold through `AreaSize`**, scaled by the player's area-size stats. | §7.1 |
| 6 | **No line of sight.** Walls are not implemented in the game yet. | §15 |
| 7 | **Do not consolidate spawn-event structs now.** Add the fourth, note the debt in `Docs/todo.md`, refactor later. | §10 |
| 8 | **No dead-target check.** Unsolvable at resolve time — damage aggregates in finalize, which runs after every resolve. | §4.5 |
| 9 | **Remember one target key, not an exclusion set.** Enough to stop a link selecting its own source; revisiting earlier targets is allowed. | §4.4 |
| 10 | **Link 0 searches around the cursor**, captured at cast time. The anchor does not follow the cursor afterwards. | §4.1 |
| 11 | **Same rendering path as projectiles and AOEs.** `TargetedTag` joins `renderQuery`; no targeted-specific render handling. | §6.1 |
| 12 | **Multiplicity via the canonical spawn path** — event → expansion explodes `echoCount` into N commands, one per entity. | §4.6, §10 |
| 13 | **No scatter, no spread, anywhere.** Forks differ by acquisition rank instead. | §4.6 |
| 14 | **No sprite stretching.** Debug affordance at authored size; the `LineSegment` VFX is the shipped visual. | §6.1 |

## 15. Out of scope

- Line of sight / wall occlusion.
- Targeted → targeted chaining.
- Targeted acting as an interval energy **source** for child spawns.
- Swept-line beam damage (that's a moving collision volume, not a query).
- Retargeting or steering an in-flight instance toward a moved anchor.
- New AOE shape kinds, ECS sound, damage-number VFX graph — separate todo entries.

## 16. What the simplification removed

Cut from the first implementation, with the reasoning:

| Removed | Why |
|---|---|
| `LingeringTargetedSkill` / `LingeringTargetedDefinition` | The only thing the variant added was "restart the walk every tick", which an interval trigger already expresses against a source that actually has a duration. |
| `lifetimeSeconds`, `tickIntervalSeconds` | Three interacting timers (these two plus `chainDelay`) with no authored relationship between them. Every truncation warning in the old §11 existed to catch a combination the author had no way to reason about. |
| `acquireRadius` | Second reach knob, distinguishable from `chainRadius` only by which link it applied to. |
| `IntervalChildKind.LingeringTargeted`, `LingeringTargetedTag`, `TargetedTickGateComponent` | Discriminators for a variant that no longer exists. |
| `LingeringTargetedSpawnEvent` + its lane, expansion system, apply system, and pool | Same. Spawn-event duplication drops from five copies to four. |
| `TargetedVariant.ChildKindFor` | Nothing left to discriminate. |
| `TargetedResolveCore` | Existed so two resolve systems could share the walk. With one system it is the system. |
| `TargetedSpawnCommand.HasTimedSpawner` / `.TimedSpawn` | Only the lingering variant could host a timed spawner. |
| Four truncation warnings | Structurally unreachable once the walk owns the lifetime. |

Renames, all covered by `[FormerlySerializedAs]` so authored values survive: `count` → `echoCount`,
`maxTargets` → `chainCount`, `chainRadius` → `chainDistance`, `chainDelaySeconds` → `chainDelay`.

Net: roughly half the targeted runtime surface, and an inspector that fits the mental model
"how many chains, how many links, how far, how fast".

## 17. Acceptance criteria

1. A `TargetedSkill` can be authored, slotted, bound to input, and cast — damaging the nearest
   enemy with no projectile or AOE entity created.
1b. `TargetedPrefab` authoring reads like `BasicAttackPrefab`/`BasicAoePrefab`: optional `Visual`
   sprite renderer, VFX slots, and **no hurtbox** — a prefab carrying one fails validation.
1c. With a debug sprite authored, it renders at its authored size **on the hit target** (the last
   one, when several links land in one update), oriented along the link that reached it. With no
   sprite the skill renders nothing and the `LineSegment` VFX carries the whole visual.
2. `chainCount > 1` with enemies inside `chainDistance` and `chainDelay == 0` damages the whole
   chain in one frame, with falloff applied.
2b. `echoCount > 1` spawns that many independent chains from one cast through the normal
   event → expansion → one-command-per-entity path, fork `i` opening on the `i`-th nearest enemy
   with no positional scatter anywhere in the feature.
3. `chainDelay > 0` staggers the same chain one link per delay, the instance's position lands on
   each target in turn, and the walk survives a target dying partway through.
4. **The instance expires when its walk ends** — after the last link, or after a link that finds
   nothing — and returns to its pool. No authored lifetime exists to tune, and the compiled
   fail-safe never fires in normal play.
5. Every landed link emits its own `LineSegment` VFX at that link's own time, source → target:
   caster → first target, then target k → target k+1, joined end-to-end.
6. Damage, crit, and death flow through the existing presentation path with no targeted-specific
   presentation code.
7. `StackTrigger` on a targeted set applies stacks to every linked target.
8. `OnImpactTargetedTrigger` fires a chain from a projectile's impact point.
9. `TargetedIntervalSpawnTrigger` spawns targeted children from a lingering AOE — this is how a
   chain repeats over time.
10. Every targeted system query requires `TargetedTag`.
11. No new persistent native container and no new spatial hash; exclusion is a single `int` and
    the resolve makes no random-access component lookups.
12. Targeted entities pool by disable-in-place across **one** pool and are trimmed by
    `CombatPoolCleanupSystem`.
12b. A link never selects the target the previous link hit.
13. All §12 validation cases surface in `SkillDriver.ValidationWarnings`, and a valid loadout
    produces none.
14. All §13 tests pass.
15. Docs updated in the same change: `skill-system.md`, `skill-modifiers.md`,
    `spawn-events-and-commands.md`, `targeted-system.md`, `reference/simulation/index.md`,
    `folder-structure.md`.
16. The targeted-skills line in `Docs/todo.md` is struck.
