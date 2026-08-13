# Task Execution Packet

## Task
006-tests-and-docs.md

## Goal
Update PlayMode coverage for deferred proxy lifecycle and bring contracts, flows, layer docs, folder map, spawn reference, and ADR record in line with implementation.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Docs/coding-standards.md`
- `Docs/contracts/target-proxy.md`
- `Docs/contracts/spawn-events-and-commands.md`
- `Docs/flows/target-proxy-lifecycle.md`
- `Docs/flows/runtime-frame.md`
- `Docs/flows/mob-spawn-and-behaviour.md`
- `Docs/architecture/phase-order.md`
- `Docs/layers/ecs-simulation.md`
- `Docs/layers/presentation-and-feedback.md`
- `Docs/folder-structure.md`
- `Docs/reference/game-logic/spawn-system.md`
- `.agent/ecs-unit-spawn/implementation-log.md`

## Files Allowed To Create
- `Docs/decisions/adr-007-deferred-spawn-despawn-handshake.md`

## Files Likely Needed For Reading
- All new systems/bridges, CombatEcsWorld, current PlayMode fixtures, target proxy and flow docs, ADR-001 and ADR-005.

## Behavior To Preserve
- Tests use public/runtime APIs or test-owned listener targets, no production test hooks.
- Existing player death behavior and registry guard.
- Documentation authority boundaries.

## Behavior To Change
- Existing mob tests kill through ECS proxy health and yield appropriate protocol frames.
- Add coverage for player exclusion, hit replay before despawn, one-time despawn/pool safety, dormant-actor spawn boundary, and cancellation orphan destruction.
- Docs describe Presentation push and next-Update actor action, current spawn runtime, death decision ownership, and ADR-007 choices.

## Relevant Global Context
- Unity test runner and frame-marker execution are user-owned. Do not run them.
- Do not state passed or observed results absent exported XML / user-provided trace.
- The two phase-order TODOs require a confirming frame-marker trace before removal. Preserve them and add/update surrounding planned-order documentation only if no trace is available.

## Dependencies Confirmed
- Tasks 001-005 complete. `SoftDie` removed; deferred contracts, tags, systems, and controller behavior exist.

## Step-By-Step Instructions
1. Add required test buffers/systems/presentation execution support to existing fixtures where required by new contracts.
2. Rewrite mob tests to mutate ECS Health and wait Simulation/Presentation then actor Update, and insert spawn confirmation yields.
3. Add five described lifecycle tests with test-owned targets/fixtures, no production test-only state.
4. Update listed docs mechanically for finalized protocol; create ADR-007 bound to ADR-001/005 and deliberate non-decisions/windows.
5. Do not remove phase-order TODOs until user supplies confirming frame-marker trace. Document this remaining validation in log.

## Acceptance Criteria
- No SoftDie tests; old regen test remains.
- Five new lifecycle tests cover required regressions.
- Both new bridges documented as TargetCompanion-permitted.
- ADR-007 created and docs updated.
- TODO removal remains blocked pending user frame marker trace; do not claim confirmation.

## Validation Required
- Static source/doc checks and `git diff --check`. Unity run deferred: user provides XML and frame-marker trace.

## Hard Boundaries
- No production hooks, no Unity YAML edits, no unrelated refactors.
