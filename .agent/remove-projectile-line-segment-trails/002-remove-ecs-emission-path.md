# 002 - Remove ECS Emission Path

## Goal

Remove projectile trail state from pooled ECS entities and make movement pure
simulation again.

## Changes

1. `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`:
   - Delete `ProjectileTrailVfxComponent` and its lifecycle comment.
   - Remove `Unity.Mathematics` import if no remaining type needs it.
2. `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`:
   - Remove trail component from discrete archetype.
   - Remove component handle, chunk array access, `WriteCommon` parameter, and
     reset assignment.
3. `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`:
   - Remove trail component from continuous archetype.
   - Remove component handle, chunk array access, and `WriteCommon` argument.
4. `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`:
   - Stop resolving `CombatAoeVfxDispatchSingleton`.
   - Remove `LineSegmentVfxPending` job field and producer-handle combination.
   - Remove `ProjectileTrailVfxComponent` from `Execute` query signature.
   - Delete id gate, distance calculation, `VfxEmit.EnqueueLineSegment`, and
     `LastEmitPosition` update.
   - Keep position integration, collision-bounds refresh, system ordering, and
     parallel scheduling unchanged.
   - Remove unused VFX, Collections, Jobs, and Mathematics imports.

## Acceptance Criteria

- Neither projectile archetype includes trail component.
- Movement schedules and runs in world without VFX dispatch singleton.
- Movement job still updates position and bounds for active, armed discrete and
  continuous projectiles.
- `ProjectileMovementSystem` no longer writes queue or touches
  `CombatAoeVfxDispatchSingleton.ProducerHandle`.
- `TargetedResolveSystem` remains valid LineSegment queue producer.
- No new structural change or allocation is introduced.

## Dependencies

- Depends on task 001 removing trail fields from `ProjectileSpawnCommand`.

## Scope / Complexity

Medium. Mechanical removal across both pooled archetypes and shared apply
utility; low gameplay risk if movement body stays unchanged.

