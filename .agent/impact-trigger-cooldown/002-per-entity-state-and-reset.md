# 002 — Per-entity cooldown state + reset-on-reuse

Add the mutable per-entity countdown and make the AOE hit-spawn component
writable in the collision queries. **Still no gate yet** (task 003) — this task
adds the field, resets it, and confirms the template hash covers the new config.

## Changes

1. **`ProjectileEcsComponents.cs` — `ProjectileHitComponent`** — add:
   ```csharp
   public float OnHitSpawnCooldownRemaining;
   ```
   (sits next to `PierceRemaining` — same config+state mix already there).
2. **`AoeEcsComponents.cs` — `AoeHitSpawnComponent`** — add:
   ```csharp
   public float OnHitSpawnCooldownRemaining;
   ```
3. **Reset on spawn/reuse — projectile.** `ProjectileSpawnApplySystem`: the
   `ProjectileHitComponent` is created around line 210 (`PierceRemaining`,
   `RepeatHitCooldownSeconds`, `HitPayload`). Add
   `OnHitSpawnCooldownRemaining = 0f` there. Confirm the same path runs for both
   cold-create and pool reuse (it does — spawn-apply is the single materialization
   site).
4. **Reset on spawn/reuse — AOE.** `AoeSpawnApplySystem` (~line 646 where
   `AoeHitSpawnComponent { HitPayload = ..., OnHitSpawn = cmd.OnHitSpawn }` is
   built) — add `OnHitSpawnCooldownRemaining = 0f`.
5. **Make `AoeHitSpawnComponent` RW in the AOE queries** (required before task
   003 can take it as `ref` — see `[[reference_ijobentity_explicit_query_enableable]]`):
   - `LingeringAoeCollisionSystem.OnCreate`: change
     `.WithAll<AoeHitSpawnComponent>()` → `.WithAllRW<AoeHitSpawnComponent>()`.
   - `ImpactAoeCollisionSystem.OnCreate`: same change.
   - Leave the `Execute` signatures as `in` for now (flip to `ref` in task 003)
     — RW in the query is harmless while the param is still `in`.

## Acceptance criteria

- Project compiles; existing AOE + projectile PlayMode simulation tests still pass
  (no behavior change expected).
- **Template-hash coverage check (load-bearing).** Add/confirm a test: compile
  two otherwise-identical loadouts differing only in impact `cooldownSeconds`.
  Their registered `TemplateKey`s must **differ**. If they collide,
  `CombatRoot.RegisterSpawnTemplate` / `RegisterTimedSpawnTemplate` hashes a field
  subset — extend that hash to include `OnHitSpawnRef.CooldownSeconds`. Record the
  outcome in the implementation log.

## Dependencies

- Task 001 (needs `OnHitSpawnRef.CooldownSeconds` to exist for the hash check).

## Scope

Small. Two component fields, two reset lines, two query flags, one hash test.
