# 005 — Tick systems look up template + expansion fan-out

## Scope

Make the interval tick systems resolve the template by key from the registry singleton and emit
a spawn event carrying the template's fan-out; let the expansion systems do the fan-out.

## Changes

1. **Tick systems** (`Assets/Scripts/System/Aoe/TimedAoeSpawnSystem.cs`,
   `Assets/Scripts/System/Projectile/TimedProjectileSpawnSystem.cs`):
   - Add `[ReadOnly] NativeHashMap<Hash128,…>` job fields fed from
     `SystemAPI.GetSingleton<ProjectileSpawnTemplate>()` / `<AoeSpawnTemplate>()`
     (use `TryGetSingleton` so a world with no bound root is a no-op).
   - In the cooldown loop, instead of looping `Count` and copying embedded fields, fetch
     `template = Map[spawner.TemplateKey]` once and emit a **single** `ProjectileSpawnEvent` /
     `AoeSpawnEvent` at the source position carrying the template's fan-out
     (`Count`, `SpreadDegrees`, pattern) + hit payload; the per-tick `JitterSeed`/tick-index seed
     the deterministic id/jitter as today.
   - Per-tick timer math (`CooldownRemaining`, `TickIndex`) is unchanged.

2. **Expansion fan-out**:
   - `ProjectileSpawnExpansionSystem`/`AoeSpawnExpansionSystem` already fan out by
     `Count`/`Spread`/`Jitter`; ensure the emitted event populates those from the template.
   - **Radial 360° pattern** for AOE-source projectile children: carry the pattern on the event
     (`SpawnPatternType`) and have the projectile expansion compute evenly-spaced full-circle
     directions for the radial case (the velocity-relative `SideSpray` stays the default for
     projectile-source children). This replaces the per-event radial loop currently in
     `TimedAoeSpawnSystem.RadialDirection`.

## Acceptance criteria

- Interval children spawn at the configured interval and count, with deterministic ids/jitter
  matching the previous behavior for the proj→proj case.
- AOE-source projectile children fan out radially (full circle) via the expansion system.
- No tick-system reads of removed embedded-template fields.

## Dependencies

004 (slim carriers). Can proceed in parallel with 006.
