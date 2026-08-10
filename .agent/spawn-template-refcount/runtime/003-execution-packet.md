# Task Execution Packet

## Task
003-apply-instance-counting.md

## Goal
Queue old-key decrements and new-key increments at all materialization funnels.

## Files Allowed To Modify
- `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`

## Behavior To Preserve
- Spawn command lookup and discrete/continuous behavior; template maps are untouched by apply jobs.

## Behavior To Change
- Apply systems direct-read scope refcount state then enqueue component-key deltas through parallel writer.

## Relevant Global Context
- Count all key-bearing fields; detonation key always belongs to projectile map.
- Default keys are ignored. Reused slot emits old `-1` before writes; timed `+1` follows exact existing `HasTimedSpawner` branch.

## Dependencies Confirmed
- 001 state and delta queue exist on scope singleton.

## Step-By-Step Instructions
- Add common enqueue helper.
- Wire writer through projectile and AOE shared utility jobs and targeted inline apply.
- Emit for on-hit, timed, and detonation fields.

## Acceptance Criteria
- Cold creation only adds keys; reuse replaces keys correctly; no count-map job access.

## Validation Required
- Static inspection; Unity tests deferred.

## Hard Boundaries
- Do not change cleanup or sweep behavior.
