# Task 004: Cut projectile producers over to events; delete the old projectile spawn pipeline

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files. This is the projectile **cutover**: after it, the new pipeline is the only projectile spawn path.

## Goal
Switch every projectile spawn producer to emit `ProjectileSpawnEvent`, then delete `ProjectileMultiExpandSystem`, `ProjectileSpawnSystem`, `ProjectileChildSpawnSystem`, and the dual-use `ProjectileSpawnRequestElement`. The new expansion+apply systems (Task 003) become the live path.

## Required Reading
- `../context/002-target-architecture.md` (§2 Projectile pipeline, §3 ownership)
- `../context/003-data-flow.md` (§1–§3)
- `../context/004-system-ordering.md` (invariants 1–5)
- `../context/005-decision-log.md` (D-TRANSPORT, D-CONVERT-RELOCATE, D-EXPANSION-OWNS-MATH)

## Design Decisions Already Made
- Three producers: (a) managed `CombatRoot.Spawn` → append `ProjectileSpawnEvent` to the scope buffer; (b) `TimedProjectileSpawnSystem` (replaces `ProjectileChildSpawnSystem`) → enqueue into `ProjectileSpawnExpansionSystem.EventQueue`; (c) `ProjectileCollisionSystem` impact-projectile consequence + `AoeCollisionSystem` burst → enqueue via `ProjectileSpawnPipeline.BuildImpactProjectileEvent`/`BuildBurstEvent`.
- The dual-use `ProjectileSpawnRequestElement` and its scope buffer are removed.
- `CombatSpawnConvertJob` keeps existing for AoE output until Task 006/007; in this task, remove only its **projectile** output by having collision enqueue projectile events directly. (If simpler, keep `CombatSpawnConvertJob` writing AoE for now and just stop it from writing projectiles.)

## Why This Task Exists
Design §5/§7.2: producers emit intent; expansion owns math; collision emits final typed events. This removes the dual-use element, the separate expand stage, and the projectile half of the generic convert job.

## Current Code References
```
Assets/Scripts/System/Common/CombatRoot.cs
- Spawn(ProjectileSpawnCommand,...): builds ProjectileSpawnRequestElement via ProjectileRequestFor,
  appends to scope ProjectileSpawnRequestElement buffer.
- Required outcome: ProjectileRequestFor builds a ProjectileSpawnEvent (carry Count/Spread/Jitter as
  intent; do NOT pre-resolve per-shot velocity); append to scope ProjectileSpawnEvent buffer.

Assets/Scripts/System/Projectile/ProjectileChildSpawnSystem.cs
- IJobEntity over child-spawner archetype; ECB.AppendToBuffer ProjectileSpawnRequestElement (Count=1).
- Required outcome: becomes TimedProjectileSpawnSystem; same timed math; enqueue ProjectileSpawnEvent
  (Count=1, HasChildSpawner=0) into expansion EventQueue.AsParallelWriter(). Delete the old file.

Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs
- On hit: writes CombatPendingSpawn (ImpactAoe + ImpactProjectile) to pendingSpawns stream → convert job.
- Required outcome: on hit, if HitPayload.ImpactProjectile.Enabled → enqueue
  ProjectileSpawnPipeline.BuildImpactProjectileEvent(...) into expansion EventQueue; if
  HitPayload.ImpactAoe.Enabled → still produce an AoE consequence (leave that going through the
  existing AoE path until Task 006). Stop writing the projectile part of CombatPendingSpawn.

Assets/Scripts/System/Aoe/AoeCollisionSystem.cs
- On hit: writes CombatPendingSpawn (ProjectileBurst) → convert job.
- Required outcome: enqueue ProjectileSpawnPipeline.BuildBurstEvent(...) into expansion EventQueue;
  stop writing the burst part of CombatPendingSpawn.

Assets/Scripts/System/Projectile/ProjectileMultiExpandSystem.cs  — DELETE.
Assets/Scripts/System/Projectile/ProjectileSpawnSystem.cs        — DELETE.
Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs      — REMOVE ProjectileSpawnRequestElement.
Assets/Scripts/System/Common/CombatEcsComponents.cs (CombatScopeOwner) — REMOVE AddBuffer<ProjectileSpawnRequestElement>.
```

