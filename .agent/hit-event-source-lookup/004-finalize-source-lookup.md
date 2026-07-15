# 004 — Finalize reads payload via ComponentLookup[Source]

## Goal
`CombatApplyFinalizeSingleSystem` stops reading payload off the event and reads it
from the source entity with one direct index.

## Changes
[CombatApplyFinalizeSingleSystem.cs](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs)

1. `FinalizeCombatSingleJob` gains a read-only lookup:
   ```csharp
   [ReadOnly] public ComponentLookup<CombatHitPayload> PayloadLookup;
   ```
   Assign in `OnUpdate` via `GetComponentLookup<CombatHitPayload>(isReadOnly: true)`
   ([:175-186](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L175-L186)).

2. In `Execute` ([:208-263](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L208-L263)),
   resolve the payload from the source, then keep the existing logic verbatim:
   ```csharp
   Entity target = hit.Target;
   if (target == Entity.Null) continue;
   if (!PayloadLookup.HasComponent(hit.Source)) continue;   // defensive
   CombatHitPayload payload = PayloadLookup[hit.Source];
   // …bucket by target (unchanged)…
   if (payload.StackEffect.Enabled && acc.HasStackBuffer == 1) { … AccrueStack(buffer, payload.StackEffect, …) }
   if (payload.DirectDamageEnabled) { … baseAmount = max(0, payload.DamageAmount); isCrit = rng < payload.CritChance; … payload.CritMultiplier … }
   ```
   Replace every `hit.StackEffect` / `hit.DamageAmount` / `hit.CritChance` /
   `hit.CritMultiplier` / `hit.DirectDamageEnabled` / `hit.TargetProxy` with the
   `payload.*` / `hit.Target` equivalents. Crit seed still uses target index + frame
   + per-target hit index — unchanged and order-independent.

## Notes
- Single lookup, no `Kind` branch: both archetypes carry `CombatHitPayload` (001).
  `HasComponent` is a defensive skip only, not a projectile/AOE discriminator.
- The lookup is RO and finalize depends on the completed collision jobs, so there is
  no aliasing with the collision `in` reads. Burst-compatible.
- Behavior parity: the crit/damage/stack math is unchanged; only the payload's
  source moved from the event to the component.

## Acceptance
- Finalize reads no damage/stack fields off `CombatHitEvent` (it has none).
- Damage + stack results identical to pre-refactor for the same hits (verify via
  existing PlayMode sim tests once 005 updates their setup).

## Dependencies
Requires 001 (component) + 002 (event shape). Scope: small.
