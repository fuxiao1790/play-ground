# Task Execution Packet

## Task
005-unify-projectile-aoe-query.md

## Goal
Use one upload query for projectile and AOE render entities while preserving separate debug counters.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Behavior To Preserve
- `LastActiveProjectileCount` and `LastActiveAoeCount` remain available for stats.
- Render visual output should not change.

## Behavior To Change
- Projectile and AOE entities are uploaded through one render query.

## Relevant Global Context
- Task 003 already introduces the direct upload query.

## Dependencies Confirmed
- Task 003 implemented a unified `renderQuery` with `WithAny<ProjectileTag, AoeTag>()`.

## Step-By-Step Instructions
- Confirm no separate projectile/AOE upload queries remain.
- Keep separate telemetry as post-upload counts.

## Acceptance Criteria
- Single upload query matches both projectile and AOE entities.
- Both kinds are written in one pass.
- Active counters remain tracked.

## Validation Required
- Search `CombatBatchedRenderSystem.cs` for query names and lock/write path.

## Hard Boundaries
- Do not split upload path again.
