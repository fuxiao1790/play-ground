> **LANDED (Increment 1) — superseded structure.** Implemented in code as a single bucketed `ProjectileSpawnApplySystem` over per-domain active tags + `...CommandData`. Increment 2 changes all three: generic `Active` ([tasks/011](011-generic-active.md)), Event/Command rename ([tasks/012](012-event-command-rename.md)), and **one apply system per shape** ([tasks/013](013-per-shape-apply.md)). Authoritative: `../context/002` §2 + `../context/005` D-SHAPE-EXPLICIT.

# Task 003: Add ProjectileSpawnExpansionSystem + ProjectileSpawnApplySystem (built beside the old pipeline)

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files.

## Goal
Create the two new projectile spawn systems and the native transport they own. After this task the new systems exist and run, but the event queue/buffer they drain is still empty (no producer feeds them yet — that is Task 004), so behavior is unchanged. The old `ProjectileMultiExpandSystem`/`ProjectileSpawnSystem` remain the live path.

## Required Reading
- `../context/002-target-architecture.md` (§2 Projectile pipeline, §3 ownership table)
- `../context/004-system-ordering.md` (phases 9.8/9.9, invariants 2–4)
- `../context/005-decision-log.md` (D-EXPANSION-OWNS-MATH, D-TRANSPORT, D-SHAPE-BUCKETING)

