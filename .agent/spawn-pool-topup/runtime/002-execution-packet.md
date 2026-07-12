# Task Execution Packet

## Task
002-impact-aoe-topup.md

## Goal
Replace impact AOE ECB cold-create suffix with top-up before existing reuse fill.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Docs/profiling.md`
- `.agent/spawn-pool-topup/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Spawning/SpawnPoolTopUp.cs`

## Behavior To Preserve
- `ImpactAoeSpawnJob` chunk fill logic remains unchanged except removing ECB suffix fallback fields/loop.
- `_deadSlotQuery` and `_impactArchetype` remain unchanged.
- Stats compatibility fields remain updated.

## Behavior To Change
- `EnsureDisabledSlots` runs before chunk array/type-handle fetch.
- Reuse job fills every command.
- No ECB creation or playback for impact AOE spawns.
- Counter name changes from `ImpactAoeSpawnApplySystem.Cold` to `ImpactAoeSpawnApplySystem.TopUp`.

## Relevant Global Context
- Top-up creates disabled `Active` slots exactly equal to deficit.
- Structural changes must precede chunk and type handle fetch.
- `LastColdCreateCount` may hold top-up count for compatibility.

## Dependencies Confirmed
- `SpawnPoolTopUp.EnsureDisabledSlots` exists in `PlayGround.System.Combat.Spawning`.
- Impact system already imports `PlayGround.System.Combat.Spawning`.
- Impact dead-slot query uses `WithDisabled<Active>()` and excludes `LingeringAoeTag`.

## Step-By-Step Instructions
- Remove impact create ECB.
- Call helper at the start of `SpawnMarker` block.
- Run existing reuse job against chunks.
- Remove `Ecb`/`Archetype` fields from `ImpactAoeSpawnJob`.
- Remove impact job cold suffix loop.
- Rename counter field to `SpawnTopUpCounter` and profiler name to `.TopUp`.
- Remove `RecordImpactReset` after confirming no other callers.

## Acceptance Criteria
- Compiles; `RecordImpactReset` removed with no dangling references.
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
