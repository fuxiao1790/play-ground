# 001 — Burst snapshot data model + converters

**File:** `Assets/Scripts/System/Common/CombatHitElement.cs`
**Depends on:** none
**Scope:** medium (new struct + field additions + 2 converters; the cycle-safety is the crux)

## Change

1. Add two **cycle-free** fields to `AoeProjectileBurstSnapshot` (~line 142). Extend the constructor
   with optional trailing params so existing call sites/behavior are unchanged when omitted:
   - `AoeOnHitSpawnSnapshot ImpactAoe` — reuses the existing struct; represents trigger2 = AOE.
   - `BurstImpactProjectileSnapshot ImpactProjectile` — new struct (below); trigger2 = projectile.

2. Add `public readonly struct BurstImpactProjectileSnapshot`: the projectile spawn params mirrored
   from `ProjectileImpactProjectileSnapshot` (typeId, targetMask, count, spread, speed, lifetime,
   radius, halfExtents, rotationRadians, shapeType, damage, directDamageEnabled, pierceCount,
   repeatHitCooldownSeconds, visualScale, visualRotationDegrees, tracking) **plus** an optional
   `AoeOnHitSpawnSnapshot ImpactAoe` (the terminal projectile's own AOE-on-hit, if any).
   - It must **NOT** contain `StackEffectSnapshot` or `ProjectileImpactProjectileSnapshot` — both
     re-introduce the value-type cycle / unbounded depth. Inline any needed stack fields the way
     `AoeOnHitSpawnTailSnapshot` does, or omit them if a terminal projectile detonation never needs
     a stack effect (decide during impl by checking what `BuildImpactProjectileSnapshot` currently
     carries).

3. Add two static converter helpers (same file/namespace):
   - `ProjectileImpactAoeSnapshot ToProjectileImpactAoe(in AoeOnHitSpawnSnapshot snap, CombatFaction faction)`
     — map fields directly; `stackEffect: snap.BuildStackEffect(faction)`,
     `aoeSpawn: snap.NextAoeOnHitSpawn.ToSnapshot()`.
   - `ProjectileImpactProjectileSnapshot ToProjectileImpactProjectile(in BurstImpactProjectileSnapshot snap, CombatFaction faction)`
     — map fields; `impactAoe: ToProjectileImpactAoe(snap.ImpactAoe, faction)`.

## Acceptance criteria

- Project **compiles** — proves no value-type cycle was introduced (the central risk).
- `AoeProjectileBurstSnapshot` default-constructs to today's behavior (`ImpactAoe`/`ImpactProjectile`
  disabled when not supplied).
- No converter reaches `AoeProjectileBurstSnapshot` transitively (re-verify the cycle is not closed).
