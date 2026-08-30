# Implementation Context

Compact shared context for `bvh-broadphase` task executors. Full detail lives in
`index.md` and `bvh.md`; read those only if this file is insufficient.

## Architectural Decisions

- Discrete projectile collision candidate enumeration moves to a deterministic,
  full-rebuild, wide bounding-circle BVH (BVH4 or BVH8, compile-time selected
  via `BvhConfig.ChildCount`, default `8`).
- Continuous projectile collision, AOE occupied-cell queries, and all target
  acquisition (targeted chain/external anchor, projectile tracking) stay on
  existing spatial-hash paths permanently. This is a scoped coexistence, not a
  migration shim.
- `TargetSpatialHashSystem`/`TargetSpatialHashSingleton` become
  `TargetBroadphaseSystem`/`TargetBroadphaseSingleton` (task 002), owning both
  the retained hashes and the new discrete BVH.
- One target snapshot (entities/positions/shapes/factions) feeds the discrete
  BVH and all retained hashes. No second entity/shape snapshot.
- Existing scalar `CombatCollisionMath` remains sole narrowphase authority.
  `CombatSweepMath` and continuous TOI/sort logic are untouched.
- No `unsafe` anywhere (`PlayGround.Sim.asmdef.allowUnsafeCode=false`,
  `ProjectSettings/ProjectSettings.asset: allowUnsafeCode: 0`). Both must stay
  false through and after this plan.

## Global Invariants

- Broadphase may return false positives, never false negatives.
- One node's geometric child test is SIMD (full packed subtract/multiply/
  add/compare/mask-extract as one batch); tree navigation and exact narrowphase
  are scalar. No scalar per-lane arithmetic/branch before the mask exists.
- BVH4: three `float4` (X,Y,radius) lanes, one `math.bitmask`-style mask.
- BVH8: two complete `float4` tests whose two 4-bit masks combine; no x86
  intrinsics or ISA branch (never an 8-lane scalar loop).
- Full Morton rebuild every simulation update (no refit, no double buffer —
  existing `BuildHandle`/`ConsumerHandle` protocol already prevents concurrent
  mutation/read).
- Root is internal node for non-empty tree; empty tree uses `RootNodeIndex=-1`.
- BVH leaves store target snapshot indices directly — no duplicate
  entity/shape array for the BVH.
- Persistent, geometrically-growing native buffers only; no per-frame managed
  allocation, no per-query `NativeList`. Query workspace is one reused
  fixed-capacity struct (`FixedList512Bytes<int>`-based) created once per
  `IJobChunk` chunk invocation, reset per enabled projectile.
- Traversal stack capacity must be validated against built depth/width; overflow
  must fail validation loudly, never silently prune.

## Ownership Boundaries

- Single ECS owner (`TargetBroadphaseSystem`) creates, resizes, rebuilds,
  publishes handles for, and disposes all target snapshot + BVH + retained-hash
  native memory. No cross-system reach into another system's private fields
  (see `Docs/coding-standards.md` System Encapsulation section) — everything
  goes through the singleton.
- `ProjectileDiscreteCollisionSystem` is the sole BVH consumer.
- `ProjectileContinuousCollisionSystem`, AOE impact/lingering systems,
  `TargetedAcquisition`/`TargetedResolveSystem`, and `ProjectileTrackingSystem`
  remain hash-only consumers — never touch BVH types.

## Data Flow

```
target proxy snapshot (entities/positions/shapes/factions)
        |
        +--> discrete projectile BVH (new)
        +--> continuous projectile collision hash (retained: cells + MaxTargetRadius)
        +--> AOE occupied cells (retained; reused by targeted chain/external anchor)
        +--> tracking cells + tracked-target ID map (retained; projectile tracking)
```

## Lifecycle / Allocation Rules

- Create/update proxy systems (`TargetProxyCreateApplySystem`,
  `TargetProxyUpdateApplySystem`) run before broadphase build; proxy deletion
  stays after current-frame consumers (unchanged ordering).
- Owner completes prior `ConsumerHandle` + `BuildHandle` before clearing/
  resizing/rebuilding this frame (existing pattern in
  `TargetSpatialHashSystem.OnUpdate`, lines ~86-95).
- Same-frame consumers depend on `BuildHandle`; their read jobs combine into
  `ConsumerHandle` before returning.
- Capacities grow geometrically (`newCapacity = max(required, oldCapacity*2)`),
  never shrink mid-session; empty target set clears logical lengths and BVH
  root to `-1` without disposing containers.
- Owner-only disposal in `OnDestroy`, after completing both handles.

## ECS / Job / Threading Constraints

- Discrete collision uses `IJobChunk` (migrating off generated per-entity
  iteration). One traversal workspace per chunk invocation, reused/reset per
  enabled projectile in that chunk — never per-entity construction, never
  shared across parallel chunk invocations.
- Build jobs are `IJob`/parallel jobs scheduled from the owner's `OnUpdate`,
  combined into one `BuildHandle`.
- All BVH/tree code must be Burst-compatible: safe C# only (no pointers, no
  `fixed` buffers, no `allowUnsafeCode`).

