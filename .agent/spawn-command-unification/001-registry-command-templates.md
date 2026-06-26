# 001 — Registry: command templates + unified registration

## Goal

Make the scope-owned spawn-template registry the single home for all follow-up
spawn data, and expose one registration API for every spawn kind. Encode the
write-only-external / read-only-during-tick contract in code.

## Changes

- [SpawnTemplateComponents.cs](../../Assets/Scripts/System/Common/SpawnTemplateComponents.cs):
  keep `ProjectileSpawnTemplate` / `AoeSpawnTemplate` (`NativeHashMap<Hash128,
  *SpawnEvent>`); the stored `*SpawnEvent` is the **command template** (volley
  params + per-entity data, per-instance fields default). Document on the struct
  that it is written only pre-tick and read `[ReadOnly]` during the tick.
- [CombatRoot.cs](../../Assets/Scripts/System/Common/CombatRoot.cs): generalize
  `RegisterTimedSpawnTemplate(in ProjectileSpawnEvent)` /
  `(in AoeSpawnEvent)` into the canonical `RegisterSpawnTemplate(...)` used by all
  follow-ups (impact AOE, impact projectile, AOE burst, AOE on-hit, detonation,
  interval, root cast). Same content-hash dedup; same "insert only if absent".
  Keep the old method names as thin aliases until 006 migrates callers.
- Add a depth-cap helper/constant (`MaxSpawnChainDepth = 3`) used by 006.

## Acceptance criteria

- Registry value is a template with per-instance fields default before hashing.
- Identical follow-up behavior dedups to one key; differing behavior yields a
  distinct key.
- No registry write path exists from inside any system/job (only `CombatRoot`
  register methods, called from managed code).
- All registry consumers can declare the map `[ReadOnly]`.

## Dependencies

None.

## Scope

Small–medium. Mostly additive + comments; the registry already exists.
