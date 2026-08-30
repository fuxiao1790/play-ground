---
name: migrate-projectile-to-shared-lookup
description: Switch ProjectileCollisionJob.Execute to drive TargetSpatialHashLookup instead of its inline cell-walk, and add the MaxHitsPerTick accepted-hit cap as a job-owned counter.
---

# 004 - Migrate Projectile Discrete to the Shared Lookup, Add the Cap

## Depends On

[001-generalize-hit-cap-constant.md](001-generalize-hit-cap-constant.md),
[002-introduce-spatial-hash-lookup.md](002-introduce-spatial-hash-lookup.md),
and [003-migrate-aoe-to-shared-lookup.md](003-migrate-aoe-to-shared-lookup.md)
(003 isn't a hard technical dependency, but land it first so both migrations
can be reviewed against the same known-good pattern).

## Changes

### [ProjectileDiscreteCollisionSystem.cs](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs)

In `ProjectileCollisionJob.Execute` (starts [line 134](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L134)):

1. Keep the existing radius-expansion + `cellMin`/`cellMax` computation
   ([lines 196-200](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L196-L200))
   unchanged — this stays projectile-specific, per index.md's constraint that
   cell-range derivation is not part of the shared lookup's job.
2. Add `int remaining = CollisionConstants.MaxHitsPerTick;` before the walk.
3. Construct `var lookup = new TargetSpatialHashLookup(TargetCells, TargetFactions, identity.Faction, cellMin, cellMax);`.
4. Replace the nested `for (cy...) { for (cx...) { ... do { ... } while (...) } }`
   block ([lines 202-294](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L202-L294))
   with:
   ```csharp
   while (remaining > 0 && lookup.MoveNext(out int targetIndex))
   {
       // existing per-candidate body: TargetKey, IsGated check, BoundsIntersect,
       // exact Hit test, EnqueueHitEvent/OnHit* emissions, AddOrRefreshGate,
       // PierceRemaining-- and its exhaustion return.
       // After a hit is fully accepted (gate refreshed, pierce decremented,
       // and not already returning due to pierce exhaustion): remaining--.
       // If remaining reaches 0, return; (no Deactivate call — reaching the
       // cap is not projectile death, see index.md's cap-ownership decision).
   }
   ```
5. Remove the now-dead inline cell/bucket-walking code and the per-candidate
   `TargetFactions[targetIdx].Value == identity.Faction` check (now handled
   inside the lookup). The rest of the per-candidate body (gate check,
   `BoundsIntersect`, `Hit`, emissions, gate refresh, pierce decrement and its
   own exhaustion check) is unchanged, just re-indented under the new `while`.
6. Order of the two independent stop conditions within the accepted-hit
   block: check pierce exhaustion first (existing behavior, unchanged),
   *then* decrement and check `remaining`. Both are plain `return` (no
   `Deactivate` for the cap case, `Deactivate` for pierce exhaustion — this
   distinction is the whole point of task index.md's constraints table).

Do not touch `IsGated`, `AddOrRefreshGate`, or `ProjectileHitEmission`'s other
functions — this task only changes how candidates are sourced and adds the
cap check after acceptance.

## Test Coverage

Add to [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs),
next to the existing pierce tests (~line 249):

**New test**, e.g. `DiscreteHitsAtMostMaxHitsPerTick`:
- `AddTarget(float2.zero, 0.25f)` in a loop for `CollisionConstants.MaxHitsPerTick + N` distinct targets, all overlapping the projectile's collision circle at `float2.zero`.
- `CreateProjectile(pierceRemaining: 99, ...)` — pierce set high enough that it is never the limiting factor, isolating the cap as the only stop condition.
- `TickSimulationOnly(0.01f)` once.
- Assert `ReadFinalizedHitCount() == CollisionConstants.MaxHitsPerTick`.
- Assert the projectile entity is still `Active`/`CombatCollisionActiveTag`-enabled afterward (cap-reached must not deactivate it) — this is the assertion that distinguishes cap-stop from pierce-exhaustion-stop.

## Acceptance Criteria

- A projectile whose collision shape overlaps more than `MaxHitsPerTick`
  distinct targets in one tick registers exactly `MaxHitsPerTick` hits that
  tick, in cell-scan order (first-N, not nearest-N).
- The projectile is not deactivated solely by reaching the cap; only pierce
  exhaustion, lifetime expiry, or faction clear deactivate it.
- Existing pierce and gate tests (`PierceRemainingZeroStillHitsOnceThenDespawns`,
  `PierceRemainingNHitsNPlusOneTargetsThenDespawns`,
  `ImpactSpawnContactGateSeedPreventsChildFromHittingSpawnTarget`, and others
  using low target counts) are unaffected, since their target counts stay
  well under `MaxHitsPerTick` (32).
- `ProjectileCollisionJob` remains Burst-compiled with no new allocations.
