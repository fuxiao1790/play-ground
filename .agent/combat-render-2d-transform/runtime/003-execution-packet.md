# Task Execution Packet

## Task
003-spawn-plumbing.md

## Goal
Carry and seed `CombatRenderAuthoring` through registry templates, spawn commands, archetypes, and spawn write paths.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRoot.cs`
- `Assets/Scripts/Skills/PlayerSkillDriver.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- direct compile fixes caused by removing `CombatRenderComponent.VisualScale`
- `.agent/combat-render-2d-transform/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `ProjectileSpawnExpansionSystem.cs`
- `CombatLifetimeSystem.cs`
- `ProjectileCollisionSystem.cs`

## Behavior To Preserve
- Spawn command pass-through.
- Pool reuse by disabled active tag.
- Existing render id and RenderZ calculation.

## Behavior To Change
- Spawn commands carry `Authoring` next to `Render`.
- All render archetypes include `CombatRenderAuthoring`.
- All reuse and cold-create paths set authoring from command.

## Relevant Global Context
- New authoring component is part of every projectile/AOE archetype.
- No per-frame structural churn.

## Dependencies Confirmed
- Task 001 added `CombatRenderAuthoring`.
- Task 002 makes prepare depend on `CombatRenderAuthoring`.

## Step-By-Step Instructions
- Update registry methods to return render plus authoring.
- Add template authoring helpers on `CombatRoot`.
- Add `Authoring` to projectile and AOE spawn commands.
- Split fallback render/authoring producers in `PlayerSkillDriver`.
- Add `CombatRenderAuthoring` to projectile, impact AOE, and lingering AOE archetypes.
- Set authoring in direct array writes and ECB reset paths.
- Apply direct compile fixes for systems that need base scale after the split.

## Acceptance Criteria
- All three archetypes contain `CombatRenderAuthoring`.
- Every reuse and cold-create path seeds authoring.
- Spawned projectiles/AOEs have correct base scale/rotation for first prepare.

## Validation Required
- Search/static validation now; compile after task 004.

## Hard Boundaries
- Do not alter shader/stride in this task.
- Do not change spawn behavior beyond carrying authoring.
