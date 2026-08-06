# 005 — Lifetime, arming, pool cleanup, stats

**Depends on:** 003. **Scope:** small. **Risk:** the calm-down gate is scene-wide.

## Why

Targeted entities must expire, arm, pool, and count exactly like the other two domains. Each of
these is an existing system that enumerates domains explicitly, so each needs a targeted entry.

## Changes

`Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`

- Add a `TargetedLifetimeJob`, alongside the existing per-domain jobs, over
  `TargetedTag` + `Active` + `CombatLifetimeComponent` with `WithDisabled<ArmingTag>` — so lifetime
  does not tick during windup, matching the other two jobs.
- On expiry, `CombatDeathUtility.Kill(active, arming)`. Targeted entities have no
  `CombatCollisionActiveTag`, so use the projectile-shaped overload, not the AOE one.
- **Single-hit fail-safe.** A single-hit chain with `chainDelaySeconds > 0` lives until its walk
  finishes (task 004 kills it), but it must still carry a computed lifetime
  (`maxTargets * chainDelaySeconds` plus margin, set at compile time in task 009) so a walk that
  somehow cannot terminate expires instead of leaking a pooled entity.

`Assets/Scripts/System/Lifetime/CombatArmingSystem.cs`

- Include targeted entities in the arming countdown. Note the existing gotcha: a job taking
  `EnabledRefRW<ArmingTag>` scheduled with an explicit `EntityQuery` must list `ArmingTag` in that
  query or scheduling throws.

`Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`

- Add the two targeted pools. Cleanup goes from four to **six**: discrete projectile, continuous
  projectile, impact AOE, lingering AOE, targeted, lingering targeted.
- Pools are keyed the same way the AOE pair is: `TargetedTag` with `WithNone<LingeringTargetedTag>`
  for single-hit, `WithAll<LingeringTargetedTag>` for interval.

`CombatStatsSingleton` / `CombatStatsDisplaySingleton`

- Add targeted spawn and link counters. Task 003 already increments `EntitiesSpawned` for the
  pool-gate maths; this task adds the feature-specific counters and surfaces them on the debug
  overlay through the display singleton (C11).
- Game-object code reads only `CombatStatsDisplaySingleton` — never the per-frame accumulator.

## Acceptance criteria

- EditMode: an interval targeted entity with `lifetime = 1.0` and `tickInterval = 0.25` runs 5
  walks then disables itself.
- EditMode: lifetime does not decrement while `ArmingTag` is enabled.
- EditMode: `tickInterval > lifetime` produces exactly one walk (regression lock for the known
  interval trap — see project memory on interval threshold vs lifetime).
- EditMode: a single-hit chain whose walk cannot terminate still expires at the fail-safe lifetime
  and returns to its pool.
- EditMode: pool cleanup trims disabled targeted slots under the same calm-down conditions it uses
  for the other pools, and the single-hit and interval pools trim independently.
- EditMode: spawning targeted entities increments `EntitiesSpawned`, and the calm-down gate's
  despawn derivation stays correct in a mixed scene (projectiles + AOEs + chains).
- Debug overlay shows targeted counters via `CombatStatsDisplaySingleton`.

## Notes

The calm-down gate derives despawns as `spawns − Δactive` **scene-wide**. A domain that churns
without incrementing the counters skews trimming for every pool, so the stats wiring is a
correctness requirement here, not telemetry polish.