## Design Decisions Already Made
- `ProjectileSpawnExpansionSystem` (`SystemBase`) **owns** `NativeQueue<ProjectileSpawnEvent> EventQueue` (Persistent) and exposes `EventQueue.AsParallelWriter()` to producers, plus a finalized command output (a `NativeStream` named `PendingCommands` + `PendingHandle`, mirroring today's `ProjectileMultiExpandSystem.PendingStream`).
- Expansion drains BOTH `EventQueue` and the scope `ProjectileSpawnEvent` buffer into one frozen `NativeArray<ProjectileSpawnEvent>`, clears both, then runs the fan-out job → `ProjectileSpawnCommandData` into `PendingCommands`.
- `ProjectileSpawnApplySystem` (`SystemBase`) reads `PendingCommands`, buckets by `(faction, typeId, hasChildSpawner)`, reuses `WithDisabled<ProjectileActiveTag>` slots, cold-creates overflow. This is the existing `ProjectileSpawnSystem` body with its input type changed from the dual-use element to `ProjectileSpawnCommandData`.

## Why This Task Exists
Design §5.1–§5.4: a true expansion phase owns all spawn math, and apply only copies resolved fields. Building the new systems beside the old keeps the repo compiling and lets Task 004 do a clean producer/consumer cutover.

## Current Code References
```
Assets/Scripts/System/Projectile/ProjectileMultiExpandSystem.cs
- Owns NativeStream PendingStream; drains scope ProjectileSpawnRequestElement buffer; expands Count>1
  (SpreadAngle, Rotate, per-shot id/velocity/renderZ) into the stream.
- Required outcome: its expansion MATH is the template for ProjectileSpawnExpansionSystem's job, but
  the input is now ProjectileSpawnEvent and the output is ProjectileSpawnCommandData. Leave this file
  live for now (Task 004 deletes it).

Assets/Scripts/System/Projectile/ProjectileSpawnSystem.cs
- OnCreate builds archetypeNoChildSpawner / archetypeWithChildSpawner.
- Buckets by ProjectileSpawnKey(faction,typeId,hasChildSpawner); DeadSlotQueryFor (WithDisabled
  <ProjectileActiveTag> + shared filter); ProjectileSpawnJob (IJobChunk) overwrites slots;
  CreateProjectileEntity cold path; RecordProjectileReset; NeedsCollision.
- Required outcome: copy this almost verbatim into ProjectileSpawnApplySystem, changing the input
  element type to ProjectileSpawnCommandData and reading PendingCommands from the new expansion system.
  Leave this file live for now (Task 004 deletes it).
```

## Files To Modify
- None of the old systems (they stay live this task). You MUST register the two new systems so they run, but ensure their input is empty so behavior is unchanged. (The `EventQueue` and scope `ProjectileSpawnEvent` buffer are empty until Task 004; the scope buffer was added in Task 002.)

## Files To Create
- `System/Projectile/ProjectileSpawnExpansionSystem.cs` — `SystemBase`, `SimulationSystemGroup`, ordered to run after both collision systems and before apply: `[UpdateAfter(typeof(ProjectileCollisionSystem))]`, `[UpdateAfter(typeof(PlayGround.System.Aoe.AoeCollisionSystem))]`, `[UpdateBefore(typeof(ProjectileSpawnApplySystem))]`. Owns `internal NativeQueue<ProjectileSpawnEvent> EventQueue;`, `internal NativeStream PendingCommands; internal JobHandle PendingHandle;`. Create `EventQueue` in `OnCreate` (Persistent); dispose in `OnDestroy`. OnUpdate: `Dependency.Complete()`; drain `EventQueue` + scope `ProjectileSpawnEvent` buffer → `NativeArray<ProjectileSpawnEvent>`; clear both; if non-empty allocate `PendingCommands = new NativeStream(count, TempJob)` and schedule the fan-out `IJob` (port `ProjectileMultiExpandJob` math but read `ProjectileSpawnEvent` and write `ProjectileSpawnCommandData`); else `PendingCommands = default`.
- `System/Projectile/ProjectileSpawnApplySystem.cs` — `SystemBase`, `SimulationSystemGroup`, `[UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]`. Port `ProjectileSpawnSystem` wholesale: same archetypes, same `ProjectileSpawnKey` bucketing, same `DeadSlotQueryFor`, same `ProjectileSpawnJob` (renamed), same cold-create + `RecordProjectileReset`/`NeedsCollision`. Change: input is `ProjectileSpawnCommandData` read from `World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>().PendingCommands` (complete `PendingHandle`, read stream, dispose). Set `CombatLifetimeComponent.Remaining` + enabled (always enabled for projectiles — per Task 001).

## Files To Delete
None this task.

## Required Changes
1. Create `ProjectileSpawnExpansionSystem` with the owned `EventQueue` (Persistent), the drain-both-sources finalize, and the fan-out job (math ported from `ProjectileMultiExpandJob`: `SpreadAngle`, `Rotate`, per-shot velocity = `Rotate(BaseDirection, angle) * Speed`, per-shot id = `BaseProjectileId + i`, per-shot render-Z step; `Count<=1` passes through resolving `Velocity = BaseDirection * Speed` and world bounds). The fan-out job writes `ProjectileSpawnCommandData` (resolved: `Velocity`, `BoundsMin/Max` via `ProjectileCollisionMath.ComputeWorldBounds`, `Render.RenderZ`, `ProjectileId`).
2. Create `ProjectileSpawnApplySystem` ported from `ProjectileSpawnSystem`, input changed to `ProjectileSpawnCommandData`.
3. Register both systems (Unity auto-registers `SystemBase` in the default world; for tests they are added explicitly). Ensure the ordering attributes place expansion after collisions and apply after expansion.
4. Confirm the `EventQueue` and scope buffer are empty (no producers yet) so the new apply creates nothing. The OLD pipeline still spawns everything.
5. Recompile and run.

## Behavior Preservation Requirements
- Because no producer writes `ProjectileSpawnEvent` yet, the new systems must be no-ops (empty inputs) and the OLD pipeline must still produce all projectiles. Net behavior unchanged.

## Intentional Behavior Changes
None. This task should be behavior-preserving.

## Out of Scope
- Switching producers to the new event (Task 004).
- Deleting old systems (Task 004).

## Dependencies
- Task 002 (types + scope buffer + helpers).
- Task 001 (lifetime component used in reset).

## Follow-Up Tasks
- Task 004 wires producers to `EventQueue` + scope buffer and deletes the old systems.

## Implementation Constraints
- ECS: no structural change in jobs; cold-create via ECB played back once on main thread (as `ProjectileSpawnSystem` does).
- Container ownership: `EventQueue` is Persistent, disposed in `OnDestroy`; `PendingCommands` is TempJob, disposed by the consumer (apply) each frame after reading — mirror the `ProjectileMultiExpandSystem`/`ProjectileSpawnSystem` disposal contract exactly.
- Cross-system access via `World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` (today's pattern).
- Keep ProfilerMarkers/counters equivalent to `ProjectileSpawnSystem` (`Projectile.Spawn`, `.ReuseJob`, Cold/Reuse counters) on the new apply system.

## Step-by-Step Implementation Plan
```
1. Create ProjectileSpawnExpansionSystem: own EventQueue (Persistent); OnUpdate drains queue+buffer
   → NativeArray; fan-out job → PendingCommands (NativeStream); set PendingHandle.
2. Port ProjectileMultiExpandJob math into the fan-out job, output ProjectileSpawnCommandData
   (resolve velocity, bounds, renderZ, id).
3. Create ProjectileSpawnApplySystem: port ProjectileSpawnSystem (archetypes, bucketing, reuse job,
   cold-create), input = ProjectileSpawnCommandData from expansion.PendingCommands; set
   CombatLifetimeComponent.Remaining (+enabled).
4. Add ordering attributes (expansion after both collisions; apply after expansion).
5. Recompile; run full test suite (must be green — new path inert).
```

## Acceptance Criteria
```
- [ ] ProjectileSpawnExpansionSystem owns EventQueue and produces PendingCommands of
      ProjectileSpawnCommandData with fully resolved fields.
- [ ] ProjectileSpawnApplySystem reuses/cold-creates from ProjectileSpawnCommandData and sets
      CombatLifetimeComponent.
- [ ] No apply system reads Count/Spread/Jitter (the type has none).
- [ ] Old pipeline still live; runtime behavior unchanged; all existing tests pass.
- [ ] No persistent-allocation leaks (EventQueue disposed in OnDestroy; PendingCommands disposed each frame).
- [ ] Repo compiles.
```

## Validation
- Compile + full existing test suite (green — new systems inert).
- A temporary focused test (optional, keep or discard): manually enqueue one `ProjectileSpawnEvent` (Count=3) into `ProjectileSpawnExpansionSystem.EventQueue` in a bare-world test, `Tick`, assert 3 active projectiles. This proves the new path before Task 004 connects it.

## Risk Level
Medium — the apply port must match the original reuse/cold-create exactly; the expansion fan-out math must match `ProjectileMultiExpandJob`.

## Failure Modes
- **New apply spawns duplicates of the old path:** a producer accidentally already feeds the queue, or the scope `ProjectileSpawnEvent` buffer is non-empty. Confirm Task 004 hasn't been started; assert the queue is empty.
- **Resolved command missing bounds/velocity:** fan-out didn't call `ComputeWorldBounds` or didn't set `Velocity` for `Count==1`. Detect via the optional focused test.

## Rollback Strategy
Remove the two new system files. The old pipeline is untouched, so the game still works.

## Notes for Future Tasks
- Task 004 deletes `ProjectileMultiExpandSystem` and `ProjectileSpawnSystem` and points all producers at this expansion system's `EventQueue` / the scope buffer.
