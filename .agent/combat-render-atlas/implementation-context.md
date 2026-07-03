# Implementation Context

## Architectural Decisions
- Use one manually-authored `UnityEngine.U2D.SpriteAtlas`, one shared unit-quad mesh, and one shared instanced atlas material.
- Do not dynamically pack sprites at runtime.
- Fail loud during registration if a non-null sprite is not returned by `SpriteAtlas.GetSprite(sprite.name)`.
- Store UV rect on `CombatRenderComponent`, computed once when the render component is built.

## Global Invariants
- No spawn/despawn structural churn beyond existing pooling paths.
- `CombatRenderActiveTag` remains the active render filter.
- `Graphics.RenderMeshInstanced` still chunks at 1023 instances.
- Domain comes from `ProjectileTag` / `AoeTag`; faction comes from `CombatFaction`, not render data.

## Ownership Boundaries
- `CombatRoot` owns the serialized atlas reference and configures `CombatRenderResourceRegistry`.
- `CombatRenderResourceRegistry` owns only resources it creates (`SharedMesh`, `SharedMaterial`), not atlas assets.
- Presentation submits already-prepared render data; it does not resolve per-kind registry data in its scatter loop.

## Data Flow
- Skill or AOE registration -> `CombatRenderResourceRegistry.Register(...)` -> folded visual scale and UV rect.
- Spawn command receives `CombatRenderComponent` with `UvRect`.
- Apply systems copy the whole render component onto entities.
- `CombatRenderPrepareSystem` writes matrices.
- `CombatBatchedRenderSystem` scatters matrices and entity UV rects into shared draw buffers.

## Lifecycle / Allocation Rules
- Shared mesh/material are lazily created and destroyed by registry teardown.
- Atlas and packed texture are referenced only.
- Batched render buffers and managed UV scratch array are allocated once in `OnCreate`.

## ECS / Job / Threading Constraints
- Registration and rendering resource creation happen on the main thread.
- No new ECS components or archetype changes are introduced for atlas rendering.
- Render scatter completes dependencies before main-thread `Graphics.RenderMeshInstanced` submission.

## Reused Mechanisms
- Existing render component copy paths.
- Existing render active tag.
- Existing render matrix utility.
- Existing `CombatRoot` pre-`Awake` configure hooks.

## Introduced Mechanisms
- `CombatRoot.ConfigureAtlas(SpriteAtlas)`.
- `CombatRenderComponent.UvRect`.
- `CombatRenderResourceRegistry.SharedMesh`, `SharedMaterial`, `Atlas`, and `AtlasTexture`.
- `CombatAtlasTestFixture` for loading the real test atlas and its sprite dependency.

## Validation Requirements
- `dotnet build PlayGround.Runtime.csproj`.
- `dotnet build PlayGround.Tests.PlayMode.csproj`.
- Unity PlayMode tests when the Editor is not already holding the project open.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatRoot.cs`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Shaders/CombatAtlasInstancedSprite.shader`
- `Assets/Tests/PlayMode/CombatAtlasTestFixture.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Docs/contracts/render-batch-data.md`
