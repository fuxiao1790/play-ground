# 002 — BuildBurstEvent forwards the impact payload

**File:** `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
**Depends on:** 001
**Scope:** small

## Change

In `BuildBurstEvent` (line 115-159), the `ProjectileHitPayload` is currently constructed with
`StackEffect = default` and both impact args `default` (lines 122-133). Replace with the same shape
`BuildImpactProjectileEvent` already uses (lines 76-87):

- `ImpactAoe`  = `ToProjectileImpactAoe(snapshot.ImpactAoe, faction)` (converter from 001).
- `ImpactProjectile` = `ToProjectileImpactProjectile(snapshot.ImpactProjectile, faction)`.
- `StackEffect` = the stack effect from the impact-AOE if applicable (mirror how
  `BuildImpactProjectileEvent` sources `snapshot.StackEffect`); otherwise leave `default`.

`faction` is already a parameter of `BuildBurstEvent`.

## Acceptance criteria

- A burst whose snapshot has a disabled `ImpactAoe`/`ImpactProjectile` produces the exact same
  `ProjectileSpawnEvent` as today (no behavior change for current bursts).
- A burst with an enabled impact produces a `ProjectileSpawnEvent.HitPayload` whose
  `ImpactAoe.Enabled` / `ImpactProjectile.Enabled` is true.
- No other system changes: `ProjectileCollisionSystem` consumes `HitPayload.ImpactAoe/ImpactProjectile`
  by component content, independent of spawn path (verified during exploration).