## Files To Modify
- `System/Common/CombatRoot.cs` — `ProjectileRequestFor` → `ProjectileEventFor` returning `ProjectileSpawnEvent`; append to scope `ProjectileSpawnEvent` buffer. Keep id assignment (`nextProjectileId += command.Count`, base `+1`) and child-spawner/tracking/render building. Carry `Count/SpreadDegrees/JitterDegrees/JitterSeed/BaseDirection/Speed` as intent (do not resolve per-shot velocity here).
- `System/Projectile/ProjectileCollisionSystem.cs` — replace projectile-spawn stream writes with `EventQueue` enqueues; the job needs the expansion system's parallel writer (fetch in `OnUpdate` via `World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` and pass `EventQueue.AsParallelWriter()` into the job). Keep damage + vfx streams as-is. Keep impact-AoE producing through the existing AoE path for now.
- `System/Aoe/AoeCollisionSystem.cs` — same: enqueue burst events to `EventQueue`; keep damage/vfx; keep impact-AoE-from-projectile path unchanged.
- `System/Projectile/ProjectileEcsComponents.cs` — remove `ProjectileSpawnRequestElement`.
- `System/Common/CombatEcsComponents.cs` — remove the `ProjectileSpawnRequestElement` buffer add.
- `System/Common/CombatSpawnConvertJob.cs` — remove the projectile-building methods/branches (`AppendImpactProjectiles`, `AppendProjectileBurst`); keep `AppendImpactAoe` (AoE still routed here until Task 006). Remove the `ProjectileRequests` `BufferLookup` field if no longer used.
- `System/Projectile/ProjectileSpawnPipeline.cs` — if the Task 002 helpers took `CombatPendingSpawn`, they still work; collision passes the same data. (`CombatPendingSpawn` is removed in Task 007 — if you switch helpers to primitive args, update callers here.)
- Tests referencing `ProjectileSpawnSystem`/`ProjectileMultiExpandSystem`/`ProjectileSpawnRequestElement` — update to the new systems/types.

## Files To Create
- `System/Projectile/TimedProjectileSpawnSystem.cs` — `ISystem`, ports `ProjectileChildSpawnSystem`'s timed catch-up loop and pattern math; enqueues `ProjectileSpawnEvent` into the expansion `EventQueue.AsParallelWriter()` instead of `ECB.AppendToBuffer`. Order at phase 9.2 (after movement, before lifetime is NOT required now since lifetime moved to 9.1; keep `[UpdateBefore(ProjectileSpawnExpansionSystem)]` and after movement as today).

## Files To Delete
- `System/Projectile/ProjectileMultiExpandSystem.cs`
- `System/Projectile/ProjectileSpawnSystem.cs`
- `System/Projectile/ProjectileChildSpawnSystem.cs`

## Required Changes
1. `CombatRoot`: rename/retarget `ProjectileRequestFor` → builds `ProjectileSpawnEvent`; append to scope `ProjectileSpawnEvent` buffer. Preserve all field mapping and id assignment.
2. Create `TimedProjectileSpawnSystem`: copy `ProjectileChildSpawnEntityJob` logic; replace the `Ecb.AppendToBuffer(chunkIndex, Scope, new ProjectileSpawnRequestElement{...})` with `EventQueue.Enqueue(new ProjectileSpawnEvent{...})` using a `NativeQueue<ProjectileSpawnEvent>.ParallelWriter` passed into the job. Remove the `EndSimulationEntityCommandBufferSystem` dependency.
3. `ProjectileCollisionSystem`: pass the expansion writer into `ProjectileCollisionJob`; on `ImpactProjectile.Enabled` call `ProjectileSpawnPipeline.BuildImpactProjectileEvent(...)` and `Enqueue`. Remove projectile writes to the `pendingSpawns` stream. (Impact-AoE still uses the `pendingSpawns`/convert path this task.)
4. `AoeCollisionSystem`: same for `ProjectileBurst.Enabled` → `BuildBurstEvent` → `Enqueue`.
5. Trim `CombatSpawnConvertJob` to AoE-only.
6. Delete the three old systems; remove `ProjectileSpawnRequestElement` + its scope buffer.
7. Update tests; recompile; run.

## Behavior Preservation Requirements
- Single-shot, multi-shot/fan, child-spawning, impact-projectile, and AoE-burst projectile spawns all produce the same entities (count, ids, velocities, render-Z, pierce, tracking, contact-gate seeding) as before. Next-tick activation (R5) preserved by ordering (apply at 9.9).

