# Implementation Context

## Architectural Decisions
- Use `Graphics.RenderMeshIndirect` for one combat sprite draw over all active projectile and AOE render entities.
- Per-instance matrix and atlas UV travel through `StructuredBuffer<CombatInstanceData>` indexed by `SV_InstanceID`.
- Keep the existing shared `SpriteAtlas`, shared unit quad mesh, per-entity `CombatRenderComponent.UvRect`, and active-only prepare system.

## Global Invariants
- Render data must not encode gameplay domain or faction; domain remains `ProjectileTag` / `AoeTag`, faction remains `CombatFaction`.
- Spawn, despawn, and reuse must not add render structural churn.
- `CombatRenderActiveTag` remains the active render filter.
- The combat atlas must be single page and contain every combat sprite.

## Ownership Boundaries
- `CombatRenderResourceRegistry` owns static GPU resources it creates: shared mesh and shared material.
- `CombatBatchedRenderSystem` owns the indirect instance buffer, indirect args buffer, and persistent instance list.
- Atlas assets and packed textures are referenced, not owned.

## Data Flow
- Registration computes folded native sprite scale and atlas `UvRect`.
- Spawn command copies `CombatRenderComponent` with `UvRect` onto pooled entities.
- `CombatRenderPrepareSystem` writes `CombatRenderElement.objectToWorld`.
- `CombatBatchedRenderSystem` scatters active projectile and AOE render data into one `NativeList<CombatInstanceData>`, uploads once, and submits one indirect draw.

## Lifecycle / Allocation Rules
- Native/GPU resources created by the render system are disposed in `OnDestroy`.
- Instance buffer grows monotonically and does not shrink.
- Zero-active frames skip upload and draw.

## ECS / Job / Threading Constraints
- `CompleteDependency()` remains before main-thread chunk reads.
- No structural changes are introduced by presentation submission.

## Determinism Requirements
- No gameplay simulation ordering changes.
- Existing render Z in `CombatRenderElement.objectToWorld` is preserved.

## Producer / Consumer Separation
- ECS combat simulation produces render components and matrices.
- Presentation consumes render data only; it does not affect spawn, damage, or faction logic.

## Reused Mechanisms
- `CombatRenderComponent.UvRect`
- `CombatRenderElement.objectToWorld`
- `CombatRenderResourceRegistry.SharedMesh`, `SharedMaterial`, `Atlas`, `AtlasTexture`
- Existing projectile and AOE render queries

## Introduced Mechanisms
- `CombatInstanceData` struct, 80-byte layout.
- Structured instance `GraphicsBuffer`.
- One-command indirect args `GraphicsBuffer`.
- `Combat/AtlasIndirectSprite` shader.

## Validation Requirements
- Runtime project must compile.
- Search must show no hot-path `SetVectorArray`, `MaxInstancesPerDraw`, or `RenderMeshInstanced` use in the render system.
- Manual on-screen and Frame Debugger validation remain required for shader matrix convention and one draw call.

## Files / Systems Mentioned By The Plan
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Docs/contracts/render-batch-data.md`
- `Assets/Atlas/Skills.spriteatlasv2`
- `Assets/Scenes/BenchmarkLarge.unity`
