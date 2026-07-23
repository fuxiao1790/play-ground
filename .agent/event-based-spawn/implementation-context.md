# Implementation Context

## Architectural Decisions
- Managed code submits one plain `CombatSpawnRequest`; ECS intake later authorizes, creates lane events, and replies with `CombatSpawnResult`.
- The three managed scope-event producer paths are removed only in task 003. Intake feeds the existing native queue lanes.
- Spawn results mirror combat health results and resolve casters only through `TargetCompanion` in a presentation bridge.

## Global Invariants
- Spawn requests/results contain only unmanaged data; `Entity` is the only caster handle.
- `Entity.Null` denotes an ownerless caster.
- Spawn template registration remains a managed pre-tick action; simulation never writes the registry.
- Preserve managed source-id, jitter, and AOE-stat allocation this change.

## Ownership Boundaries
- `CombatScopeOwner` owns the shared scope entity and scope submission buffers.
- The spawn result lane is an ECS system singleton, not scope-owned.
- Managed targets are resolved only at the ECS-to-managed bridge.

## Data Flow
- Current: managed root writes three scope event buffers, then expansion merges those buffers with native queues.
- Target: managed root writes one request buffer; intake drains it to native queues and result lane; expansion drains queues; bridge replays results.

## Lifecycle / Allocation Rules
- Requests are transient scope-buffer elements drained during simulation.
- Results are transient native-list elements created/disposed by `SpawnIntakeSystem`.
- Existing scope event buffers remain until task 003.

## ECS / Job / Threading Constraints
- Intake is a main-thread simulation system ordered before `TimedSpawnSystem` and expansions.
- Native queue producer handles govern queue draining; do not access another system's private fields.

## Determinism Requirements
- Preserve source ids and jitter seeds supplied by managed submission.
- No reliance on cross-element native queue order.

## Producer / Consumer Separation
- Requests are pre-authorization caster intent; spawn events are ECS-internal post-authorization intent.
- Do not add managed references to request/event data.

## Reused Mechanisms
- Scope `DynamicBuffer` ownership, existing spawn native queue singletons, and `CombatApplyResultSingleton`/bridge lifecycle pattern.

## Introduced Mechanisms
- `CombatSpawnRequest`, `CombatSpawnResult`, `CombatSpawnResultSingleton`, `SpawnIntakeSystem`, and `CombatSpawnResultBridge`.

## Validation Requirements
- Validate each task before continuing; stop on failure or architectural ambiguity.
- Run the task's specified compile/test/static validation where possible.

## Files / Systems Mentioned By The Plan
- `CombatScopeOwner`, `CombatRoot`, `SkillDriver`, `SkillSpawnTranslator`
- projectile/AOE spawn pipelines and expansion systems
- `CombatApplyResults`, `CombatApplyBridge`, `ICombatTarget`
- `SpawnIntakeSystem` and play-mode spawn tests
