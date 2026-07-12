# Task Execution Packet

## Task
004-projectile-topup.md

## Goal
Replace projectile ECB cold-create suffix with top-up before existing reuse fill.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Docs/profiling.md`
- `Assets/Scripts/System/Stats/CombatStatsComponents.cs`
- `.agent/spawn-pool-topup/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`

## Behavior To Preserve
- `ProjectileSpawnJob` fill logic remains unchanged except removing ECB suffix fallback fields/loop.
- Contact-gate seed, tracking enable bit, timed-spawn seed, and arming writes still happen in the job.
- `_deadSlotQuery` and `_archetype` remain unchanged.
- `HitPayloadFor`, `InitialTimedSpawnStateFor`, and `DeterministicJitter` remain.

## Behavior To Change
- `EnsureDisabledSlots` runs before chunk array/type-handle fetch.
- Reuse job fills every command.
- No ECB creation or playback for projectile spawns.
- Counter name changes from `ProjectileSpawnApplySystem.Cold` to `ProjectileSpawnApplySystem.TopUp`.

## Relevant Global Context
- Top-up creates disabled `Active` slots exactly equal to deficit.
- Structural changes must precede chunk and type handle fetch.
- `LastColdCreateCount` may hold top-up count for compatibility.

## Dependencies Confirmed
- `SpawnPoolTopUp.EnsureDisabledSlots` exists.
- Projectile dead-slot query uses `WithDisabled<Active>()`.
- AOE tasks are complete and do not affect projectile query/archetype.

## Step-By-Step Instructions
- Remove projectile create ECB.
- Call helper at the start of `SpawnMarker` block.
- Run existing reuse job against chunks.
- Remove `Ecb`/`Archetype` fields from `ProjectileSpawnJob`.
- Remove projectile job cold suffix loop.
- Rename counter field to `SpawnTopUpCounter` and profiler name to `.TopUp`.
- Remove `RecordCommonProjectileReset` and `RecordTimedSpawnReset` after confirming no other callers.
- Update profiling docs and stats comments to describe top-up-created counts.

## Acceptance Criteria
- Compiles; `RecordCommonProjectileReset`, `RecordTimedSpawnReset`, and old cold references removed with no dangling references.
- Top-up call happens before `ToArchetypeChunkArray` and type handle fetch.
- Cold start expected counters: `Reuse == totalRequests`, `TopUp == totalRequests`.
- Warm expected counters: `TopUp == 0`.

## Validation Required
- Search for old helper/counter references.
- Build/Unity compile if available.

## Hard Boundaries
- Do not modify files outside the allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
