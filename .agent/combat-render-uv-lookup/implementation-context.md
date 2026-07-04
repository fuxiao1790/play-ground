# Implementation Context

## Architectural Decisions
- Static atlas UV basis moves from per-instance `CombatRenderComponent` data to a registry-owned per-kind GPU table.
- Per-instance GPU record and ECS component remain binary-identical for zero-copy scatter.
- New instance layout is `Matrix4x4 objectToWorld` plus one packed `int RenderMeta`, stride 68.
- `RenderMeta` bits 0..30 are `renderId`; bit 31 is CPU-only align-to-velocity.

## Global Invariants
- Shader, C# component, and graphics buffer stride must stay in lockstep.
- `_nextRenderId` starts at 1; render id 0 is non-renderable and must have safe UV slot 0.
- UV affine basis math stays unchanged and must preserve atlas rotation/flip.
- Matrix path remains column-major, untransposed, `mul(M, v)`.

## Ownership Boundaries
- `CombatRenderResourceRegistry` owns shared mesh, material, atlas reference, per-kind entries, and the new UV basis buffer.
- `CombatBatchedRenderSystem` owns per-frame instance and args buffers.
- GPU/native handles are disposed by their owners.

## Data Flow
- Registry computes per-kind `UvOriginU`/`UvV` at registration and uploads a full `_UvBasis` table lazily when dirty.
- Spawn templates keep render id and visual transform metadata, but no longer copy UV basis to instances.
- Render scatter copies `CombatRenderComponent` directly into `_InstanceData`.
- Shader reads instance `renderMeta`, masks render id, then indexes `_UvBasis`.

## Lifecycle / Allocation Rules
- UV basis buffer is rebuilt only when register/unregister changes kind set.
- Per-frame path must not allocate for UV basis when the registry is clean.
- Buffer slot 0 is zeroed and unused.

## ECS / Job / Threading Constraints
- Shared combat components stay domain-neutral.
- Render prepare remains Burst/job based and only writes matrices.
- Render submission completes dependencies before main-thread GPU upload.

## Determinism Requirements
- Preserve render id values and entity render behavior except UV storage location.

## Producer / Consumer Separation
- Registry is source of truth for per-kind UV basis.
- Render system binds buffers but does not recompute UV basis.
- Shader consumes `_InstanceData` and `_UvBasis`.

## Reused Mechanisms
- Existing `Entries` table.
- Existing `Material.SetBuffer` pattern for indirect draws.
- Existing graphics buffer grow/dispose pattern.
- Existing zero-copy component-to-instance scatter.

## Introduced Mechanisms
- `CombatUvBasis` sequential struct, stride 32.
- Registry `_uvBasisBuffer` and `_uvDirty` flag.
- `EnsureUvBasisBuffer()` public registry method.
- Shader `_UvBasis` structured buffer lookup.

## Validation Requirements
- `UnsafeUtility.SizeOf<CombatRenderComponent>() == 68`.
- No component UV fields/properties remain.
- `_UvBasis` is bound with `Material.SetBuffer`.
- Docs reflect 68-byte instance plus separate per-kind UV table.
- Relevant Unity Edit/PlayMode tests compile and pass when runnable.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Docs/reference/simulation/combat-render-system.md`
- `Docs/contracts/render-batch-data.md`
- PlayMode tests touching `CombatRenderComponent`
