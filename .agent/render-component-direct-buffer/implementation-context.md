# Implementation Context

## Architectural Decisions
- Merge render matrix and UV upload data into `CombatRenderComponent`.
- `CombatRenderComponent` is the GPU instance data and must remain 96 bytes.
- Disabled render entities are uploaded with a degenerate matrix instead of being filtered out.
- Batched rendering writes all render components directly to the GPU buffer.
- Shader stays unchanged; zero-scale instances naturally draw no pixels.

## Global Invariants
- GPU stride is 96 bytes: `Matrix4x4 objectToWorld` + `Vector4 uvOriginU` + `Vector4 uvV`.
- `CombatRenderActiveTag` remains enableable and is still the render active state.
- Rendering is presentation-only; projectile and AOE simulation ownership does not move.
- Preserve visual output for active projectiles and AOEs.

## Ownership Boundaries
- Spawn/registry code owns static render resource data and initial render component values.
- `CombatRenderPrepareSystem` owns per-frame matrix preparation.
- `CombatBatchedRenderSystem` owns GPU buffer capacity, args buffer, and render submission.
- Collision/lifetime systems may read render visual size for VFX sizing.

## Data Flow
- Registry builds UV basis and initial visual transform data.
- Spawn apply copies `CombatRenderComponent` onto projectile/AOE entities.
- Prep updates `objectToWorld` for every entity with a render component and active tag.
- Batched render uploads component data in query order and submits one indirect draw.

## Lifecycle / Allocation Rules
- No per-frame `NativeList` for render upload.
- GPU buffers are grown by capacity and disposed by `CombatBatchedRenderSystem`.
- Entity archetypes should avoid adding/removing render components at runtime.

## ECS / Job / Threading Constraints
- Prep runs in `PresentationSystemGroup` with Burst `IJobChunk`.
- Batched render runs after prep in `PresentationSystemGroup` and must `CompleteDependency()` before buffer lock/write.
- Queries that need disabled render entities must use `EntityQueryOptions.IgnoreComponentEnabledState`.

## Determinism Requirements
- No change to projectile/AOE spawn order or deterministic ids.
- Upload order can remain query/chunk order as before.

## Producer / Consumer Separation
- Spawn systems produce component values.
- Prep mutates only render matrix data.
- Batched render consumes prepared component values and does not recompute gameplay state.

## Reused Mechanisms
- `CombatRenderMatrixUtility.ElementFor(...)` remains the active matrix math source.
- `CombatRenderResourceRegistry` remains the render id and UV source.
- `CombatIndirectRenderFeature` and `DrawMeshInstancedIndirect` remain the render path.

## Introduced Mechanisms
- Direct `GraphicsBuffer.LockBufferForWrite<T>` / `UnlockBufferAfterWrite<T>` upload.
- Unified render query for projectile and AOE entities.
- Degenerate matrix for disabled render entities.

## Validation Requirements
- Build/compile succeeds.
- Search confirms `CombatRenderElement` and `CombatInstanceData` are removed.
- Search confirms no render scatter loop or `NativeList<CombatInstanceData>` remains.
- Shader file remains unchanged for task 004.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- Render-related play mode tests that directly reference removed component types.
