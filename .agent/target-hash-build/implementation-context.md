# Implementation Context

## Architectural Decisions
- Add one producer system, `TargetSpatialHashSystem`, that gathers target proxy data once and builds three target spatial hashes.
- Publish hash container handles and snapshot arrays through `TargetSpatialHashSingleton`, not through another system's fields.
- Consumers read the singleton and combine `BuildHandle` before scheduling read jobs.

## Global Invariants
- Target proxy data is frame-constant during `SimulationSystemGroup`.
- Preserve cell sizes: projectile collision uses 1, tracking uses 64, AOE uses 64.
- Preserve FNV-1a `CellKey` math and `floor(pos / cellSize)` cell math.
- No managed target companion access from jobs.
- Combat hot paths must be allocation-light and use persistent native containers where planned.

## Ownership Boundaries
- Shared target proxy components live in `Assets/Scripts/System/Common`.
- Projectile consumers live in `Assets/Scripts/System/Projectile`.
- AOE consumers live in `Assets/Scripts/System/Aoe`.
- The new spatial hash helper and singleton/system belong in shared common ECS code.

## Data Flow
- Producer: target proxy query -> persistent snapshot arrays -> three single-threaded Burst build jobs -> singleton fields.
- Consumers: singleton snapshot/maps -> collision/tracking jobs -> existing output queues.

## Lifecycle / Allocation Rules
- Producer owns persistent maps, id map, max-radius reference, and snapshot arrays.
- Producer completes prior `ConsumerHandle` before clearing and rebuilding containers.
- Producer completes build/consumer handles and disposes persistent native containers in `OnDestroy`.

## ECS / Job / Threading Constraints
- Build jobs write raw native container fields, not the singleton component.
- Consumers must accumulate read job handles into `ConsumerHandle`.
- Insertion jobs must be single-threaded `IJob`s to preserve multihashmap iteration order.
- Avoid main-thread sync points except what gather requires.

## Determinism Requirements
- Insert targets in snapshot array order `0..TargetCount-1`.
- `TrackingIndicesById.TryAdd` remains first-wins.
- AOE overflow remains first-N in cell-scan order.

## Producer / Consumer Separation
- `TargetSpatialHashSystem` is the only builder of these target hashes.
- Tracking, projectile collision, lingering AOE collision, and impact AOE collision only read the singleton maps/snapshot.

## Reused Mechanisms
- Singleton-component publication with dependency handles, like existing render buffer pattern described by the plan.
- Persistent native containers cleared and reused per frame.
- Burst jobs for simulation hot paths.

## Introduced Mechanisms
- `CombatSpatialHash` shared helper.
- `TargetSpatialHashSingleton` component.
- `TargetSpatialHashSystem` producer.

## Validation Requirements
- Compile/build check after implementation.
- Run targeted PlayMode tests when possible: projectile tracking, projectile collision, AOE simulation/play mode, combat pool cleanup.
- Add or update targeted test for AOE hit set/order parity if feasible.
- Verify no local target extraction/hash build remains in consumers.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatSpatialHash.cs`
- `Assets/Scripts/System/Common/TargetSpatialHashSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
