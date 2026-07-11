# Task Execution Packet

## Task
003-projectile-cold-create-burst.md

## Goal
Move projectile cold-create ECB recording from the managed main-thread loop into `ProjectileSpawnJob`, and remove the dead instance helper and profiler marker.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `.agent/spawn-cold-create-burst/implementation-log.md`

## Files Allowed To Create
- None beyond this packet.

## Files Allowed To Delete
- No files; delete only dead members in the projectile apply system.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`

## Behavior To Preserve
- Projectile component values, contact-gate seed buffer append, tracking enable bit, timed-spawn seed, arming, and collision enable bits.
- Main-thread ECB playback after job completion.
- Reuse counter records only reused disabled slots.

## Behavior To Change
- Use `Allocator.TempJob` for projectile create ECB.
- Record unreused projectile commands inside the Burst job.
- Delete `ColdCreateMarker`.
- Delete `CreateProjectileEntity`.

## Relevant Global Context
- ECB in a job must use `Allocator.TempJob`.
- Cold suffix is `Commands[commandIndex..]` after reuse chunk scan.
- Job stays single-threaded and preserves command order.

## Dependencies Confirmed
- `ProjectileSpawnJob` exists in `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`.
- `RecordCommonProjectileReset` and `RecordTimedSpawnReset` are static and reachable from the nested job.
- Contact-gate and timed-spawn cold-create behavior is contained in those static helpers.

## Step-By-Step Instructions
- Change projectile `createEcb` allocator to `Allocator.TempJob`.
- Add `EntityCommandBuffer Ecb` and `EntityArchetype Archetype` to `ProjectileSpawnJob`.
- Pass `Ecb = createEcb` and `Archetype = _archetype` into the job.
- Add a cold-suffix loop at the tail of `Execute()` before `ReuseCount.Value = commandIndex`.
- Remove the managed `ColdCreateMarker` loop from `OnUpdate`.
- Compute `coldCreateCount = commands.Length - reuseCount`.
- Delete `CreateProjectileEntity`.
- Delete `ColdCreateMarker`.

## Acceptance Criteria
- `ProjectileSpawnJob` compiles under Burst.
- Projectile reuse plus cold count equals command count.
- No remaining `CreateProjectileEntity` or `ColdCreateMarker`.
- Projectile collision simulation tests pass or any inability to run is reported.

## Validation Required
- Compile/build.
- Run relevant projectile PlayMode tests when practical.
- Search for removed members.

## Hard Boundaries
- Do not modify files outside the allowed list except imports directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