## Determinism Requirements

- Morton build is deterministic; ties break by snapshot index.
- Baseline DFS traversal order is deterministic but not a gameplay API contract
  (discrete pierce/consequence behavior must not depend on order).

## Producer / Consumer Separation

- BVH build (producer) publishes `BuildHandle`; discrete collision (consumer)
  depends on it and publishes into `ConsumerHandle`. Same protocol already
  governs retained hash producers/consumers — do not invent a second handle
  scheme for the BVH.

## Reused Mechanisms

- Target proxy snapshot arrays/snapshot-index references.
- Continuous projectile collision spatial hash, `MaxTargetRadius` expansion.
- AOE occupied-cell map, dedup/cap behavior, targeted reuse of those cells.
- Singleton native-container ownership + explicit `JobHandle` publication
  pattern (`Docs/coding-standards.md` System Encapsulation section).
- Persistent capacity growth, owner-only disposal.
- Burst chunk iteration, existing component/query contracts.
- Exact collision math (`CombatCollisionMath`), sweep math (`CombatSweepMath`),
  hit gates, caps, queues, consequences.
- Tracking spatial grid + tracked-target ID map.

## Introduced Mechanisms

- `BvhConfig`, `BvhChildKind`, circle type, Morton entry, BVH4/BVH8 hot/cold
  node structs (task 001).
- Deterministic Morton/bottom-up builder + parent-circle builder (task 001).
- Explicit width-wide SIMD node-overlap kernels + safe scalar fixed-stack
  discrete-projectile BVH traversal workspace (task 001).
- `TargetBroadphaseSystem`/`TargetBroadphaseSingleton` (renamed owner, task 002).
- Validation/profiling instrumentation proving containment + SIMD codegen
  (tasks 001, 006).

## Validation Requirements

- Agent NEVER runs tests. User runs named EditMode/PlayMode suites and exports
  XML under `Logs/` per `Docs/testing.md` §Result Files. Implementation review
  reads XML before any pass claim.
- Task 001: new `BvhBroadphaseEditModeTests` — build/containment/mask/width/
  empty/degenerate coverage, brute-force randomized validation. Written but
  NOT run by the agent.
- Task 006: full XML review gate —
  `Logs/TestResults-EditMode-BvhBroadphase.xml` (EditMode:
  `BvhBroadphaseEditModeTests`, `TargetedResolveEditModeTests`) and
  `Logs/TestResults-PlayMode-BvhBroadphase.xml` (PlayMode:
  `ProjectileCollisionSimulationTests`, `ProjectileContinuousSimulationTests`,
  `ProjectileTrackingSimulationTests`, `AoeSimulationTests`, relevant
  `AoePlayModeTests`, `TargetedSkillPlayModeTests`).
- Burst Inspector proof required for BVH4 packed compare/bitmask and both BVH8
  `float4` halves through packed mask extraction (task 006); no scalar lane mask.

## Files / Systems Mentioned By The Plan

- New: `Assets/Scripts/System/Api/Collision/Broadphase/` — add `BvhConfig.cs`
  (or similar split) for config/types/build/traversal/validation (task 001);
  add `BvhBroadphaseEditModeTests` under `Assets/Tests/EditMode/`.
- Rename: `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
  -> `TargetBroadphaseSystem.cs` (task 002). Namespace
  `PlayGround.System.Combat.Collision.Broadphase`.
- Existing, referenced not modified in 001:
  `Assets/Scripts/System/Api/Collision/Broadphase/CombatSpatialHash.cs`
  (cell math/constants — keep for continuous/AOE/tracking).
- Existing consumers to touch in later tasks:
  `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
  (task 003, migrate), `Assets/Scripts/System/Projectiles/
  ProjectileContinuousCollisionSystem.cs` (task 004, preserve-only,
  do not touch structurally), AOE collision systems / `AoeCollisionCore.cs` /
  `CollisionConstants.cs`, `Assets/Scripts/System/Targeted/
  TargetedAcquisition.cs`, `TargetedResolveSystem.cs`,
  `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs` (task 005,
  preserve-only).
- Existing test files to extend, never rewrite wholesale:
  `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`,
  `ProjectileContinuousSimulationTests.cs`, `ProjectileTrackingSimulationTests.cs`,
  `AoeSimulationTests.cs`, `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs`.

## Current Owner Pattern Reference (pre-rename, task 001 does not touch this file)

`TargetSpatialHashSingleton` today holds: `ProjectileCollisionCells`,
`TrackingCells`, `TrackingIndicesById`, `AoeOccupiedCells`, `MaxTargetRadius`,
`TargetEntities/Positions/Shapes/Factions` (`NativeList`), `TargetCount`,
`BuildHandle`, `ConsumerHandle`. `OnUpdate` completes both handles, clears maps,
gathers snapshot via `Temp` arrays copied into persistent lists, schedules three
`IJob` builds combined into `BuildHandle`. `OnDestroy` completes handles then
disposes everything it created. Task 002 extends this exact shape with BVH
buffers; it does not redesign the handle protocol.
