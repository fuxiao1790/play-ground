# 001 - Remove Authoring And Snapshot Path

## Goal

Delete projectile trail configuration and every copy before ECS materialization.

## Changes

1. `Assets/Scripts/System/Authoring/BasicAttackPrefab.cs`:
   - Remove serialized `trailEffect`, `trailEffectShape`, `trailWidth`, and
     `trailStepDistance`.
   - Remove matching public properties.
   - Remove now-unused VFX namespaces.
   - Add no migration alias or replacement placeholder.
2. `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`:
   - Remove `TrailVfxId`, `TrailWidth`, and `TrailStepDistance`.
3. `Assets/Scripts/Skills/SkillDriver.cs`:
   - Remove `RegisterProjectileVfx` and its call from skill registration.
   - Remove trail fields from `SkillIntervalTemplateBuilder.BuildProjectileTemplate`.
   - Preserve projectile render/template registration and all non-trail VFX
     registration for AOE and targeted definitions.
4. `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`:
   - Remove three trail fields from `ProjectileSpawnCommand`.
   - Preserve command blittability and remaining field semantics.
5. `Assets/Scripts/Skills/SkillLoadoutValidator.cs` and
   `Assets/Scripts/Skills/SkillValidationWarning.cs`:
   - Remove projectile trail validation routine/call.
   - Remove now-unused `ProjectileVisualWarning` enum member; current search
     shows no other producer or consumer.
   - Keep targeted LineSegment validation unchanged.

## Acceptance Criteria

- No production C# member named `TrailEffect`, `TrailEffectShape`,
  `TrailVfxId`, `TrailWidth`, or `TrailStepDistance` remains.
- Projectile compile/template/spawn path carries no trail data.
- AOE and targeted VFX registration remains unchanged.
- No compatibility fields, `FormerlySerializedAs`, zero-id placeholders, or
  feature flag remain.

## Dependencies

None.

## Scope / Complexity

Medium. Crosses authoring, managed runtime graph, validator, and unmanaged spawn
snapshot, but behavior is deletion-only.

