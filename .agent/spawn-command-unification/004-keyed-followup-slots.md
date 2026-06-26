# 004 — Keyed follow-up slots on source components

## Goal

Replace the embedded follow-up snapshots on source components with `(kind, key)`
references, and break the `StackEffectSnapshot` cycle.

## Changes

- New small struct `OnHitSpawnRef { IntervalChildKind Kind; Hash128 TemplateKey; }`
  (default = disabled when key is default).
- [ProjectileSpawnPipeline.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs)
  `ProjectileHitPayload` / projectile hit component: drop `ProjectileImpactAoeSnapshot`
  and `ProjectileImpactProjectileSnapshot`; add one `OnHitSpawnRef`.
- [AoeEcsComponents.cs](../../Assets/Scripts/System/Aoe/AoeEcsComponents.cs)
  `AoeHitSpawnComponent`: drop `AoeProjectileBurstSnapshot ProjectileBurst` and
  `AoeOnHitSpawnSnapshot AoeSpawn`; add one `OnHitSpawnRef`.
- [StackEffectSnapshot.cs](../../Assets/Scripts/System/Status/StackEffectSnapshot.cs):
  `DetonationSnapshot` drops the embedded `AoeProjectileBurstSnapshot` /
  AOE-geometry spawn data; `StackEffectSnapshot` carries
  `(StackDetonationKind DetonationKind, Hash128 DetonationKey)` plus the existing
  `Contribution`, `DebuffKey`, `Threshold`, `Lifetime`.

## Acceptance criteria

- No component or hit payload embeds a spawn snapshot struct.
- `StackEffectSnapshot` no longer transitively contains `AoeProjectileBurstSnapshot`
  → the value-type cycle is structurally gone (verify it compiles with the field
  present, which previously triggered CS0523).
- A source can hold an on-hit spawn ref **and** a stacking detonation **and** an
  interval spawner simultaneously.

## Dependencies

001 (keys come from the registry).

## Scope

Medium. Touches the three hot payload structs; ripples to 005/006.
