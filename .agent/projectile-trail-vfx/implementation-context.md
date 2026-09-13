# Implementation Context

## Architectural Decisions
- Projectile trails use existing `LineSegment` VFX shape and `CombatVfxRoot.Register`.
- Skill path only: direct `CombatRoot.ProjectileCommandFor` keeps default trail id `0`.
- One `ProjectileTrailVfxComponent` owns resolved trail id, width, authored step distance, and last emitted position.
- `ProjectileMovementSystem` emits distance-gated trail segments during shared movement; no new system.

## Global Invariants
- Trail is optional: id `0` means no emission and no distance math.
- Trail segments emit only after movement reaches authored `StepDistance`; compare squared distance without `sqrt` and advance last-emission state only after emission.
- `BasicAttackPrefab` must not hard-fail for absent or misconfigured optional trail; validator warns.
- VFX ids are shape-generic; `VfxEmit.EnqueueLineSegment` owns no-op/shape behavior.
- Directly authored movement-test projectile entities must carry every required movement-job component.

## Ownership Boundaries
- `BasicAttackPrefab`: authored asset/shape/width.
- `SkillDriver`: registration and template construction.
- Projectile spawn command/apply systems: resolved per-instance data materialization.
- Movement: VFX event production. VFX dispatcher: consumption.

## Data Flow
- Prefab -> `SkillDriver.RegisterProjectileVfx` -> runtime definition -> projectile spawn command -> trail ECS component (seeded at spawn) -> `ProjectileMovementSystem` -> line-segment VFX queue.

## Lifecycle / Allocation Rules
- Component added by spawn materialization, retained through root teardown, fully overwritten and reseeded to spawn position on pooled reuse.
- Use existing singleton queue and producer-handle combine pattern; no new allocation path.

## ECS / Job / Threading Constraints
- Movement job runs parallel across active, non-arming projectiles in both lanes; it mutates trail state by `ref`.
- It must use fail-loud `GetSingletonRW<CombatAoeVfxDispatchSingleton>()` and combine scheduled handle into `ProducerHandle`.

## Reused Mechanisms
- `VfxDataShape.LineSegment`, `CombatVfxRoot.Register`, `VfxEmit.EnqueueLineSegment`, `CombatAoeVfxDispatchSingleton`, `ProjectileSpawnApplyUtility.WriteCommon`.

## Introduced Mechanisms
- `ProjectileTrailVfxComponent`; BasicAttackPrefab trail fields; runtime/command trail fields; `RegisterProjectileVfx`; `ProjectileVisualWarning`.

## Validation Requirements
- Do not run Unity tests. Static inspection/search only.
- User must run named Unity EditMode/PlayMode tests and export `Logs/TestResults-<platform>.xml`; agent reviews XML before claiming test result.

## Files / Systems Mentioned By The Plan
- `BasicAttackPrefab`, `SkillLoadoutValidator`, `RuntimeProjectileDefinition`, `SkillDriver`, `ProjectileSpawnPipeline`, projectile apply systems/components/movement system, projectile fixture tests, VFX/projectile docs.
