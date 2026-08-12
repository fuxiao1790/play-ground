# Task Execution Packet

## Task

001-combatroot-active-aoe-count.md

## Goal

Delete CombatRoot duplicate AOE counter path and replace stale test with display-mirror spawn coverage.

## Files Allowed To Modify

- Assets/Scripts/System/Core/CombatRoot.cs
- Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs
- Assets/Tests/PlayMode/AoePlayModeTests.cs
- Docs/performance.md

## Files Allowed To Create

- None

## Files Allowed To Delete

- None; delete specified code/test only.

## Behavior To Preserve

- allAoeQuery creation, disposal, and OnDestroy cleanup remain unfiltered.
- AOE spawn buffers and append behavior remain live.
- Shared AoePlayModeTests fixture helpers remain unchanged.

## Behavior To Change

- Remove ActiveAoeCount, Counters, spawnedAoes, and AoeRuntimeCounters.
- Replace stale counter test with CombatStatsDisplaySingleton spawn-total test.

## Relevant Global Context

- No new EntityQuery or data path. GameObject readers use display mirror, never CombatStatsSingleton.
- Do not assert ActiveAoes or EntitiesDespawned. Spawn test asserts ECB plus reuse total >= 1 after one frame.
- Do not run Unity tests; static validation only.

## Dependencies Confirmed

- None; task has no prerequisites. Existing source contains every planned deletion.

## Step-By-Step Instructions

1. Delete three field/property/counter references and ActiveAoeCount from CombatRoot.
2. Delete AoeRuntimeCounters but retain AoeSpawnRequest.
3. Correct performance documentation ownership sentence.
4. Replace stale play-mode test; add Combat.Stats namespace import.
5. Repo-wide search required names under Assets.

## Acceptance Criteria

- No ActiveAoeCount, Counters, spawnedAoes, or AoeRuntimeCounters in Assets.
- allAoeQuery survives intact; no new query.
- Stale test replaced by display-mirror spawn assertion.

## Validation Required

- Static search/source review; user later runs supplied Unity XML commands.

## Hard Boundaries

- No changes outside allowed files.
- No architecture changes or unrelated cleanup.
