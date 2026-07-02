# Implementation Context

## Architectural Decisions
- `CombatRenderBatchId` becomes one plain per-entity `IComponentData` int.
- `RenderTypeId` from spawn commands remains the source value minted by `CombatRenderResourceRegistry`.
- Spawn pooling must no longer partition by render batch id.
- Rendering must group by reading the plain batch id and scattering matrices into per-batch scratch buffers.
- Main-thread render scatter is accepted for this pass; parallel compaction is deferred.

## Global Invariants
- Every spawn path, cold and reused, must write `CombatRenderBatchId.Value = cmd.RenderTypeId`.
- Reused slots must never keep a stale batch id.
- AOE archetype variants remain separated by impact, lingering, and timed-lingering shape, not by batch id.
- Disabled `CombatRenderActiveTag` entities must not render.
- `LastActiveProjectileCount` and `LastActiveAoeCount` must keep reporting active renderable counts.

## Ownership Boundaries
- Spawn apply systems own entity creation, reuse, and component reset for projectiles and AOEs.
- `CombatRenderResourceRegistry` owns GPU resource registration and batch id/resource entries.
- `CombatBatchedRenderSystem` owns presentation submit and per-batch render scratch buffers.

## Data Flow
- Managed content resolves to `RenderTypeId`.
- Spawn commands carry `RenderTypeId`.
- Spawn apply writes `CombatRenderBatchId` and `CombatRenderComponent`.
- `CombatRenderPrepareSystem` writes `CombatRenderElement` matrices.
- `CombatBatchedRenderSystem` reads `CombatRenderBatchId` and `CombatRenderElement`, scatters by id, then submits with registry resources.

## Lifecycle / Allocation Rules
- `CombatRenderBatchId` is present in renderable archetypes at creation.
- Cold creation uses `SetComponent`, not `AddSharedComponent`.
- Reuse jobs write the component through chunk `ComponentTypeHandle`s.
- Render scratch buffers are reused and disposed on system destroy.
- No per-frame managed allocation should be introduced in render submit beyond existing ECS temporary chunk arrays.

## ECS / Job / Threading Constraints
- Reuse writes must stay Burst-safe.
- Component writes in reuse jobs use `ComponentTypeHandle<T>` with safety restrictions matching nearby reset writes.
- `CombatBatchedRenderSystem` completes dependencies before main-thread chunk reads.
- Avoid structural changes on high-count reuse paths.

## Determinism Requirements
- Spawn command order and existing reuse machinery stay intact.
- Projectile counting-sort/bucketing stays even though it becomes a single bucket.

## Producer / Consumer Separation
- Spawn systems produce render data components.
- Render systems consume render data only; they do not mutate spawn state.
- Registry remains the GPU resource lookup owner.

## Reused Mechanisms
- Projectile counting-sort reuse machinery.
- AOE `_byKey` bucketing for impact/lingering/timed-lingering variants.
- Existing `CombatRenderPrepareSystem` matrix preparation.
- Existing `SubmitAll` draw submission helper.

## Introduced Mechanisms
- Plain `CombatRenderBatchId` writes in cold and reuse spawn paths.
- Render-side per-batch scratch buffers keyed by batch id.
- Main-thread scatter from active render queries into those buffers.

## Validation Requirements
- Search proves no projectile/AOE/render/test use of `AddSharedComponent` or `SetSharedComponentFilter` remains for `CombatRenderBatchId`.
- Build and relevant PlayMode tests should be run when possible.
- New stale-batch-id reuse coverage must exist for projectile basic, projectile child, and AOE.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Docs/contracts/render-batch-data.md`
- `.agent/rendering-rework/context.md`
