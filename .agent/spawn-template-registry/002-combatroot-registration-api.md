# 002 — CombatRoot registration API

## Scope

Add the `CombatRoot` method that stores a spawn event into the scope-entity registry, mirroring
the existing `RegisterType`/`RegisterTemplate` managed-registration pattern.

## Changes

In `Assets/Scripts/System/Common/CombatRoot.cs`, add overloads:

- `Hash128 RegisterTimedSpawnTemplate(in ProjectileSpawnEvent evt)` and
  `Hash128 RegisterTimedSpawnTemplate(in AoeSpawnEvent evt)`:
  1. `EnsureRuntimeReady()`.
  2. `Hash128 key = SpawnTemplateHash.Of(evt)` (caller passes the event with per-instance fields
     default).
  3. Read the registry component from `scopeEntity`; if `!map.ContainsKey(key)`, complete
     outstanding combat-world jobs (`entityManager.CompleteAllTrackedJobs()`), then `map.Add(key, evt)`.
  4. Return `key`.
- Insert-if-absent (idempotent). Called only at compile time (`PlayerSkillDriver`), never during
  active spawning, so the job-completion guard is rare.

## Acceptance criteria

- Registering the same event twice → same `Hash128`, single map entry.
- Registering during gameplay (loadout change) does not race a tick job (no safety error); entry
  present afterward.
- No exception when called before any spawn (scope entity + maps exist from 001).

## Dependencies

001.
