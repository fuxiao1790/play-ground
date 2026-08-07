# Implementation Context

## Architectural Decisions
- Add `Targeted` combat domain beside Projectile/Aoe. Single hit uses `TargetedSkill`; interval uses `LingeringTargetedSkill` and `LingeringTargetedTag`.
- Target selection uses existing target spatial-hash snapshot: first near acquisition anchor, then nearest valid target from each prior link. No simulated geometry or new spatial structure.
- Keep additive typed spawn lanes: targeted and lingering targeted each own event, expansion, and apply lane.
- Per-link damage uses `CombatHitEvent.DamageScale`; do not mutate source payload or spawn one entity per link.
- Chain state has inline fields and one immediate-previous-target exclusion key. No liveness lookup.

## Global Invariants
- Domain systems explicitly require domain tags; `Active`, scope, or shared data never imply domain/faction.
- Simulation jobs use ECS/native data only. No GameObjects, Transforms, Colliders, ScriptableObjects, or managed companions.
- Events express gameplay intent; expansion owns template lookup and one-command-per-entity multiplication; commands express allocation intent.
- Registry changes only managed pre-tick; simulation reads it read-only.
- Hot despawn disables enableable components. Reuse disabled pool slots before cold creation.
- Components/tags/buffers include current `ECS Lifecycle:` comments.
- No managed allocations/LINQ/closure capture/per-entity growing native containers in hot combat paths.
- Native handles disposed by owning system.

## Ownership Boundaries
- Cross-system lanes are singleton components with native containers plus explicit `ProducerHandle`/`PendingHandle`; systems never access another system's fields.
- Missing lane singleton is a broken world: direct singleton access, not a silent skip.
- `CombatRoot` owns managed template registration and public spawn submission; Game Logic creates plain snapshots; simulation resolves/applies; presentation consumes VFX/results.

## Data Flow
- Spawn producer -> typed spawn event lane -> expansion -> typed spawn command lane -> apply/pool -> next-frame resolve.
- Resolve -> `CombatHitEvent` lane -> finalizer -> `CombatTickResult` -> managed bridge.
- Resolve emits `LineSegmentVfxSpawn` through VFX lane; it is presentation-only.

## Lifecycle / Allocation Rules
- `Active` and gate tags are enableable. Newly spawned entities act next simulation update.
- Initial delay uses `ArmingTag` + `CombatArmingComponent`; lifetime is common timer data.
- Pool cleanup must include both targeted archetypes; spawn/despawn counters feed combat stats.

## ECS / Job / Threading Constraints
- Spatial consumers combine broadphase `BuildHandle` into dependency and publish to `ConsumerHandle`.
- Lane sinks complete stored handles before drain, and dispose native containers on teardown.
- No random health lookup during targeted resolve; finalization aggregation makes it stale anyway.

## Determinism Requirements
- Query ordering/selectors follow task specification. Parallel native queues are unordered; consumers treat them as sets.

## Producer / Consumer Separation
- Damage events may contain `DamageScale`, never spawn-routing data. Spawn events must not hold target-replay-only data.

## Reused Mechanisms
- TargetSpatialHash snapshots; combat hit/finalize/tick result path; Active pooling; arming/lifetime; render alignment; LineSegment VFX; mana gate; StatFold/AreaSize; interval trigger/timed spawn.

## Introduced Mechanisms
- `TargetedTag`, `LingeringTargetedTag`, `TargetedChainComponent`, int-only `TargetedVfxIds`, visual-only `TargetedVfxSizeComponent`, `TargetedVfxUtility`; targeted and lingering targeted events/commands/registry lanes; targeted expansion/apply/resolve systems; targeted authoring/runtime definitions/triggers/support.

## Validation Requirements
- Execute each task's EditMode/static/build validation before next task. Stop after missing dependency, failed validation, or architectural ambiguity. Do not hand-edit Unity YAML; authored asset/VFX/loadout steps remain user work.

## Files / Systems Mentioned By The Plan
- Combat hit finalizer, target spatial hash, spawn lanes/cores/apply, lifetime/arming/pool cleanup, render/VFX, CombatRoot/spawn gate, skill compiler/driver/translators, supports/triggers, validation, tests, docs.
