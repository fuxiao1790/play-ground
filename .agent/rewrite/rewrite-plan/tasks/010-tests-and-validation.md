> **LANDED (Increment 1) — superseded by [tasks/017](017-tests-and-validation.md).** Implemented against the Increment-1 architecture (per-domain tags, `...CommandData`, `CombatDamageElement` damage, `CombatTargetElement` targets). Increment 2 migrates these to `Active`, Event/Command names, proxy entities, and `Entity`-keyed damage. Authoritative: `../context/006`.

# Task 010: Test migration + new coverage for the rewritten pipeline

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and `../context/006-validation-strategy.md`.

## Goal
Update existing tests to the new systems/types and add focused tests for the new behaviors (event/command split, expansion fan-out, next-tick spawn, unified lifetime/pulse, reuse, convert-elimination parity).

## Required Reading
- `../context/006-validation-strategy.md` (entire)
- `../context/003-data-flow.md` (the behaviors to assert)
- `../context/005-decision-log.md` (what is preserved vs intentionally changed)

## Design Decisions Already Made
- Tests observe through public APIs / real gameplay effects / buffer contents — no production-only test hooks (per `Docs/coding-standards.md` "Test Hooks").
- Canonical pattern: `Assets/Tests/PlayMode/AoeSimulationTests.cs` (bare world + system list + scope buffers + `Tick`).

## Why This Task Exists
The rewrite changed system names, types, and the scope buffer set; existing tests must follow, and the new phase boundaries (expansion, next-tick) need explicit coverage so future changes can't silently regress them.

## Current Code References
```
Assets/Tests/PlayMode/AoeSimulationTests.cs
- Adds AoeSimulationSystem, AoeSpawnSystem, AoeContactGateSystem, AoeLifetimeSystem, AoeCollisionSystem;
  scope buffers CombatTargetElement, AoeSpawnRequestElement, CombatDamageElement, VfxSpawnRequestElement.
- Required outcome: add AoeSpawnExpansionSystem + AoeSpawnApplySystem (replacing AoeSpawnSystem),
  CombatLifetimeSystem (+ AoePulseVfxSystem if pulse-VFX asserted), drop AoeLifetimeSystem; scope buffer
  AoeSpawnRequestElement → AoeSpawnEvent; handle damage-clear ownership per Task 008 Validation note.

Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs
- If it spawns via the buffer, switch to ProjectileSpawnEvent and the new expansion/apply systems.

Assets/Tests/PlayMode/AoePlayModeTests.cs, BareMinimumPrototypePlayModeTests.cs
- Update system/type references as needed.

Assets/Tests/EditMode/CritEditModeTests.cs, ProjectileAuthoringEditModeTests.cs, SkillValidationEditModeTests.cs
- CritEditModeTests: crit lives in DamageDispatchBridge — update the type name only.
- Authoring/skill tests: update if they reference renamed types.
```

## Files To Modify
- All test files above — update system lists, scope buffer types, and renamed system/struct names.

