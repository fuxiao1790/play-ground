# 005 — Tests

**Files:** `Assets/Tests/PlayMode/AoePlayModeTests.cs` (primary), and/or
`Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
**Depends on:** 001–004
**Scope:** medium

## New coverage

1. **AOE → projectile → AOE (2 links).** Compile a loadout
   `AOE [skill0] | OnImpactProjectile | projectile [skill1] | OnImpactAoe | AOE2 [skill2]`.
   Spawn the top-level AOE onto a mob; step the sim. Assert:
   - the burst projectile spawns when the AOE hits, and
   - AOE2 materializes / applies damage when the burst projectile impacts.

2. **AOE → projectile → projectile (2 links).** Same shape with trigger2 = `OnImpactProjectile`
   and skill2 a projectile. Assert the terminal projectile spawns on the burst projectile's impact.

3. **No-second-trigger regression.** An AOE → projectile with no second trigger spawns the burst
   exactly as before (impact disabled), guarding the default path.

## Regression runs

- `AoePlayModeTests`, `ProjectileCollisionSimulationTests`, `ProjectileSpawnPipelineTests`.
- Stacking-detonation tests (shared `BuildProjectileDetonationBurstSnapshot` changed in 003) —
  confirm unchanged behavior where no impact is configured.

## Acceptance criteria

- New tests pass; existing PlayMode suites stay green.
