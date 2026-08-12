# Implementation Context

## Architectural Decisions
- Delete CombatRoot duplicate active-AOE counter path. CombatStatsGatherSystem is sole active-count producer; display reads CombatStatsDisplaySingleton.
- Pool cleanup query must express disabled Active entities and use IJobChunk query mask for popcount.

## Global Invariants
- No unsafe gameplay/shared-runtime code.
- Preserve trim threshold and calm-down-gate behavior.
- Game-object/display paths must not read CombatStatsSingleton.
- CombatRoot allAoeQuery remains unfiltered for teardown of disabled pool slots.
- Do not run Unity tests; review exported XML before reporting a pass.

## Lifecycle / Allocation Rules
- EntityQuery owners dispose their queries. Do not create an AOE count query.
- Pooling remains disable-in-place; cleanup destroys disabled slots only.

## ECS / Job / Threading Constraints
- Active is only enableable component in cleanup queries.
- WithDisabled<Active>() supplies disabled-Active matching mask.
- If useEnabledMask is false, mask contents are undefined; use chunk.Count.
- Use math.countbits and ChunkEntityEnumerator; keep PoolTrimJob Burst-compatible.

## Producer / Consumer Separation
- CombatStatsSingleton is ECS-internal accumulator.
- CombatStatsGatherSystem publishes CombatStatsDisplaySingleton for GameObject/debug readers.

## Reused Mechanisms
- EntityQueryBuilder.WithDisabled<Active>(), math.countbits, ChunkEntityEnumerator.

## Introduced Mechanisms
- None.

## Validation Requirements
- Static searches and source review after each task.
- User runs EditMode and PlayMode Unity commands, exporting XML under TestResults.

## Files / Systems Mentioned By The Plan
- Assets/Scripts/System/Core/CombatRoot.cs
- Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs
- Assets/Tests/PlayMode/AoePlayModeTests.cs
- Docs/performance.md
- Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs
