# Task Execution Packet

## Task
001-fix-regen-revive.md

## Goal
Prevent health regeneration from reviving `Health.Current <= 0f`, retaining branchless health-loop semantics.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/ResourceRegenSystem.cs`
- `.agent/ecs-unit-spawn/implementation-log.md`

## Files Allowed To Create
- None

## Files Likely Needed For Reading
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`

## Behavior To Preserve
- Positive health regenerates up to Max.
- Mana loop stays unchanged.
- Zero delta-time remains a no-op without an early-out.
- No branch reintroduced in health loop.

## Behavior To Change
- Depleted health remains depleted even with positive `RegenPerSecond`.

## Relevant Global Context
- ECS owns proxy health. No managed/Unity access in simulation systems.
- Unity test execution is user-owned; static validation only here.

## Dependencies Confirmed
- None required. Existing depleted-health and positive-health ordering tests exist.

## Step-By-Step Instructions
1. In health query, set `alive` with `math.select(0f, 1f, health.ValueRO.Current > 0f)`.
2. Multiply regeneration delta by `alive` in existing capped health write.
3. Leave mana loop and delta-time behavior unchanged.

## Acceptance Criteria
- Depleted-health regen test can pass.
- Positive-health ordering and mana behavior unchanged.
- No branch in health loop.

## Validation Required
- Static source check. Do not run Unity tests.

## Hard Boundaries
- Modify only allowed files.
- No architecture changes or later task work.
