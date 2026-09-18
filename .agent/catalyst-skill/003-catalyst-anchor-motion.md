# 003 — Catalyst anchor motion (orbit)

## Goal

Catalysts orbit their owner. Motion is authored per skill and the pattern set is
built to grow without touching the archetype or any other system.

## Changes

`Assets/Scripts/System/Catalysts/CatalystEcsComponents.cs`
- `enum CatalystMotionPattern : byte { OrbitOwner = 0 }`
- `CatalystMotionComponent { CatalystMotionPattern Pattern; float Radius; float AngularSpeed; float Phase; float2 LastOwnerPosition; }`
  `AngularSpeed` is radians/second; its sign is the orbit direction.

`Assets/Scripts/System/Catalysts/CatalystAnchorSystem.cs` (new, `ISystem`, Burst)
- `SimulationSystemGroup`, `[UpdateAfter(typeof(CatalystSpawnApplySystem))]`,
  `[UpdateBefore(typeof(TargetSpatialHashSystem))]`. Positions must be final
  before the broadphase lane is built, which puts it before every collision
  system by construction.
- Job over `CatalystTag + Active`, `WithDisabled(ArmingTag)`, reading
  `ComponentLookup<TargetPosition>` for the owner.
- Owner resolution:
  - owner proxy exists → `center = ownerPosition`, write it to
    `LastOwnerPosition`.
  - owner proxy gone and `DespawnOnOwnerLoss == 0` → `center = LastOwnerPosition`;
    the body keeps orbiting the last known point and expires on its own timer.
  - owner proxy gone and `DespawnOnOwnerLoss != 0` → kill through the same
    death funnel as lifetime expiry (disable `Active`, disable
    `CombatCollisionActiveTag`, release the trigger template key).
- Angle is **stateless**: `angle = Phase + (float)ElapsedTime * AngularSpeed`.
  Never accumulate into `Phase`. This is what makes pool reuse, mid-life
  refresh, and phase respacing safe, and it keeps a ring in formation with no
  shared ring entity.
- Write `CombatKinematicsComponent.Position = center + Radius * (cos angle, sin angle)`
  and `Velocity` as the tangent (`Radius * AngularSpeed * (-sin, cos)`), which
  drives the existing `AlignToVelocity` render bit.
- Refresh `CombatCollisionComponent.BoundsMin/BoundsMax` via
  `CombatCollisionMath.ComputeWorldBounds` for the body's authored shape and
  rotation. Rectangle and capsule bodies keep their authored
  `RotationRadians`; task 007 decides whether authoring offers
  tangent-aligned rotation.
- One `switch` on `Pattern` with a single case. Unknown values are already
  rejected at registration (task 001), so the job has no defensive branch.

## Acceptance criteria

- A ring of N bodies stays evenly spaced while the owner moves.
- Position for a given `(Phase, ElapsedTime)` is reproducible: two runs with the
  same sim time produce the same position, and a body that refreshes mid-life
  does not jump.
- Adding a member respaces the ring within one update, with no visible drift on
  the existing members beyond the respace itself.
- Owner proxy deletion with the default policy leaves bodies orbiting the last
  known point until expiry; with `DespawnOnOwnerLoss` they die that update and
  release their key once.
- Collision bounds follow the body every update for all three shapes.

## Tests to run (PlayMode, `CatalystAnchorTests`)

- `Orbit_KeepsEvenSpacingWhileOwnerMoves`
- `Orbit_AngleIsStatelessAcrossRefresh`
- `Orbit_AddMemberRespacesWithoutDrift`
- `OwnerLost_DefaultPolicy_KeepsOrbitingLastPosition`
- `OwnerLost_DespawnPolicy_KillsAndReleasesOnce`
- `Bounds_FollowBodyForCircleRectangleCapsule`

## Dependencies

002.

## Scope

Small. One job, one lookup, no allocation.
