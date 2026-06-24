# 002 — CombatRoot registration API

## Scope

Add the `CombatRoot` method that registers a spawn template into the scope entity's registry
map, mirroring the existing `RegisterType`/`RegisterTemplate` managed-registration pattern.

## Changes

In `Assets/Scripts/System/Common/CombatRoot.cs`:

- `public Hash128 RegisterTimedSpawnTemplate(in ProjectileSpawnTemplateData data)` and an
  `(in AoeSpawnTemplateData data)` overload:
  1. `EnsureRuntimeReady()` (same guard as `Spawn`).
  2. Compute `Hash128 key = SpawnTemplateHash.Of(data)`.
  3. Fetch the registry component from `scopeEntity`
     (`entityManager.GetComponentData<ProjectileSpawnTemplate>(scopeEntity)`), and if
     `!map.ContainsKey(key)`, complete outstanding combat-world jobs
     (`entityManager.CompleteAllTrackedJobs()` or the tick-system dependency) and `map.Add(key, data)`.
  4. Return `key`.
- Insert-if-absent: registrations are idempotent; re-registering identical content is a no-op.
- This runs only at compile time (driven by `PlayerSkillDriver`, see 003) — never during active
  spawning — so the job-completion guard is rare and cheap.

## Acceptance criteria

- Registering the same template twice yields the same `Hash128` and a single map entry.
- Registering during active gameplay (loadout change) does not race a tick job (no safety-system
  error); the map contains the entry afterward.
- No exception when called before any spawn (scope entity + components already exist from 001).

## Dependencies

001 (registry components + hashing + scope ownership).
