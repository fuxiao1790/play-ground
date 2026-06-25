# 003 — Populate burst impact fields in the translation layer

**Files:**
- `Assets/Scripts/Skills/SkillSpawnTranslator.cs` (`BuildProjectileDetonationBurstSnapshot`, ~line 411)
- `Assets/Scripts/Skills/PlayerSkillDriver.cs` (nested `SkillIntervalTemplateBuilder.BuildProjectileDetonationBurstSnapshot`, ~line 693)

**Depends on:** 001
**Scope:** medium (two duplicated copies, kept in sync)

## Change

`BuildProjectileDetonationBurstSnapshot` currently takes `(RuntimeProjectileDefinition projectile,
int targetMask)`. Change the signature to take the `CombatRoot root` (derive `targetMask` from
`root.TargetMask`) so it can call the existing impact-snapshot builders, then populate the new
`AoeProjectileBurstSnapshot` fields:

- `ImpactAoe` = `BuildAoeOnHitSpawnSnapshot(projectile.ImpactAoeDefinition, root, MaxAoeOnHitSpawnDepth)`
  (existing helper; returns `AoeOnHitSpawnSnapshot`, the type 001 added to the burst).
- `ImpactProjectile` = new small builder that maps `projectile.ImpactProjectileDefinition` (a
  `RuntimeProjectileDefinition`) into `BurstImpactProjectileSnapshot`. Reuse the field extraction
  already in each file's `BuildImpactProjectileSnapshot` (prefab radius/halfExtents/rotation/shape/
  visual, count/spread/speed/lifetime/pierce/repeat), and set its `ImpactAoe` via
  `BuildAoeOnHitSpawnSnapshot(impactProjectile.ImpactAoeDefinition, root, ...)`.

Update **both** call sites that invoke `BuildProjectileDetonationBurstSnapshot` to pass `root`
instead of `targetMask`:
- `SkillSpawnTranslator.SpawnAoe` and its `BuildAoeStackEffectSnapshot`/`BuildProjectileStackEffectSnapshot` usages.
- `SkillIntervalTemplateBuilder` equivalents in `PlayerSkillDriver.cs` (including
  `BuildProjectileStackEffectSnapshot` for stacking detonations).

Keep the two copies byte-for-byte equivalent in logic.

## Acceptance criteria

- AOE-source `OnImpactProjectileSpawnDefinition` whose projectile has an `ImpactAoeDefinition` or
  `ImpactProjectileDefinition` yields a burst snapshot with the corresponding impact enabled.
- Projectiles with no further impact yield a burst identical to today.
- Stacking projectile detonations build without error and (intentionally) now carry their
  detonation projectile's impact.
