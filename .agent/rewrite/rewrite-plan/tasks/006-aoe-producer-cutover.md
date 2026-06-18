> **LANDED (Increment 1) — partly superseded.** Implemented in code. The cutover stands, but it uses per-domain active tags + `...CommandData`. Increment 2 applies generic `Active` ([tasks/011](011-generic-active.md)) and the Event/Command rename ([tasks/012](012-event-command-rename.md)). Authoritative: `../context/002`/`../context/003`.

# Task 006: Cut AoE producers over to events; delete the old AoE spawn path

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files. AoE **cutover**.

## Goal
Switch AoE producers to emit `AoeSpawnEvent`, then delete `AoeSpawnSystem` and `AoeSpawnRequestElement`. The Task-005 expansion+apply become the only AoE spawn path.

## Required Reading
- `../context/002-target-architecture.md` (§2 AoE pipeline)
- `../context/003-data-flow.md` (§4)
- `../context/004-system-ordering.md` (invariants 2–4)
- `../context/005-decision-log.md` (D-TRANSPORT, D-CONVERT-RELOCATE)

## Design Decisions Already Made
- Producers: (a) managed `CombatRoot.Spawn(AoeSpawnCommand)` / `Spawn(ProjectileAoeSpawnRequest)` → append `AoeSpawnEvent` to scope buffer; (b) `ProjectileCollisionSystem` impact-AoE consequence → enqueue `AoeSpawnPipeline.BuildImpactAoeEvent` into `AoeSpawnExpansionSystem.EventQueue`. (AoE-from-AoE is not a current path.)
- Remove `AoeSpawnRequestElement` + its scope buffer.

## Why This Task Exists
Design §5.7/§7.2: AoE intent flows through the same typed event pipeline; impact-AoE becomes a final typed event from collision instead of going through the generic convert job.

## Current Code References
```
Assets/Scripts/System/Common/CombatRoot.cs
- Spawn(AoeSpawnCommand): AoeRequestFor → append scope AoeSpawnRequestElement buffer; spawnedAoes++.
- Spawn(ProjectileAoeSpawnRequest): delegates to Spawn(AoeSpawnCommand).
- Required outcome: AoeRequestFor → AoeSpawnEvent; append scope AoeSpawnEvent buffer (keep spawnedAoes
  counter + nextAoeId).
- ActiveAoeCount(): reads pending AoeSpawnRequestElement buffer for the count. Update to AoeSpawnEvent.

Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs
- Impact-AoE currently written to CombatPendingSpawn (ImpactAoe) → CombatSpawnConvertJob.AppendImpactAoe.
- Required outcome: on HitPayload.ImpactAoe.Enabled → enqueue AoeSpawnPipeline.BuildImpactAoeEvent(...)
  into AoeSpawnExpansionSystem.EventQueue; stop writing the impact-AoE pending-spawn.

Assets/Scripts/System/Aoe/AoeSpawnSystem.cs           — DELETE.
Assets/Scripts/System/Aoe/AoeEcsComponents.cs         — REMOVE AoeSpawnRequestElement.
Assets/Scripts/System/Common/CombatEcsComponents.cs   — REMOVE AddBuffer<AoeSpawnRequestElement>.
Assets/Scripts/System/Common/CombatSpawnConvertJob.cs — now has no work (projectiles removed in T004,
                                                        AoE removed here) → leave as empty no-op for
                                                        Task 007 to delete, OR stop scheduling it.
```

## Files To Modify
- `System/Common/CombatRoot.cs` — `AoeRequestFor` builds `AoeSpawnEvent`; append to scope `AoeSpawnEvent` buffer; update `ActiveAoeCount()` to read the `AoeSpawnEvent` pending buffer; keep `spawnedAoes`/`nextAoeId`.
- `System/Projectile/ProjectileCollisionSystem.cs` — pass the AoE expansion writer into the job; on impact-AoE enqueue `BuildImpactAoeEvent`; remove impact-AoE writes to `pendingSpawns`. After this, `ProjectileCollisionJob` no longer writes `pendingSpawns` at all (impact-projectile went to the projectile queue in T004, impact-AoE now to the AoE queue) → remove the `pendingSpawns` stream + `CombatSpawnConvertJob` schedule from this system.
- `System/Aoe/AoeCollisionSystem.cs` — after T004 it enqueues bursts to the projectile queue; it has no remaining `pendingSpawns` consumer → remove the `pendingSpawns` stream + `CombatSpawnConvertJob` schedule from this system too.
- `System/Aoe/AoeEcsComponents.cs` — remove `AoeSpawnRequestElement`.
- `System/Common/CombatEcsComponents.cs` — remove the `AoeSpawnRequestElement` buffer add.
- Tests referencing `AoeSpawnSystem` / `AoeSpawnRequestElement` (notably `AoeSimulationTests.cs`) — switch to `AoeSpawnExpansionSystem` + `AoeSpawnApplySystem` and the `AoeSpawnEvent` buffer.

