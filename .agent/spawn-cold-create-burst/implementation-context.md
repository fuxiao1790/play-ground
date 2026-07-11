# Implementation Context

## Architectural Decisions
- Fuse cold-create ECB recording into the existing single-threaded Burst reuse job for each spawn domain.
- Keep ECB playback on the main thread after the job completes.
- Do not add a second cold-create job or a new data path.

## Global Invariants
- Reuse plus cold-created count must equal the tick command count.
- Existing spawn component values and enable bits must match current behavior.
- Scope is limited to impact AOE, lingering AOE, and projectile spawn apply systems.

## Ownership Boundaries
- Spawn apply systems own materializing their command streams into pooled or newly-created ECS entities.
- Shared record helpers remain the source of cold-created component values.
- No unrelated systems, contracts, or architecture changes.

## Data Flow
- Existing flow stays: command array -> reuse disabled slots -> record cold suffix into ECB -> complete -> playback ECB.
- The cold suffix is `commands[reuseCount..]`, where `reuseCount` is the `commandIndex` after chunk reuse.

## Lifecycle / Allocation Rules
- ECB recorded inside a job must use `Allocator.TempJob`, not `Allocator.Temp`.
- `using var` disposal remains after playback.
- Structural changes are only applied by `createEcb.Playback(EntityManager)` on the main thread.

## ECS / Job / Threading Constraints
- Use the existing Burst `IJob` per domain.
- Record ECB commands sequentially in the job; do not use `ParallelWriter`.
- Keep existing component type-handle and buffer-handle plumbing.

## Determinism Requirements
- Single-threaded job preserves command order for cold-created entities.
- Entity identity comes from command fields, not creation order.

## Producer / Consumer Separation
- Spawn commands stay distinct from damage, VFX, and result streams.
- Apply systems only consume their own command singleton lanes.

## Reused Mechanisms
- Existing reuse jobs.
- Existing static record helpers.
- Existing archetypes.
- `TempJob` ECB-in-job precedent from `CombatPoolCleanupSystem`.

## Introduced Mechanisms
- Add `EntityCommandBuffer Ecb` and `EntityArchetype Archetype` fields to each spawn job.
- Add a cold-suffix recording loop at the tail of each job before writing `ReuseCount`.

## Validation Requirements
- Compile/build with Burst-compatible jobs.
- Run AOE simulation tests after AOE tasks.
- Run projectile collision simulation tests after projectile task.
- Confirm no remaining projectile `ColdCreateMarker` or `CreateProjectileEntity`.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
