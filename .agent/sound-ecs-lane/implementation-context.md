# Implementation Context

## Architectural Decisions
- Add one singleton-owned `NativeQueue<SoundEvent>` lane for Burst spawn producers.
- `SoundEventDispatchSystem` transports only; `AudioRoot` remains the sole ranking and playback owner.
- Remove managed root-cast emission so materialization, not input, is the one spawn-sound boundary.

## Global Invariants
- `SoundEvent` stays unmanaged and clip id `0` stays silent.
- Every materialized root or nested spawn uses its own definition's registered id and radius.
- Sound remains presentation-only and cannot affect combat.
- `PlayGround.Sim.asmdef` stays unchanged.

## Ownership Boundaries
- `SoundEventSingleton` owns its queue and combined producer handle.
- Simulation jobs write only through `SoundEmit` and a `ParallelWriter`.
- The dispatcher completes/drains the lane; `AudioRoot` owns all policy, pooling, and counters.
- Managed producers use `AudioRoot.Enqueue`, never the native lane.

## Data Flow
- Prefab clip/radius -> compiled runtime definition tree -> recursive clip registration -> spawn template/command -> expansion or armed-AOE completion -> native sound lane -> dispatcher -> `AudioRoot.Enqueue` -> one `LateUpdate` ranking pass.

## Lifecycle / Allocation Rules
- The dispatcher allocates the persistent queue in `OnCreate` and disposes it in `OnDestroy`.
- Producer handles complete before drain and reset afterward.
- Native and managed pending batches clear on every early-out path.
- Drain scratch is reused; no per-frame managed allocation.

## ECS / Job / Threading Constraints
- Jobs receive `NativeQueue<SoundEvent>.ParallelWriter`; no managed reference enters Burst code.
- Every producer combines its job into `SoundEventSingleton.ProducerHandle` on the main thread.
- Systems access the singleton, never another system's fields.

## Determinism Requirements
- Parallel queue order is meaningless; `AudioRoot` uses explicit ranking and tie-break fields.
- Volleys and echoes emit per entity and are reduced by existing per-clip policy.

## Producer / Consumer Separation
- Expansion owns projectile, targeted, and unarmed-AOE spawn occurrences.
- `CombatArmingSystem` owns the deferred armed-AOE occurrence.
- `SoundEventDispatchSystem` transports without selecting.

## Reused Mechanisms
- Singleton container/producer-handle lifecycle mirrors the combat VFX lanes.
- Recursive registration follows the existing skill definition-tree walks.
- AOE sound timing mirrors its arming/spawn VFX branch.

## Introduced Mechanisms
- `SoundEventSingleton`, `SoundEmit`, and `SoundEventDispatchSystem`.
- Sound id/radius fields on all three spawn command kinds.
- AOE identity persistence for the values needed at deferred arm completion.

## Validation Requirements
- Agents do not run Unity tests or the Unity test runner.
- Compile with Unity's current Roslyn response files and perform static/diff checks.
- User runs the named EditMode tests and exports `Logs/TestResults-EditMode-SoundEcsLane.xml` for review.
- Confirm `PresentationSystemGroup` versus `AudioRoot.LateUpdate` order in the Unity Profiler; do not guess.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Audio/`: lane, helper, dispatcher, existing event/root.
- Spawn command registries/builders and projectile/AOE/targeted expansion systems.
- `CombatArmingSystem` and `SkillDriver`.
- EditMode sound registration tests and maintained sound/presentation documentation.
