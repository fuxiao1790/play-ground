# AOE on-hit projectile carries its own (2nd) trigger

## Summary

The game allows linking up to **2 triggers** in a loadout chain. We already support
`AOE --OnImpactProjectile--> projectile` (a top-level AOE spawning a projectile burst on hit).
What does not work is the *second* trigger after that projectile:

```
AOE  --trigger1(OnImpactProjectile)-->  projectile  --trigger2-->  skill2 (AOE or projectile)
```

The burst-spawned projectile cannot fire its own impact because `AoeProjectileBurstSnapshot` is a
**flat** struct with no impact payload, and `ProjectileSpawnPipeline.BuildBurstEvent` hard-codes the
projectile's `ImpactAoe`/`ImpactProjectile` to `default`. This plan makes the burst-spawned
projectile carry **one** terminal level of its own impact, exactly what the 2-trigger limit allows.

## The blocker (key architectural decision)

`AoeProjectileBurstSnapshot` **cannot** simply gain a `ProjectileImpactAoeSnapshot` /
`ProjectileImpactProjectileSnapshot` field — that forms a value-type struct cycle (C# compile error):

```
AoeProjectileBurstSnapshot
  -> ProjectileImpactAoeSnapshot.StackEffect (StackEffectSnapshot)
    -> DetonationSnapshot.ProjectileBurst (AoeProjectileBurstSnapshot)   <-- closes the loop
```
(`Assets/Scripts/System/Status/StackEffectSnapshot.cs:26`)

**Decision: reuse the codebase's existing cycle-free flattened struct.** `AoeOnHitSpawnSnapshot`
(`CombatHitElement.cs:294`) already inlines stack-detonation fields (no `StackEffectSnapshot`),
bounds depth via `AoeOnHitSpawnTailSnapshot` + `MaxStackChainLinks = 2`, and contains **no**
`AoeProjectileBurstSnapshot` — so it is safe to embed in the burst. Because trigger2's effect is
terminal (a 3rd trigger is disallowed), these flattened forms are sufficient; no recursive burst is
ever needed on the impact.

This keeps all work in the snapshot/translation ("outer") layer — no changes to ECS systems or
hot Burst jobs.

## Alternative considered (rejected)

**Template-key reference**: store the on-hit projectile as a registered `ProjectileSpawnEvent`
template (already carries full impacts) and have the burst hold a `Hash128` key resolved at hit
time. Avoids growing the snapshot, but requires injecting the projectile template registry
(`TimedSpawnSystem.ProjectileTemplates`) into the AOE-collision Burst job and reworking the
burst-fire + stacking-detonation paths — more hot-path/job plumbing than the recommended
snapshot-layer change. Revisit only if `AoeProjectileBurstSnapshot` size growth proves problematic.

## Tasks

| # | File | Change | Depends on |
|---|------|--------|-----------|
| [001](001-burst-snapshot-data-model.md) | `CombatHitElement.cs` | Extend `AoeProjectileBurstSnapshot`; add `BurstImpactProjectileSnapshot`; add converters | — |
| [002](002-buildburstevent-fire-path.md) | `ProjectileSpawnPipeline.cs` | `BuildBurstEvent` forwards impact payload | 001 |
| [003](003-translation-builders.md) | `SkillSpawnTranslator.cs`, `PlayerSkillDriver.cs` | Populate new burst fields from the projectile's impact defs | 001 |
| [004](004-compiler-drop-warning.md) | `SkillSetCompiler.cs` | Remove obsolete AOE-source "flat burst" warning | 002, 003 |
| [005](005-tests.md) | `Assets/Tests/PlayMode/...` | PlayMode + regression coverage | 001–004 |
| [006](006-docs.md) | `Docs/reference/game-logic/skill-system.md` | Document the 2-link AOE→projectile→skill path | 001–004 |

## Constraints & considerations

- **No registration changes needed.** `RegisterProjectileTypesRecursive` /
  `RegisterAoeTypesRecursive` already descend into `OnHitProjectileSpawnDefinition` and a
  projectile's `ImpactAoeDefinition` / `ImpactProjectileDefinition`
  (`PlayerSkillDriver.cs:218-224, 361-364`), so trigger2's type/prefab is already registered.
  Confirm during implementation.
- **Shared helper side effect (intended).** `BuildProjectileDetonationBurstSnapshot` is also used by
  stacking projectile detonations (`StatusProcessSystem.BuildProjectileDetonation` via
  `DetonationSnapshot.ProjectileBurst`); they will likewise gain the ability to carry their
  detonation-projectile's impact — consistent with the 2-link budget. Verify no regression.
- **Duplicated builders.** `BuildProjectileDetonationBurstSnapshot` and the impact-snapshot
  builders exist twice (in `SkillSpawnTranslator` and the nested `SkillIntervalTemplateBuilder` in
  `PlayerSkillDriver.cs`). Keep both copies in sync; do not attempt to merge them here.
- **Struct size.** `AoeProjectileBurstSnapshot` grows (it is embedded in `DetonationSnapshot`,
  `AoeHitSpawnComponent`, `AoeSpawnEvent`/`Command`). Blittable, acceptable; note it.
