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

Drain loop, per requirements §3.3:

```text
linksThisUpdate = 0
chain.LinkGateRemaining -= dt

while chain.LinkIndex < maxTargets
      and chain.LinkGateRemaining <= 0
      and iterations < maxTargets:

    searchFrom = chain.LinkIndex == 0 ? chain.AcquireAnchor : chain.LinkTarget
    radius     = chain.LinkIndex == 0 ? cfg.AcquireRadius   : cfg.ChainRadius
    rank       = chain.LinkIndex == 0 ? identity.InstanceIndex : 0

    target = SelectNthNearest(searchFrom, radius, rank, faction, chain.LastTargetKey)
    if none: break

    chain.LinkSource   = chain.LinkTarget
    chain.LinkTarget   = target.Position
    chain.LastTargetKey = TargetKey(target.Entity)

    HitWriter.Enqueue(new CombatHitEvent {
        Source = self, Target = target.Entity,
        DamageScale = pow(cfg.ChainDamageFalloff, chain.LinkIndex) })

    VfxEmit.EnqueueLineSegment(cfg.LinkVfxId, chain.LinkSource, chain.LinkTarget,
                               cfg.LinkWidth, lineSegments)
    if cfg.ImpactVfxId != 0:
        VfxEmit.Enqueue(cfg.ImpactVfxId, chain.LinkTarget, ...)

    chain.LinkIndex++
    linksThisUpdate++
    chain.LinkGateRemaining += cfg.ChainDelaySeconds   // += 0 drains the whole walk this update
    iterations++

if linksThisUpdate > 0:
    kinematics.Position = chain.LinkTarget                       // the only render coupling
    kinematics.Velocity = chain.LinkTarget - chain.LinkSource
```

`SelectNthNearest(from, radius, rank, faction, excludeKey)`:

- Scans the cells of `TrackingCells` overlapping the circle, clamped by `MaxTargetedSearchRadius`.
- Skips `target.Faction == self.Faction` and `TargetKey(entity) == excludeKey`.
- Keeps the `rank+1` nearest in a small fixed-size running set — the scan already visits every
  candidate to find the nearest, so ranking costs a bounded insert, not a second pass.
- Returns rank `rank mod eligibleCount` when fewer than `rank+1` candidates exist (the fork wrap in
  requirements §3.6), so `count = 3` against one enemy has all three forks hit it.
- Tie-break: lowest broadphase index. Deterministic **within a frame** only — the index comes from
  `ToEntityArray` in chunk order. Do not assert cross-session reproducibility.

Key details that are easy to get wrong:

- `chain.LinkSource = chain.LinkTarget` **before** writing the new target; `LinkTarget` is seeded to
  the origin at spawn (task 003), so link 0's segment correctly starts at the caster.
- `rank` applies to link 0 only. Links 1..n always take the nearest.
- `LastTargetKey` is **not** cleared between links — it advances with each link, and it is the
  entire exclusion mechanism. A link may revisit an earlier target; only the immediately previous
  one is barred.
- Iteration cap is `maxTargets`, mirroring the bounded catch-up `TimedSpawnSystem` uses.
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

## Notes

Rank-offset fork differentiation is settled — implement it as written. It derives from the "no
scatter" decision (requirements decision 14): with no positional spread, the instance index is what
separates forks.
