# Implementation Context

## Architectural Decisions
- Remove only the projectile trail producer; shared `LineSegment` VFX infrastructure (enum/event/queue/GPU buffers) stays because `TargetedResolveSystem` still produces it for chain links.
- Delete the data path outright (fields, component, registration, assets). No disable flag, zero-id placeholder, or compatibility shim.
- `ProjectileMovementSystem` returns to pure simulation: integrate position, refresh collision bounds. No VFX singleton resolution, no queue writes, no producer-handle merge.
- `PlagueLink.vfx` becomes the fixture for generic AgentVFX read-compatibility coverage (replacing the deleted projectile trail graph).

## Global Invariants
- Remove trail values from every layer in the same change (authoring → runtime → command → ECS). No stale source of truth left in one layer.
- Gameplay behavior (velocity, collision bounds, arming, lifetime, collision, damage, spawn expansion, render identity) must not change.
- No new allocation, managed lookup, structural change, event lane, or per-frame compatibility check introduced anywhere.

## Ownership Boundaries
- Authoring: `BasicAttackPrefab`. Compiled managed data: `RuntimeProjectileDefinition`. Unmanaged spawn snapshot: `ProjectileSpawnCommand`. Runtime state: pooled ECS entities.
- `TargetedResolveSystem` remains the sole LineSegment producer after this change; do not touch its path.

## Data Flow
- Trail fields must be removed from: `BasicAttackPrefab` → `RuntimeProjectileDefinition` → `ProjectileSpawnCommand` → `ProjectileTrailVfxComponent` (ECS) → `ProjectileMovementSystem` emission → `LineSegmentVfxEvent` queue. The queue/dispatcher/event type itself is shared infrastructure and is NOT deleted (targeted links still use it).

## Lifecycle / Allocation Rules
- Both discrete and continuous projectile archetypes currently carry `ProjectileTrailVfxComponent`; pooled slots reset it on reuse. Remove declaration, both archetype entries, handles, native-array access, `WriteCommon` argument, and reset write together.
- ECS lifecycle comments must be deleted alongside the component they document (no replacement comment).

## ECS / Job / Threading Constraints
- VFX producers write via `NativeQueue<T>.ParallelWriter` and combine job handles into `CombatAoeVfxDispatchSingleton.ProducerHandle`; dispatcher completes it before draining.
- `ProjectileMovementSystem` must stop acquiring `PendingLineSegmentSpawns` and stop combining its handle into the VFX producer handle. Position integration and bounds refresh job body must stay byte-for-byte equivalent otherwise.

## Determinism Requirements
- Not explicitly called out beyond "gameplay behavior unchanged" — no RNG/seed concerns in this removal.

## Producer / Consumer Separation
- `TargetedResolveSystem` is the only remaining LineSegment producer. Do not remove `LineSegmentVfxEvent`, queue alloc/disposal, bucket lists/job, `DrainAndDispatchLineSegment`, `LineSegmentVfxResources`, dispatcher upload, validation contract, or `VfxEmit.EnqueueLineSegment`.

## Reused Mechanisms
- Batched projectile sprite rendering (only visual path remaining for projectiles).
- Existing spawn expansion, pooled archetypes, movement integration, collision bounds refresh, collision, lifetime, arming systems.
- Existing LineSegment request/dispatch path for targeted links.
- `PlagueLink.vfx` as AgentVFX read-compatibility fixture.

## Introduced Mechanisms
- None. No runtime mechanism, data type, adapter, fallback, or replacement trail.

## Validation Requirements
- Agents do not run Unity tests; user runs EditMode/PlayMode suites and exports XML under `Logs/` (see task 004 for exact test list and file names).
- Validation here is search-based (grep for removed symbols) plus manual code review; no `.sln`/compiler available in this environment.
- Editor-only steps (prefab YAML cleanup via reserialize, manual scenario checks) are the user's to run, not agent-executed.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Authoring/BasicAttackPrefab.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`, `Assets/Scripts/Skills/SkillValidationWarning.cs`
- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`
- `Assets/Prefabs/Skills/Projectile/MagicBolt.prefab`, `MagicBolt2.prefab`, `FireArrow.prefab`
- `Assets/Vfx/LineSeg/MagicBoltTrail.vfx`, `FireArrowTrail.vfx`, `PlagueLink.vfx` (kept)
- `Assets/Sprite/Skills/magic-bolt-2-trail.png`, `fire-arrow-trail.png`, `fire-arrow-trail-v2.png`
- `Assets/Tests/EditMode/AgentVfxReadCompatibilityTests.cs`
- `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs`, `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`, `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Docs/reference/simulation/projectile-system.md`, `Docs/reference/game-logic/skill-system.md`, `Docs/reference/simulation/vfx-system.md`, `Docs/contracts/vfx-requests.md`

## Session-Specific Execution Note
- Task 003 touches `.prefab` YAML (removing obsolete serialized fields). Per standing user feedback ([[editor-steps-are-user-steps]]), hand-editing prefab/meta YAML for editor-native operations is not done directly — that sub-step becomes precise instructions for the user instead. Asset deletion (file + `.meta` pairs) and the C# test-fixture path change in task 003 are still executed directly, since they are plain file removal / ordinary code edits, not editor wiring.
