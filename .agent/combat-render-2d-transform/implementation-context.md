# Implementation Context

## Architectural Decisions
- Use the refactor path from `index.md`: `CombatRenderComponent` becomes the 32 B GPU wire record again.
- Move authored base scale/rotation out of spare matrix cells into CPU-only `CombatRenderAuthoring`.
- Preserve zero-copy upload from `CombatRenderComponent` chunk arrays into the render upload list.

## Global Invariants
- Uploaded StructuredBuffer stride must equal `UnsafeUtility.SizeOf<CombatRenderComponent>()`.
- Inactive pooled entities are still uploaded and must collapse to zero-area quads by zeroing the 2x2 basis.
- No per-frame structural churn; new authoring component belongs in base projectile/AOE archetypes.
- Render id bits stay in `RenderMeta` bits 0..30; align-to-velocity stays in bit 31.

## Ownership Boundaries
- Registry and spawn templates own authored render resource lookup.
- Spawn apply systems seed ECS components from spawn commands.
- Prepare system combines kinematics plus authoring into compact render data.
- Batched render system only uploads prepared render data and submits indirect draw.

## Data Flow
- Registry/template builders produce `CombatRenderComponent` metadata and `CombatRenderAuthoring` base transform.
- Spawn commands carry both render and authoring.
- Spawn apply writes both components to reused or cold-created entities.
- Prepare writes `Rotation` and `Position.xy` each frame while preserving `Position.z` and `RenderMeta`.
- Batched render uploads `CombatRenderComponent` with `AddRange`.

## Lifecycle / Allocation Rules
- Add `CombatRenderAuthoring` to projectile, impact AOE, and lingering AOE archetypes once.
- Reuse paths overwrite authoring via component set; no chunk partitioning by render id.
- Native/GPU handles remain owned and disposed by existing systems.

## ECS / Job / Threading Constraints
- Prepare job is Burst `IJobChunk` and reads `CombatRenderAuthoring` read-only.
- No managed access from Burst jobs.
- No structural changes in prepare or hot simulation jobs.

## Determinism Requirements
- Preserve existing spawn command pass-through and deterministic projectile/AOE ids.

## Producer / Consumer Separation
- Do not widen damage events with render routing.
- Keep spawn event, spawn command, render prep, and GPU submission responsibilities separate.

## Reused Mechanisms
- Existing `CombatRenderActiveTag` enableable pooling.
- Existing render registry id space and UV basis buffer.
- Existing `NativeList<CombatRenderComponent>.AddRange` upload path.

## Introduced Mechanisms
- `CombatRenderAuthoring` component: `BaseScale`, `BaseSin`, `BaseCos`.
- Compact 2D render record: `float4 Rotation`, `float3 Position`, `int RenderMeta`.

## Validation Requirements
- `CombatRenderComponent` size is 32 B; `CombatRenderAuthoring` size is 16 B.
- Shader and C# stride agree at 32 B.
- Existing combat/render/spawn tests compile and pass where runnable.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`
- `Assets/Scripts/Skills/PlayerSkillDriver.cs`
- `Assets/Scripts/System/Common/CombatRoot.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- directly affected tests and docs named by Task 005
