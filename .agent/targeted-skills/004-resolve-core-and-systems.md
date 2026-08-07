# 004 — Resolve core and the two resolve systems

**Depends on:** 002, 003. **Scope:** large. **Risk:** highest in the plan — this is the feature.

## Why

The chain walk itself. Mirrors `AoeCollisionCore` + the two AOE collision systems: one shared core,
two thin systems, the interval one adding the tick gate.

## New files

`Assets/Scripts/System/Targeted/TargetedResolveCore.cs`

Burst-compatible static core. **Reads only the broadphase snapshot arrays and its own chain state
— no `ComponentLookup`, no random access** (C2, C9). Requirements §3.5 explains why there is
deliberately no liveness check.

Drain loop, per requirements §3.3. The three bounds — remaining links, elapsed delay, and the
catch-up cap — collapse into a single `availableHits` count computed up front, so the loop body is
a plain walk:

```text
chain.LinkGateRemaining -= dt

// Pure read, no mutation: how many links the gate has unlocked this update.
// ChainDelaySeconds == 0 unlocks the whole remaining walk, which is why delay 0 is not a
// special case anywhere in the body.
availableHits = cfg.ChainDelaySeconds <= 0
    ? cfg.MaxTargets
    : (chain.LinkGateRemaining <= 0
        ? floor(-chain.LinkGateRemaining / cfg.ChainDelaySeconds) + 1
        : 0)

availableHits = min(availableHits, cfg.MaxTargets - chain.LinkIndex)   // also the catch-up cap

currentPosition = chain.LinkTarget      // walk state, NOT kinematics.Position (see below)
linksThisUpdate = 0

for i in 0 ..< availableHits:
    // link 0 is the only asymmetric one: it searches the anchor, at acquire range, at fork rank
    searchFrom = chain.LinkIndex == 0 ? chain.AcquireAnchor    : currentPosition
    radius     = chain.LinkIndex == 0 ? cfg.AcquireRadius      : cfg.ChainRadius
    rank       = chain.LinkIndex == 0 ? identity.InstanceIndex : 0

    // excludeKey is load-bearing, not an optimisation — see below
    target = SelectNthNearest(searchFrom, radius, rank, faction, chain.LastTargetKey)
    if target not found: break

    chain.LinkSource    = currentPosition
    currentPosition     = target.Position
    chain.LinkTarget    = currentPosition
    chain.LastTargetKey = TargetKey(target.Entity)

    HitWriter.Enqueue(new CombatHitEvent {
        Source = self, Target = target.Entity,
        DamageScale = pow(cfg.ChainDamageFalloff, chain.LinkIndex) })

    VfxEmit.EnqueueLineSegment(vfxIds.LinkId, chain.LinkSource, chain.LinkTarget,
                               vfxSize.LinkWidth, lineSegments)
    if vfxIds.HitId != 0:
        VfxEmit.Enqueue(vfxIds.HitId, chain.LinkTarget, vfxSize.EffectSize, timing,
                        circularPending, timedCircularPending)

    chain.LinkIndex++
    linksThisUpdate++
    chain.LinkGateRemaining += cfg.ChainDelaySeconds   // per landed link only

if linksThisUpdate > 0:
    kinematics.Position = chain.LinkTarget                       // the only render coupling
    kinematics.Velocity = chain.LinkTarget - chain.LinkSource
```

Three things the loop shape must keep, each of which silently breaks the feature if dropped:

- **`excludeKey` is mandatory, not an optimisation.** After `currentPosition = target.Position`,
  that target's own centre is at distance zero from the next search origin, so an unfiltered
  `SearchClosest(currentPosition)` re-selects it forever. A chain would hit one enemy
  `maxTargets` times. `LastTargetKey` is the entire mechanism preventing it.