## Intentional Behavior Changes
None. This task should be behavior-preserving (it changes the transport, not the outcomes).

## Out of Scope
- AoE event/command split (Tasks 005/006).
- Deleting `CombatSpawnConvertJob` / `CombatPendingSpawn` entirely (Task 007).

## Dependencies
- Task 003 (expansion + apply systems exist).
- Task 002 (types + helpers + scope buffer).

## Follow-Up Tasks
- Task 005/006 (AoE), Task 007 (remove dead convert job/pending-spawn), Task 009 (ordering audit).

## Implementation Constraints
- The collision jobs are `[BurstCompile] IJobEntity` — they may hold a `NativeQueue<ProjectileSpawnEvent>.ParallelWriter` field (Burst-safe). Fetch the writer on the main thread in `OnUpdate`; schedule the enqueue within the existing parallel job and chain dependencies so expansion's `Dependency.Complete()` sees the writes.
- Producers MUST run before expansion (invariant 2). `TimedProjectileSpawnSystem` and both collisions before `ProjectileSpawnExpansionSystem`.
- No per-frame managed allocations in `OnUpdate`.
- Keep `ECS Lifecycle:` comments updated for the removed buffer/element.

## Step-by-Step Implementation Plan
```
1. CombatRoot: build ProjectileSpawnEvent, append to scope ProjectileSpawnEvent buffer.
2. Create TimedProjectileSpawnSystem (ported child-spawn math, enqueues events); delete
   ProjectileChildSpawnSystem.
3. ProjectileCollisionSystem: enqueue impact-projectile events via the pipeline helper; stop writing
   projectile pending-spawns.
4. AoeCollisionSystem: enqueue burst events; stop writing burst pending-spawns.
5. Trim CombatSpawnConvertJob to AoE-only.
6. Delete ProjectileMultiExpandSystem + ProjectileSpawnSystem; remove ProjectileSpawnRequestElement
   + its scope buffer add.
7. Update tests/bootstrap references; recompile; run.
```

## Acceptance Criteria
```
- [ ] CombatRoot.Spawn appends a ProjectileSpawnEvent; no ProjectileSpawnRequestElement remains.
- [ ] TimedProjectileSpawnSystem enqueues events; ProjectileChildSpawnSystem is deleted.
- [ ] Collision systems enqueue impact-projectile / burst events via ProjectileSpawnPipeline helpers.
- [ ] ProjectileMultiExpandSystem and ProjectileSpawnSystem are deleted; the new expansion+apply are
      the only projectile spawn path.
- [ ] CombatSpawnConvertJob no longer builds projectiles.
- [ ] Multi-shot, child spawn, impact projectile, and AoE burst all behave as before (manual + tests).
- [ ] Repo compiles; no persistent-allocation leaks.
```

## Validation
- Compile + full test suite (update system lists first).
- New test (can be authored here or in Task 010): enqueue a `Count=3` event → 3 projectiles; child-spawner over several ticks → children appear; projectile-with-impact-projectile on hit → burst appears next tick.
- Manual: Play `Main.unity` — fire single/multi/child/impact attacks; confirm visuals + damage. Profiler: `Projectile.Spawn` markers present on the new apply system; structural-change count steady (cold-create only on overflow).

## Risk Level
High — this is the projectile cutover; a mismatch in id/velocity/render-Z resolution or producer ordering breaks visible spawning. Mitigated by the Task 002/003 ports being verbatim and by tests.

## Failure Modes
- **Nothing spawns:** expansion not draining the scope buffer, or producers writing a different `EventQueue` instance. Confirm `World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` returns the live instance; confirm finalize drains both sources.
- **Double spawns:** old systems not actually deleted, or convert job still building projectiles.
- **Impact spawns mis-aim:** helper direction-invert or salt drifted from the original convert job.
- **Same-tick recursion:** impact projectile collides the same frame → ordering invariant 1 broken.

## Rollback Strategy
Revert the commit to restore the three deleted systems + the dual-use element + buffer; the Task 003 systems become inert again. Because Task 003 left the old path live, rollback is clean.

## Notes for Future Tasks
- After Task 006 makes AoE typed, `CombatSpawnConvertJob` and `CombatPendingSpawn` become fully dead → Task 007 deletes them.
