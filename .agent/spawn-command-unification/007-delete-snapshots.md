# 007 — Delete superseded snapshot structs

## Goal

Remove the embedded follow-up snapshot structs and any remaining dead code now that
every follow-up is a `(kind, key)` reference.

## Changes

- [CombatHitElement.cs](../../Assets/Scripts/System/Common/CombatHitElement.cs):
  delete `ProjectileImpactAoeSnapshot`, `ProjectileImpactProjectileSnapshot`,
  `AoeProjectileBurstSnapshot`, `AoeOnHitSpawnSnapshot`, `AoeOnHitSpawnTailSnapshot`.
- [StackEffectSnapshot.cs](../../Assets/Scripts/System/Status/StackEffectSnapshot.cs):
  remove the now-unused spawn fields from `DetonationSnapshot` (keep only what the
  keyed model needs, or fold it away entirely).
- Remove any leftover fields/params referencing the deleted types across requests,
  events, commands, and the runtime defs' translator surface.

## Acceptance criteria

- `grep` for each deleted type name returns no references in `Assets/`.
- Project compiles with no managed refs and no value-type cycle.
- `sizeof(AoeSpawnCommand) < 4096` still holds.

## Dependencies

005, 006 (all producers/consumers migrated first).

## Scope

Medium. Mechanical deletion, but wide; gated behind the migration.