## Files To Create
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` (new) — covers the projectile event→expansion→apply path:
  1. **Fan-out:** enqueue one `ProjectileSpawnEvent { Count=3, SpreadDegrees=30 }` → after `Tick`, exactly 3 active projectiles with distinct ids and spread velocities.
  2. **Single-shot:** `Count=1` → exactly one entity, `Velocity = BaseDirection*Speed`.
  3. **Child spawn:** spawn a child-spawner projectile; over N ticks children appear with `HasChildSpawner==0`.
  4. **Next-tick (R5):** a projectile with an impact-projectile snapshot hits a target → the impact projectile exists after the tick but its position/hit state is unchanged until the next `Tick`.
  5. **Reuse:** spawn → expire (lifetime) → spawn reuses the same entity.
- (Optional) `Assets/Tests/PlayMode/ImpactConsequenceParityTests.cs` — golden-value parity: an impact-AoE-on-hit produces an AoE with the expected type/position/lifetime/damage (asserts the relocated `BuildImpactAoeEvent` matches the old convert output).

## Files To Delete
None.

## Required Changes
1. Migrate `AoeSimulationTests` to the new systems/buffers; keep its existing assertions (pulse-once, lingering-expire, reentry-cooldown, reuse, out-of-radius, alien-entity-ignored). For pulse tests, set pulse via `Lifetime<=0` and confirm the AoE apply disables `CombatLifetimeComponent`.
2. Add `ProjectileSpawnPipelineTests` covering items 1–5 above. To enqueue events in a bare world, add the producer systems OR enqueue directly into `ProjectileSpawnExpansionSystem.EventQueue` (and/or append to the scope `ProjectileSpawnEvent` buffer) before `Tick`, mirroring how `AoeSimulationTests` writes the spawn buffer directly.
3. Update EditMode tests for renamed types only.
4. Run the full suite (EditMode + PlayMode); fix any drift.

## Behavior Preservation Requirements
- Migrated tests assert the same outcomes as before (the rewrite is behavior-preserving except the documented AoE despawn-VFX area note, which these tests should not depend on).

## Intentional Behavior Changes
None introduced by tests. If a test must encode the AoE despawn-VFX area change (D-LIFETIME-VFX), assert on render-scale-derived area, not `AoeAreaComponent.Size`.

## Out of Scope
- Performance/stress scenes (covered by manual profiling in context 006, not unit tests).

## Dependencies
- Tasks 001–009 (the full rewrite must be in place).

## Follow-Up Tasks
- None — this is the final task.

## Implementation Constraints
- Bare-world tests must add the same scope buffers the real `CombatScopeOwner.Acquire` adds (now including `ProjectileSpawnEvent` and `AoeSpawnEvent`, and NO longer `ProjectileSpawnRequestElement`/`AoeSpawnRequestElement`).
- Dispose any test-created `World` in `TearDown` (existing pattern).
- No production-only test hooks; observe via buffers, entity queries, and `ICombatTarget` listeners.
- Tests live under `Assets/Tests/` (PlayMode for system-driven, EditMode for pure).

## Step-by-Step Implementation Plan
```
1. Migrate AoeSimulationTests (systems, buffers, damage-clear note); run it.
2. Update ProjectileTrackingSimulationTests + the two other PlayMode tests.
3. Update EditMode tests for renamed types.
4. Add ProjectileSpawnPipelineTests (fan-out, single, child, next-tick, reuse).
5. (Optional) Add ImpactConsequenceParityTests.
6. Run the full EditMode + PlayMode suite; fix drift.
```

## Acceptance Criteria
```
- [ ] All pre-existing tests pass against the new systems/types.
- [ ] Fan-out test: Count=3 → 3 distinct projectiles with spread velocities; no command carries Count.
- [ ] Next-tick test: an impact projectile does not move/collide the same tick it is created.
- [ ] Unified-lifetime tests: pulse hits once then deactivates; lingering expires; reuse works.
- [ ] Bare-world tests create the new scope buffer set (ProjectileSpawnEvent/AoeSpawnEvent, no
      *SpawnRequestElement).
- [ ] Full EditMode + PlayMode suite green.
```

## Validation
- Run Unity Test Runner (EditMode + PlayMode) — all green.
- Manual: Play `Main.unity`, exercise single/multi/child/impact projectile attacks + impact/pulse/lingering AoEs; watch `DebugOverlay` counters and the Profiler spawn markers for regressions vs a pre-rewrite baseline capture.

## Risk Level
Low — test-only changes. Risk is mis-encoding the new pipeline in the bare-world setup (wrong buffer set), which surfaces immediately as failing tests.

## Failure Modes
- **Bare-world test spawns nothing:** missing the `ProjectileSpawnEvent`/`AoeSpawnEvent` buffer on the scope, or the expansion system not added to the test's `SimulationSystemGroup`.
- **Next-tick test flaky:** assertion reads state after apply but before the next collision — make sure it asserts within the same `Tick` (entity exists, unchanged) and then after the next `Tick` (now active).

## Rollback Strategy
Tests are additive/independent; revert individual test files without affecting runtime.

## Notes for Future Tasks
- These tests are the regression guard for any future AoE-scatter / timed-AoE / proxy-entity work.
