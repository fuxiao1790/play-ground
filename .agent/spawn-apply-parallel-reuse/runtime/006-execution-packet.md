# Task Execution Packet

## Task
006-docs.md

## Goal
Document the parallel apply pipeline and accepted lossy-reuse tradeoff.

## Files Allowed To Modify
- `Docs/flows/spawn-event-to-entity.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/aoe-system.md`
- `Docs/reference/simulation/project-ecs-implementation.md`
- `Docs/profiling.md`
- `.agent/spawn-apply-parallel-reuse/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`

## Behavior To Preserve
- Documentation remains implementation-facing and concise.

## Behavior To Change
- Docs describe parallel worker-lane apply instead of serial claim cursor/direct `IJobChunk`.
- Docs state lossy reuse and pool convergence tradeoff.
- Profiling guide names reuse/cold counters and how to read them.

## Relevant Global Context
- Event hop remains queue/buffer.
- Command hop is flat per-domain command containers.
- Apply builds worker-lane command-index stream and cold-creates overflow.

## Dependencies Confirmed
- Tasks 001 through 005 completed.

## Step-By-Step Instructions
- Update spawn flow doc.
- Update projectile and AOE system docs.
- Update profiling guide.
- Update any stale ECS implementation note describing serial reuse.

## Acceptance Criteria
- No doc still describes one shared claim cursor or one serial reuse job as current behavior.
- Docs state accepted lossy-reuse tradeoff.
- Docs mention optional free-slot popcount as future lever.

## Validation Required
- Search docs for stale serial/direct reuse wording.

## Hard Boundaries
- Do not rewrite unrelated docs.
