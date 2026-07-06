# Task Execution Packet

## Task
003-lifetime-plain-data.md

## Goal
Make `CombatLifetimeComponent` plain `IComponentData` and remove all lifetime enable API use.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `.agent/aoe-telegraph-refactor/implementation-log.md`

## Dependencies Confirmed
- 002 removed AOE lifetime `EnabledRef` reads and `WithPresent` usage.
- Search found no `SetComponentEnabled<CombatLifetimeComponent>(..., false)` writes.

## Step-By-Step Instructions
- Remove `IEnableableComponent` from `CombatLifetimeComponent`.
- Update its lifecycle comment to plain timer semantics.
- Remove all `SetComponentEnabled<CombatLifetimeComponent>` calls.
- Remove `EnabledMask lifetimeMask` and `lifetimeMask[i] = true` writes from reuse jobs.
- Remove `IsComponentEnabled<CombatLifetimeComponent>` assertions.

## Acceptance Criteria
- No `SetComponentEnabled`, `IsComponentEnabled`, `EnabledRef*`, or `WithPresent` usage remains for `CombatLifetimeComponent`.
- Presence-only consumers still compile.

## Validation Required
- Search checks after patch.
- Build/test or explain if unavailable.
