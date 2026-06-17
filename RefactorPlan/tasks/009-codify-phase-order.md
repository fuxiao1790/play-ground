# Task 009: Codify the §9 phase order and audit native-container phase rules

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and `../context/004-system-ordering.md` exactly.

## Goal
Make the design §9 frame order an explicit, verifiable contract across all touched systems, guarantee next-tick spawn (R5), and audit that every spawn/event/command native container obeys the create→write→finalize→read→clear/dispose phase rules (§6).

## Required Reading
- `../context/004-system-ordering.md` (entire)
- `../context/002-target-architecture.md` (§3 ownership table)
- `../context/005-decision-log.md` (D-DAMAGE-CLEAR, D-TRANSPORT)

## Design Decisions Already Made
- The ordering table and invariants in context 004 are the contract. No new ordering decisions.
- Next-tick spawn (R5) is enforced by order only — no `SpawnedThisTick` marker.

## Why This Task Exists
Design §9: "System order is part of the architecture, not incidental scheduling." After the cutovers the system set changed (new expansion/apply/timed/lifetime systems, deleted old ones); their `[UpdateBefore/After]` attributes must be made consistent and the R5/§6 invariants verified.

## Current Code References
```
New/renamed systems whose ordering attributes must match context 004:
- CombatLifetimeSystem (9.1)                       [System/Common]
- TimedProjectileSpawnSystem (9.2)                 [System/Projectile]
- ProjectileTrackingSystem, ProjectileMovementSystem (9.3)  [unchanged]
- ProjectileContactGateSystem, ProjectileCollisionSystem (9.4)
- AoeContactGateSystem, AoeCollisionSystem (9.5)
- ProjectileSpawnExpansionSystem, AoeSpawnExpansionSystem (9.8)
- ProjectileSpawnApplySystem, AoeSpawnApplySystem (9.9)
- AoePulseVfxSystem (after lifetime, before render prepare)
- CombatRenderPrepareSystem (after apply)           [unchanged]
- DamageDispatchBridge (PresentationSystemGroup)    [renamed in T008]
OrderFirst: CombatTargetSyncSystem, ProjectileSimulationSystem (sole damage-clear owner).
```

## Files To Modify
- Each system above: set/verify `[UpdateInGroup]`, `[UpdateBefore]`, `[UpdateAfter]`, `OrderFirst` so the realized order matches the context-004 table. In particular:
  - `ProjectileSpawnExpansionSystem` / `AoeSpawnExpansionSystem`: `[UpdateAfter(ProjectileCollisionSystem)]`, `[UpdateAfter(AoeCollisionSystem)]`, `[UpdateAfter(TimedProjectileSpawnSystem)]`, `[UpdateBefore(<domain>SpawnApplySystem)]`.
  - `*SpawnApplySystem`: `[UpdateAfter(<domain>SpawnExpansionSystem)]`, and before `CombatRenderPrepareSystem`.
  - `CombatLifetimeSystem`: `[UpdateBefore(ProjectileCollisionSystem)]` and `[UpdateBefore(AoeCollisionSystem)]` (phase 9.1).
  - `TimedProjectileSpawnSystem`: after movement, `[UpdateBefore(ProjectileSpawnExpansionSystem)]`.
- Remove any stale `[UpdateBefore/After]` attributes naming deleted systems (`ProjectileSpawnSystem`, `ProjectileMultiExpandSystem`, `ProjectileChildSpawnSystem`, `AoeSpawnSystem`, `ProjectileLifetimeSystem`, `AoeLifetimeSystem`).

## Files To Create
None. (Optionally add an EditMode test that asserts ordering — see Validation.)

## Files To Delete
None.

