# 003 — Id-keyed target stack state

## Change kind: adapt

## Structural role
Replaces the enum-indexed fixed array (source of the shared-counter and `StatusCount`
out-of-range bugs) with a buffer keyed by the registration-derived debuff key, storing the
accumulated contribution rather than a bare count.

## Change
- Remove `TargetStackStateComponent`'s `FixedList<int> Counts`, `StatusCount`,
  `IsValidStatus`, `EnsureLength`. Remove the `DebuffStatus`-as-key coupling.
- Add `DynamicBuffer<TargetStackEntry>` on the proxy archetype
  ([CombatTargetProxy.Archetype](Assets/Scripts/System/Common/CombatTargetProxy.cs#L173)):
  ```
  struct TargetStackEntry : IBufferElementData {
      int   DebuffKey;
      int   Count;
      float SummedDamage;       // sum of per-stack 1/X contributions
      int   SummedProjectileCount;
      float SummedArea;
      float LifetimeRemaining;
      DetonationSnapshot Detonation;   // kind tag (Aoe|Projectile) + type id + params; constant per key
  }
  ```
  The summed scalars (damage/count/area) and `DetonationSnapshot` are **detonation-kind
  agnostic** — projectile detonation reuses count/spread without new buffer fields. The buffer
  never changes when the projectile path is added (focus item 5).
- Lookup/insert by `DebuffKey` (linear scan; the buffer is small per target). Bounded cap
  sized above realistic concurrent stacking skills so eviction is not reached in practice;
  if reached, eviction (fizzle the entry with the least `LifetimeRemaining`) must bump an
  observable counter, never silently drop (focus item 7).

## Structural notes
- The buffer dies with the proxy (lifetime owned by `MobRoot`). Only `StackAccrualSystem`
  writes it (single-writer; document on the buffer).
- No enum, no fixed array size to keep in sync — the prior fragility is gone by design.

## Acceptance criteria
- Stack state is a per-proxy buffer keyed by debuff key; no `DebuffStatus` indexing.
- Adding a new cosmetic status never affects accrual.

## Dependencies
002.

## Scope
Medium.