## Files To Create
None.

## Files To Delete
- `System/Aoe/AoeSpawnSystem.cs`

## Required Changes
1. `CombatRoot`: build/append `AoeSpawnEvent`; fix `ActiveAoeCount` buffer type.
2. `ProjectileCollisionSystem`: enqueue impact-AoE events; remove the now-unused `pendingSpawns` stream and the `CombatSpawnConvertJob` schedule (and the `AoeRequests`/`ProjectileRequests` lookups it passed in).
3. `AoeCollisionSystem`: remove its `pendingSpawns` stream + convert-job schedule (bursts already go to the projectile queue).
4. Delete `AoeSpawnSystem`; remove `AoeSpawnRequestElement` + its scope buffer.
5. `CombatSpawnConvertJob` now does nothing and is unscheduled — leave the file for Task 007 to delete (or delete here if you also remove `CombatPendingSpawn`; prefer leaving the deletion to Task 007 to keep this task focused).
6. Update tests; recompile; run.

## Behavior Preservation Requirements
- External AoEs, impact AoEs (from projectile hits), status-effect AoEs, pulse vs lingering, spawn VFX, hit/damage, and pulse-once semantics all match prior behavior. Next-tick activation preserved by ordering.

## Intentional Behavior Changes
None beyond the lifetime-VFX area note already recorded in Task 001 (D-LIFETIME-VFX).

## Out of Scope
- Deleting `CombatSpawnConvertJob` / `CombatPendingSpawn` (Task 007).

## Dependencies
- Task 005 (AoE pipeline exists), Task 004 (projectile cutover done — so collision no longer needs the projectile convert path).

## Follow-Up Tasks
- Task 007 (remove dead convert job + pending-spawn), Task 008 (damage rename), Task 009 (ordering audit).

## Implementation Constraints
- Collision jobs hold the `NativeQueue<AoeSpawnEvent>.ParallelWriter` (Burst-safe); fetch on main thread via `World.GetExistingSystemManaged<AoeSpawnExpansionSystem>()`.
- Producers before expansion (invariant 2): `ProjectileCollisionSystem` before `AoeSpawnExpansionSystem`.
- Update `ECS Lifecycle:` comments for removed buffer/element.

## Step-by-Step Implementation Plan
```
1. CombatRoot: AoeSpawnEvent build + append; fix ActiveAoeCount.
2. ProjectileCollisionSystem: enqueue impact-AoE events; remove pendingSpawns + convert schedule.
3. AoeCollisionSystem: remove pendingSpawns + convert schedule.
4. Delete AoeSpawnSystem; remove AoeSpawnRequestElement + scope buffer.
5. Update AoeSimulationTests (systems + buffer type).
6. Recompile; run.
```

## Acceptance Criteria
```
- [ ] CombatRoot.Spawn(AoeSpawnCommand) appends AoeSpawnEvent; no AoeSpawnRequestElement remains.
- [ ] ProjectileCollisionSystem enqueues impact-AoE events; no system schedules CombatSpawnConvertJob.
- [ ] AoeSpawnSystem deleted; new expansion+apply are the only AoE spawn path.
- [ ] AoeSimulationTests pass against the new systems.
- [ ] External + impact + status AoEs and pulse/lingering behave as before (manual + tests).
- [ ] Repo compiles; no leaks.
```

## Validation
- Compile + `AoeSimulationTests` (ported to new systems) + full suite.
- Manual: Play `Main.unity`; trigger impact AoEs (projectile with impact-AoE hitting a mob), pulse AoEs, lingering AoEs; confirm damage + VFX + expiry.

## Risk Level
High — AoE cutover + removal of the convert-job scheduling from both collision systems.

## Failure Modes
- **No impact AoEs:** collision not enqueueing, or expansion not draining the queue.
- **AoEs spawn but never expire / pulse wrong:** lifetime enable-state from Task 005 apply incorrect.
- **Leftover convert references:** a collision system still schedules `CombatSpawnConvertJob` with a removed lookup → compile error; remove all references.

## Rollback Strategy
Revert to restore `AoeSpawnSystem` + `AoeSpawnRequestElement` + the convert-job schedules; the Task-005 systems go inert.

## Notes for Future Tasks
- With both cutovers done, `CombatSpawnConvertJob` and `CombatPendingSpawn` are dead → Task 007.
