# 002 — Switch collision producers to the map; retire the old damage path

**Change:** adapt · **Depends:** 001 · **Scope:** medium

## Goal

Activate the HP path: collision jobs write `CombatHitEvent` (damage half) into
the bucketed map instead of emitting `DamageReplayEvent`. Remove the old
single-threaded damage systems in the same change so there is no double emission
and no behavior overlap. Crit now rolls in ECS.

## Changes

1. **Collision jobs** — in each producer, replace the `DamageReplayEvent`
   enqueue with `StackApplyWriter`-style `HitMap.AsParallelWriter().Add(target,
   new CombatHitEvent { ...damage fields... })`:
   - [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs)
     (the damage emission paired with the existing stack emit at
     [:310-322](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L310)).
   - [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs)
   - [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs)
   - any shared path in
     [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs).
   Leave the `StackEffect` field default for now (filled in 003).

2. **Producer handle wiring** — where each collision system forwards its
   `collisionHandle` into `damageBridge.ProducerHandle`
   ([ProjectileCollisionSystem.cs:132-134](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L132)),
   forward it into `HitApplyFinalizeSystem.ProducerHandle` instead.

3. **Remove** `DamageFinalizeSystem` and `DamageDispatchBridge`
   ([DamageDispatchBridge.cs](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs))
   and the `DamageQueue` / `FinalizedDamageEvents` plumbing.

4. **Remove** `DamageReplayEvent`
   ([CombatHitElement.cs:395-409](../../Assets/Scripts/System/Common/CombatHitElement.cs#L395))
   and any remaining references.

## Acceptance criteria

- Damage applies to mobs/player through the new pipeline; HP, hurt flash, death
  all behave as before (death still decided by the GameObject).
- ECS never reads HP — confirm no new `TargetHealth`/HP component was introduced.
- Crit is rolled in the finalize job; `UnityEngine.Random` no longer appears in
  the damage path. Crit visuals/behavior still occur (now deterministic).
- No `DamageReplayEvent` / `DamageFinalizeSystem` / `DamageDispatchBridge`
  references remain; project compiles.
- Existing damage playmode tests pass (adjust crit assertions for deterministic
  RNG if they relied on `UnityEngine.Random`).

## Notes / risks

- Stack emission (`StackApplyEvent`) is **untouched** here — `StackAccrualSystem`
  still runs off its own queue. The two paths coexist for one task; 003 unifies.
- Watch the `IsTargetUsable` / `ResolveTarget` guards
  ([DamageDispatchBridge.cs:176-192](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L176))
  — port them verbatim into `HitApplyBridge` so dead/destroyed proxies are skipped.
