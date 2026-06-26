# 005 — Collision / status / timed emit invocations

## Goal

Every on-hit, detonation, and interval spawn emits a `SpawnInvocation` built from a
follow-up key + the hit's per-instance frame. Remove the bespoke event builders.

## Changes

- [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs)
  (and `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`): on hit, if
  `AoeHitSpawnComponent.OnHitSpawnRef` is set, emit a `SpawnInvocation`
  (`Kind`/`TemplateKey` from the ref; `Position = impact`, `AimDirection`,
  `ContactGateSeedTargetId = targetKey`, faction/source/seed/tick stamped).
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs):
  same for the projectile on-hit ref (impact AOE / impact projectile collapse to one
  emit; aim-back direction preserved).
- [StatusProcessSystem.cs](../../Assets/Scripts/System/Status/StatusProcessSystem.cs):
  threshold detonation emits a `SpawnInvocation` from
  `(DetonationKind, DetonationKey)` at the target position, with summed
  contribution applied as before.
- [TimedSpawnSystem.cs](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs):
  emit `SpawnInvocation` from `TimedSpawnComponent.TemplateKey` (already keyed; just
  swap the emitted type).
- Remove `ProjectileSpawnPipeline.BuildImpactProjectileEvent` / `BuildBurstEvent`
  and `AoeSpawnPipeline.BuildImpactAoeEvent` / `BuildOnHitAoeSpawnEvent` (their work
  now lives in expansion).

## Acceptance criteria

- All follow-up spawns flow through `invocation -> expansion -> command -> apply`.
- Tracking config, contact-gate seeding, and impact-position spawning are preserved
  (the three earlier fixes are now structural, not per-builder).
- No collision/status/timed code constructs a fat spawn event or touches the
  registry for writes; registry reads are `[ReadOnly]`.

## Dependencies

003, 004.

## Scope

Large. Touches every spawn producer; deletes the pipeline builders.
