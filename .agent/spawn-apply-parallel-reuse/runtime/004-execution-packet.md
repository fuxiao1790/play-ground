# Task Execution Packet

## Task
004-determinism-and-order.md

## Goal
Audit order-sensitive consumers and add a replay/overflow test proving command-data determinism survives parallel apply.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `.agent/spawn-apply-parallel-reuse/determinism-order-finding.md`
- `.agent/spawn-apply-parallel-reuse/implementation-log.md`

## Files Allowed To Create
- `.agent/spawn-apply-parallel-reuse/determinism-order-finding.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`

## Behavior To Preserve
- No change to runtime behavior except the already-migrated apply path.

## Behavior To Change
- Add written audit finding.
- Add forced-overflow deterministic replay coverage.

## Relevant Global Context
- Reuse/cold split may vary by chunk placement, but command payload owns IDs and jitter.
- No consumer may rely on entity/chunk order for simulation results.

## Dependencies Confirmed
- Projectile and AOE apply migrations completed and runtime build passed.

## Step-By-Step Instructions
- Check hit finalize and VFX dispatch consumers for order dependence.
- Record short written finding.
- Add or identify deterministic repeated-run forced-overflow test.

## Acceptance Criteria
- Written finding names checked consumers and order-safety reason.
- Deterministic replay/sim test runs with forced overflow.

## Validation Required
- Compile playmode tests.
- Run relevant projectile spawn test if available.

## Hard Boundaries
- Do not change consumer architecture.
- Do not reintroduce serial apply ordering.
