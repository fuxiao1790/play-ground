# 001 — Projectile spawn-state helper (mirror AOE `SpawnStateFor`)

## Goal

Give `ProjectileSpawnApplySystem` a single gate-state helper, mirroring
`AoeSpawnApplyUtility.SpawnStateFor`, so the two hand-written gate sites (reuse job
+ cold-create) derive their enable bits from one place. Behavior-identical.

## Why

Today the projectile enable bits are written independently in
`RecordCommonProjectileReset` / `RecordTimedSpawnReset` (cold-create) and in
`ProjectileSpawnJob.Execute` (reuse). They already agree, but by hand — the same
drift risk `SpawnStateFor` was introduced to kill on the AOE side. Establishing the
helper now also gives task 003 one obvious place to layer the `ArmingTag` decision.

## Changes

- [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs):
  - Add a `SpawnState`-style helper (private struct/static, matching the AOE shape:
    `Active`, `Collision`, `Timed`, plus projectile's `Tracking`) computed from the
    `ProjectileSpawnCommand`. `Collision = NeedsCollision(cmd.HitPayload)`,
    `Tracking = cmd.Tracking.TrackingEnabled`, `Timed = cmd.HasTimedSpawner != 0`,
    `Active = true`.
  - Route both the cold-create path (`SetComponentEnabled<…>` calls) and the reuse
    path (`activeMask`/`collisionActiveMask`/`trackingMask`/`timedSpawnMask` writes)
    through that one helper.
  - Note: `CombatRenderActiveTag` no longer exists (consolidation done), so there is
    no render bit to include — render derives from `Active`.

## Acceptance criteria

- One helper is the sole producer of the projectile spawn enable bits; no
  hand-written enable value remains at either site except via the helper.
- Compiles; existing PlayMode suite passes unchanged (spawn, reuse, visual-only
  projectiles skipped by collision, timed-spawn projectiles still tick).

## Scope

Small. Single file, pure refactor.

## Dependencies

None. Independent of 002 and 003.
