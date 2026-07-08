# Implementation Context

## Architectural Decisions
- Replace the combat sprite custom indirect RendererFeature path with one persistent `MeshRenderer` submitted by the normal URP 2D renderer.
- Use Sorting Layers for tier order: `CombatVfx`, then `CombatSprites`, then `Default`.
- Keep player and mob renderers on `Default` so their existing Y-sort behavior stays intact.

## Global Invariants
- `CombatRenderComponent` stays 32 bytes and binary-compatible with the HLSL `CombatInstanceData`.
- `_InstanceData` and `_UvBasis` remain `GraphicsBuffer` structured buffers bound with `Material.SetBuffer`.
- Shader pass keeps `LightMode = Universal2D`.
- One combat sprite renderer, one mesh, one material, one draw submission.
- ECS component shapes and archetypes do not change.

## Ownership Boundaries
- `CombatRenderResourceRegistry` owns the shared material, capacity mesh, UV basis buffer, and persistent renderer GameObject.
- `CombatBatchedRenderSystem` owns the per-frame instance list and `_InstanceData` buffer.
- `CombatRoot` configures atlas and sorting values on the registry.

## Data Flow
- Render prep writes `CombatRenderComponent`.
- Batched render system scatters active projectile/AOE render components into `_InstanceData`.
- Registry mesh capacity grows as active count grows.
- Batched render system sets the active submesh index count to `activeCount * 6`.
- URP 2D `DrawRenderer2DPass` discovers and sorts the registry-owned `MeshRenderer`.

## Lifecycle / Allocation Rules
- Instance buffer grows by doubling and never shrinks.
- Capacity mesh grows by doubling and never shrinks.
- Zero active entities means a zero-index submesh, not renderer destruction.
- Registry cleanup destroys owned material, mesh, renderer GameObject, and UV buffer.

## ECS / Job / Threading Constraints
- No structural changes are added.
- Existing presentation-system scatter path remains main-thread after `CompleteDependency()`.
- No new ECS component or buffer type is introduced.

## Determinism Requirements
- Entity scatter order and render data shape are unchanged by this refactor.

## Producer / Consumer Separation
- Spawn and simulation data stay separate from presentation submission.
- VFX sorting changes must not alter VFX request payloads or simulation logic.

## Reused Mechanisms
- Existing atlas registration and UV basis computation.
- Existing `_InstanceData` upload.
- Existing render query and `WriteDirectJob`.
- Existing `BoundsHalfExtent`.

## Introduced Mechanisms
- Capacity-baked quad mesh with UV1 slot index.
- Registry-owned `MeshFilter` and `MeshRenderer`.
- Combat sprite sorting layer/order configuration on `CombatRoot`.
- `Mesh.SetSubMesh` active index range per frame.

## Validation Requirements
- Search confirms no old indirect handoff references remain after removal.
- `dotnet build` or Unity build-solutions validates C# compilation when possible.
- Manual Unity visual checks remain required for draw order, frame debugger, growth, and idle cost.

## Files / Systems Mentioned By The Plan
- `ProjectSettings/TagManager.asset`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRoot.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Scripts/System/Common/CombatIndirectRenderFeature.cs`
- `Assets/Settings/Renderer2D.asset`
- `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`
- `Docs/reference/simulation/combat-render-system.md`
- `Docs/contracts/render-batch-data.md`
