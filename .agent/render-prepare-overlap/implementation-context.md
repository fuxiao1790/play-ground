# Implementation Context

## Architectural Decisions
- Split render matrix preparation from render submission inside `PresentationSystemGroup`.
- `CombatRenderPrepareSystem` schedules `RenderPrepareJob` at presentation `OrderFirst`.
- `CombatBatchedRenderSystem` becomes consume-only and joins dependencies before reading prepared elements.
- Use Unity ECS per-component dependency chaining. Do not add manual job-handle plumbing.

## Global Invariants
- Kinematics are final before presentation.
- Render preparation runs after simulation/apply; render submission remains in presentation.
- No global sync should occur between prepare and submit unless profiling proves otherwise.
- Presentation may write only presentation-owned render buffers/components.

## Ownership Boundaries
- `CombatRenderPrepareSystem` owns the render prepare query and type handles.
- `CombatBatchedRenderSystem` owns render resource registry lookup, batch filtering, draw submission, submit buffer, and active-count counters.
- `CombatApplyBridge` owns managed target replay only.
- `CombatVfxDispatchSystem` owns VFX queue draining/dispatch only.

## Data Flow
- `RenderPrepareJob` reads `CombatKinematicsComponent` and `CombatRenderComponent`.
- `RenderPrepareJob` writes `CombatRenderElement`.
- `CombatBatchedRenderSystem` reads `CombatRenderElement` grouped by `CombatRenderBatchId`, `ProjectileTag`, and `AoeTag`.

## Lifecycle / Allocation Rules
- Persistent native resources stay owned and disposed by the system that creates them.
- The prepare query is owned by the new prepare system.
- The submit buffer stays owned and disposed by `CombatBatchedRenderSystem`.

## ECS / Job / Threading Constraints
- Schedule prepare with `ScheduleParallel(renderPrepareQuery, Dependency)`.
- Assign the scheduled handle back to `Dependency`.
- Do not call `CompleteDependency()` in the prepare system.
- Call `CompleteDependency()` at the start of `CombatBatchedRenderSystem.OnUpdate`.

## Determinism Requirements
- Existing projectile/AOE matrix calculation remains unchanged.
- Newly spawned entities must be included in the same-frame presentation query.

## Producer / Consumer Separation
- Prepare system produces `CombatRenderElement`.
- Batched render system consumes `CombatRenderElement` and submits GPU draws.
- No second prepared-matrix representation is introduced.

## Reused Mechanisms
- Existing `CombatRenderMatrixUtility.ElementFor`.
- Existing ECS dependency chain.
- Existing render resource registry and batch submission path.

## Introduced Mechanisms
- New `CombatRenderPrepareSystem` file/system.

## Validation Requirements
- Project compiles.
- Only one `RenderPrepareJob` definition exists.
- No job safety errors should be introduced.
- Task 002 requires profiler evidence or a reported inability to capture.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`
