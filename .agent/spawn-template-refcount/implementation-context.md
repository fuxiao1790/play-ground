# Implementation Context

## Architectural Decisions
- Template command maps remain unchanged and Burst-readable. Lifetime data is in sidecar count maps keyed by the same `Hash128`.
- Identity is `(IntervalChildKind, Hash128)`; impact and lingering AOE share one AOE registry.
- Ad-hoc `CombatRoot.Spawn` registrations are pinned; managed skill registrations are reference-counted.

## Global Invariants
- Reclaim only when unpinned with `OwnerCount == 0` and `InstanceCount == 0`.
- Unknown/default unregister is a no-op. Missing command lookup remains a silent continue.
- Do not change `CombatDeathUtility.Kill` — it only receives `EnabledRefRW` handles and
  cannot see key components. Release events are emitted beside each `Kill` call instead.
- Spawn and despawn systems never touch a count. They enqueue `SpawnTemplateRefDelta`
  and hold nothing but a `ParallelWriter`.
- `InstanceCount` is live entities only. One acquire per spawn, one release per despawn.
- Any job reading `TimedSpawnComponent` for release must declare `[WithPresent]`.

## Ownership Boundaries
- `CombatScopeOwner` allocates/disposes all scope-owned native maps and queues with `Allocator.Persistent`.
- `CombatRoot` owns managed registration claims.
- Spawn apply and pool trim only enqueue instance deltas.
- `SpawnTemplateRefCountSystem` alone drains deltas and erases both paired map entries.
- `SkillDriver` owns its registration-key lifecycle.

## Data Flow
- Managed registration increments owners before simulation.
- Jobs enqueue key deltas; late simulation applies them after all simulation work.
- Pool reuse removes old component-key references then adds new references; trim removes remaining references.

## Lifecycle / Allocation Rules
- Scope state is added on first acquire and disposed on final release, including world reset paths.
- Instance count includes live and pooled-disabled entities until their key-bearing components are overwritten or the entity is destroyed.

## ECS / Job / Threading Constraints
- Template maps immutable throughout a simulation tick and read concurrently as `[ReadOnly]`.
- Jobs never access count maps; they use `NativeQueue<SpawnTemplateRefDelta>.ParallelWriter`.
- Singleton state is read directly where required; no `TryGetSingleton` or `RequireForUpdate` fallback.
- Sweep is single-threaded in `LateSimulationSystemGroup`, after `CombatPoolCleanupSystem`.

## Reused Mechanisms
- Scope owner native-container lifecycle, singleton-owned `NativeQueue`, materialization funnels, late cleanup group.

## Introduced Mechanisms
- `SpawnTemplateRegistryState`, `SpawnTemplateRefCount`, `SpawnTemplateRefDelta`, `SpawnTemplateRefEmit`, `SpawnTemplateRefCountSystem`, and `CombatRoot.UnregisterSpawnTemplate`.

## Validation Requirements
- Do not run Unity tests. User must run PlayMode and EditMode test commands with XML export; review XML before claiming pass.
- Use static/code checks per task.

## Files / Systems Mentioned By The Plan
- `SpawnTemplateComponents`, `CombatScopeOwner`, `CombatRoot`, spawn apply systems, `CombatPoolCleanupSystem`, `SkillDriver`, registry/API/skill docs, listed test fixtures.