- **Seed `currentPosition` from `chain.LinkTarget`, not `kinematics.Position`.** They hold the same
  value at the top of a resolve, but kinematics is a *derived render mirror* (§3.2). Reading it
  back would make a render concern authoritative for targeting, and a stale mirror would become a
  gameplay bug instead of a visual one.
- **Gate advances per landed link, not per available slot.** `availableHits` is computed as a pure
  read; `LinkGateRemaining` only accrues inside the loop. Pre-consuming it would charge delay for
  links that never landed when the walk breaks early.

Hits are enqueued straight into the lane's parallel writer rather than accumulated into a local
list — the allocation rule forbids a per-entity temp container on this path (C9).

`SelectNthNearest(from, radius, rank, faction, excludeKey)`:

- Uses **`TargetSpatialHashSingleton.AoeOccupiedCells`** at `CombatSpatialHash.AoeCellSize` — the
  same broadphase AOE area queries use. Not `TrackingCells`: that stores one centre cell per
  target, so a large target reaching into range would be missed. Iterate the cell range with
  `CombatSpatialHash.MinCell`/`MaxCell` over the query circle's bounds, exactly as
  `AoeCollisionCore` does.
- **No radius clamp.** The hash only holds cells targets actually occupy, so cost tracks targets in
  the region, not radius². Do not reintroduce a `MaxTargetedSearchRadius`.
- Narrow phase: reuse `CombatCollisionMath` to test the search circle against the candidate's
  shape, the same overlap test AOEs run. Eligibility is shape overlap; **ranking** is by centre
  distance.
- Skips `target.Faction == self.Faction` and `TargetKey(entity) == excludeKey`.
- **Dedupe by target index.** A target's bounds span several cells, so one scan can encounter it
  repeatedly. Harmless for a plain nearest search — same target, same distance — but it would
  corrupt rank selection by filling the running set with one enemy. The running set is keyed by
  index for this reason.
- Keeps the `rank+1` nearest in a small fixed-size running set bounded by `MaxChainTargets`. The
  scan already visits every candidate to find the nearest, so ranking costs a bounded insert, not
  a second pass.
- Returns rank `rank mod eligibleCount` when fewer than `rank+1` candidates exist (the fork wrap in
  requirements §3.6), so `count = 3` against one enemy has all three forks hit it.
- Tie-break: lowest broadphase index. Deterministic **within a frame** only — the index comes from
  `ToEntityArray` in chunk order. Do not assert cross-session reproducibility.

Further details that are easy to get wrong:

- `chain.LinkTarget` is seeded to the origin at spawn (task 003), so link 0's `LinkSource` lands on
  the caster and its segment starts there.
- `rank` applies to link 0 only. Links 1..n always take the nearest.
- `LastTargetKey` is **not** cleared between links — it advances with each one. A link may revisit
  an earlier target; only the immediately previous one is barred.
- When `linksThisUpdate == 0`, leave position and velocity untouched: a zero `Velocity` makes
  `ElementFor` fall back to facing `+X`, snapping the debug sprite sideways between jumps.

`Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`

- `TargetedResolveSystem` — query `TargetedTag`, `Active`, `WithDisabled<ArmingTag>`,
  `WithNone<LingeringTargetedTag>`. Runs the core once; when the walk ends
  (`LinkIndex >= MaxTargets` or a link found nothing) calls `CombatDeathUtility.Kill` to disable
  the entity.
- `LingeringTargetedResolveSystem` — same query with `WithAll<LingeringTargetedTag>`. Decrements
  `TargetedTickGateComponent.Remaining`; on expiry, **restarts the walk**: `LinkIndex = 0`,
  `LinkSource = LinkTarget = Origin`, `Remaining += TickIntervalSeconds`, and **`LastTargetKey` is
  deliberately preserved** so the new walk does not open on the enemy the last one ended on
  (requirements §3.4). Lifetime expiry is handled by task 005, not here.
- Both: `[UpdateInGroup(SimulationSystemGroup)]`, after `TargetSpatialHashSystem` and
  `CombatArmingSystem`, before `CombatApplyFinalizeSingleSystem` and before every spawn expansion
  system — the slot the AOE collision systems occupy.
- **This task owns the resolve↔expansion ordering.** Task 003 deliberately does not name these
  systems (they did not exist yet), so the constraint is declared here:
  `[UpdateBefore(typeof(TargetedSpawnExpansionSystem))]`,
  `[UpdateBefore(typeof(LingeringTargetedSpawnExpansionSystem))]`, plus the existing projectile and
  AOE expansion systems, so an on-hit spawn a chain produces lands in the same frame.
- Both combine `TargetSpatialHashSingleton.BuildHandle` into their dependency and publish into
  `ConsumerHandle` (C8), and combine their handle into `CombatHitDispatchSingleton.ProducerHandle`
  and `CombatAoeVfxDispatchSingleton.ProducerHandle` on the main thread (C6).
- Lanes are read with `GetSingletonRW`, never `TryGetSingleton` — a missing lane must throw (C7).

## Acceptance criteria

EditMode, using hand-registered templates (no authored assets needed):

- Five targets in a line spaced under `chainRadius`, `maxTargets = 5`, delay `0` → five hits in
  nearest-first order, in **one** update.
- A gap larger than `chainRadius` after target 2 → exactly two hits.
- No eligible target in `acquireRadius` → zero hits; the entity still expires.
- Same-faction proxies nearer than enemies are never selected.
- Link k damage equals `Damage * pow(falloff, k)` (via `DamageScale` on the emitted events).
- **No self-link:** link 1 never re-selects link 0's target even though its centre is the nearest
  point to the search origin. Without `LastTargetKey` this test hits one enemy `maxTargets` times.
- **Revisiting allowed:** two enemies, `maxTargets = 6` → A→B→A→B→A→B, six links, falloff by link
  index.
- `chainDelaySeconds > 0` → exactly one link per elapsed delay; link k lands at `k * delay`.
- Catch-up: one update with `dt > chainDelaySeconds` fires several links, capped at `maxTargets`.
- Fork rank: `count = 3` against four enemies at increasing distance → fork 0 opens on the nearest,
  fork 1 on the second, fork 2 on the third.
- Fork wrap: `count = 3` against one enemy → all three forks hit it.
- Interval restart: a tick restarts from `Origin` with `LinkIndex = 0`, and link 0 skips the enemy
  the previous walk ended on; with only one enemy in range it hits it anyway.
- Walk positions: after link k, `LinkSource == target k-1` (origin for k = 0) and
  `LinkTarget == target k`.
- Render mirror: after a resolve, `Position == LinkTarget` and
  `Velocity == LinkTarget - LinkSource`; after an update with no link, both are unchanged.
- Segment topology: N landed links emit exactly N `LineSegmentVfxSpawn`, link 0's start equals the
  origin, link k's start equals link k-1's end. A walk that stops at link k emits k segments.
- Target dies (proxy removed) mid-walk → the walk continues from its last position.
- Arming: `armSeconds > 0` produces no hits until the window elapses.
- `maxTargets` above `MaxChainTargets` clamps without overrunning the rank set.
- **Shape overlap, not centre distance:** a large target whose collision shape reaches into
  `acquireRadius` but whose centre lies outside it **is** selected. This is the case `TrackingCells`
  would have missed.
- **Dedupe:** a target whose bounds span several cells is counted once. With `count = 2` and two
  enemies where the near one spans many cells, fork 1 opens on the *far* enemy, not on a duplicate
  of the near one.
- A large search radius over a sparse field costs proportional to targets present, not to radius —
  assert the candidate-visit count, not wall time.

## Notes

Rank-offset fork differentiation is settled — implement it as written. It derives from the "no
scatter" decision (requirements decision 14): with no positional spread, the instance index is what
separates forks.
