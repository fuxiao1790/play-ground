# 004 — Catalyst broadphase lane

## Goal

Publish catalyst bodies as broadphase candidates for the projectile and AOE
collision lanes, inside the existing broadphase system, without touching the
target proxy arrays.

## Changes

`Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
- New packed entry type (in this file or beside it):
  ```
  CatalystSnapshotEntry {
      Entity Catalyst;
      float2 Position;
      CombatShapeType ShapeType; float Radius; float2 HalfExtents; float RotationRadians;
      float2 BoundsMin; float2 BoundsMax;
      CombatFaction Faction;
      int CatalystId; int TypeId;
      OnHitSpawnRef OnHitSpawn;
      CatalystTriggerAim Aim;
  }
  ```
  One array, not parallel streams: at ≤200 entries every consumer reads the
  whole entry, so one stream is both simpler and better laid out. The target
  lane keeps parallel arrays because its consumers read different subsets of
  thousands of proxies.
- `TargetSpatialHashSingleton` gains `CatalystEntries` (`NativeList<CatalystSnapshotEntry>`),
  `CatalystProjectileCells` and `CatalystAoeOccupiedCells`
  (`NativeParallelMultiHashMap<long, int>`), `MaxCatalystRadius`
  (`NativeReference<float>`), and `CatalystCount`.
- Second gather query: `CatalystTag + Active`, `WithDisabled(ArmingTag)`,
  reading `CatalystIdentityComponent`, `CombatKinematicsComponent`,
  `CombatCollisionComponent`, `CatalystTriggerComponent`. Same `.Run()` chunk
  walk style as `GatherTargetsJob`.
- Two build jobs mirroring the existing ones: center-cell insert at
  `ProjectileCollisionCellSize` (plus `MaxCatalystRadius`), and occupied-cell
  insert at `AoeCellSize` over the body's bounds.
- All catalyst work is skipped when `CatalystCount == 0`; the lists and maps are
  cleared and the existing early-out for `targetCount == 0` must not skip the
  catalyst build (the two counts are independent — a world with catalysts and
  no proxies is legal).
- Lifetime: allocate in `OnCreate`, clear-and-rebuild each update, dispose in
  `OnDestroy`, and fold the catalyst build handles into the same `BuildHandle`.
  No new handle protocol: consumers keep combining `BuildHandle` and publishing
  into `ConsumerHandle`.
- The system name stays. A rename to a broadphase-neutral name is a reasonable
  follow-up but touches many files and is out of scope.

## Acceptance criteria

- With catalysts alive, `CatalystEntries.Length == CatalystCount` and every
  entry's position matches that body's kinematics for the same update.
- An arming catalyst and a disabled (pooled) catalyst appear in neither map.
- `MaxCatalystRadius` equals the largest body bounding radius and does not
  affect the target cell range used by unit scans.
- A world with catalysts and zero target proxies still builds the catalyst maps.
- With zero catalysts the added per-frame cost is one count check.
- No catalyst entity carries `TargetProxyTag`, `TargetPosition`,
  `TargetCollisionShape`, or `TargetFaction`, so the target arrays are
  unchanged and tracking, targeted acquisition, resource regen, and companion
  replay see nothing new.

## Tests to run (PlayMode, `CatalystBroadphaseTests`)

- `Snapshot_MatchesLiveBodyPositions`
- `ArmingOrPooledBody_IsNotPublished`
- `MaxCatalystRadius_DoesNotWidenTargetCellRange`
- `CatalystsWithoutProxies_StillBuildMaps`
- `NoCatalysts_ClearsLaneAndSkipsBuild`
- `TargetArrays_UnchangedByCatalysts`

## Dependencies

003 (positions must be final before the lane is built).

## Scope

Medium. Mechanical mirroring of an existing gather/build pattern, but it edits
the most dependency-sensitive file in the combat runtime — get the handle
folding and the two independent count gates right.