## Required Changes
1. Audit every `[UpdateBefore]`/`[UpdateAfter]` attribute in the combat systems; delete references to deleted systems; add the attributes listed above so the realized order equals the context-004 table.
2. Verify R5: all of lifetime (9.1), timed-spawn (9.2), tracking/movement (9.3), both collisions (9.4/9.5) run BEFORE both apply systems (9.9). Entities created/reused in apply must not be visited by any 9.1–9.5 system the same frame.
3. Container phase audit — for each of `ProjectileSpawnExpansionSystem.EventQueue`, `AoeSpawnExpansionSystem.EventQueue`, their `PendingCommands`, and the scope `ProjectileSpawnEvent`/`AoeSpawnEvent` buffers:
   - the queue is written only by producers that run before the owning expansion;
   - the expansion completes producer dependencies, drains to a `NativeArray`, and clears the queue + buffer in the same finalize;
   - the command output is read by apply, then disposed; nothing mutates it after finalize.
4. Confirm exactly one `CombatDamageElement` clear (T008) and that `DamageDispatchBridge` runs in `PresentationSystemGroup` after simulation.
5. Recompile; run.

## Behavior Preservation Requirements
- The realized execution order must keep all behavior from the cutover tasks; this task only makes ordering explicit and corrects any drift.

## Intentional Behavior Changes
None. This task should be behavior-preserving.

## Out of Scope
- Any data-shape or logic change.

## Dependencies
- Tasks 001, 004, 006, 008 (all systems exist in final form).

## Follow-Up Tasks
- Task 010 (tests, including a next-tick test that depends on this ordering).

## Implementation Constraints
- Prefer explicit `[UpdateBefore/After]` referencing concrete system types over relying on default sort.
- Do not introduce a `SpawnedThisTick` marker or delayed-activation queue (design R5 — order only).
- One ECB sync point per apply system per frame; do not add extra structural-change phases.

## Step-by-Step Implementation Plan
```
1. Grep combat systems for [UpdateBefore/After] and OrderFirst; list current realized order.
2. Remove attributes naming deleted systems.
3. Add the attributes from context 004 so order matches the table.
4. Verify R5 by inspection (apply is last simulation write; nothing 9.1–9.5 touches new entities).
5. Audit each container's create/write/finalize/read/clear-dispose against §6.
6. Recompile; run full suite + the next-tick test (Task 010, or a temporary one here).
```

## Acceptance Criteria
```
- [ ] Realized SimulationSystemGroup order matches context 004 (lifetime → timed → tracking/movement →
      gates/collisions → expansion → apply; pulse-vfx + render-prepare after).
- [ ] No [UpdateBefore/After] references a deleted system.
- [ ] R5 holds: entities from apply do not move/collide/emit until the next frame (next-tick test passes).
- [ ] Each event queue/buffer/command container obeys create→write→finalize→read→clear/dispose; no
      container is read while still written.
- [ ] Exactly one CombatDamageElement clear; DamageDispatchBridge in PresentationSystemGroup.
- [ ] Repo compiles; full suite passes.
```

## Validation
- Compile + full suite.
- Next-tick test (author here or in Task 010): a projectile that spawns an impact projectile on hit — assert the impact projectile exists after the tick but has not moved/collided until the following `Tick`.
- Optional: an EditMode test that creates the world, sorts systems, and asserts the index order of the key systems matches the table.
- Profiler: confirm one sync point per apply phase; no unexpected structural changes.

## Risk Level
Low–Medium — attribute-only changes, but a wrong order can reintroduce same-tick recursion or lost spawns.

## Failure Modes
- **Same-tick spawn recursion / spawn explosion:** apply runs before a collision/lifetime system. Detect via the next-tick test and frame-rate/spawn-count spikes.
- **Lost or duplicated spawns:** expansion runs before a producer. Detect via multi-shot/child/impact manual checks.

## Rollback Strategy
Revert attribute edits. Since this task changes only ordering metadata, rollback restores the prior (working but implicit) order.

## Notes for Future Tasks
- Adding a future `TimedAoeSpawnSystem` or AoE scatter slots in at 9.2/9.8 without reordering anything else.
