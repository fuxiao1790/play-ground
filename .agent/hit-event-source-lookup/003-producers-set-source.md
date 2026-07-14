# 003 — Producers enqueue {Source, Target}

## Goal
Collision systems stop copying the payload into the event; they enqueue only the
source + target entities. The gate still reads the payload locally.

## Changes

### Projectile — [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs)
- Add `in CombatHitPayload payload` to the `Execute` signature (the entity now
  carries it as its own component — 001). Add it to the system's `EntityQuery`
  ReadOnly set ([:30-39](../../Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs#L30-L39)).
- On-hit-spawn logic reads `projectileHit.OnHitSpawn` (moved onto the component in
  001) instead of `projectileHit.HitPayload.OnHitSpawn`.
- Gate: `HasHitEvent(in payload)` = `payload.DirectDamageEnabled || payload.StackEffect.Enabled`.
- Enqueue ([:254-267](../../Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs#L254-L267)):
  ```csharp
  HitWriter.Enqueue(new CombatHitEvent { Source = entity, Target = targetEntity });
  ```

### AOE core — [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs)
- Thread the source `Entity` and `in CombatHitPayload payload` through
  `RunCollision` → `EmitHit`.
- Gate: `HasHitEvent(in payload)` (same predicate).
- On-hit-spawn logic reads `hitSpawn.OnHitSpawn` (unchanged; still on the slimmed
  `AoeHitSpawnComponent`).
- Enqueue ([:162-175](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L162-L175)):
  ```csharp
  hitWriter.Enqueue(new CombatHitEvent { Source = sourceEntity, Target = targetEntity });
  ```

### AOE systems — pass the source entity + payload into RunCollision
- `ImpactAoeCollisionSystem` `Execute` already has `Entity entity` and now also
  `in CombatHitPayload payload`; forward both
  ([ImpactAoeCollisionSystem.cs:153-164](../../Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs#L153-L164)).
- `LingeringAoeCollisionSystem` likewise
  ([LingeringAoeCollisionSystem.cs:153-164](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L153-L164)).
- Add `CombatHitPayload` (RO) to both AOE collision `EntityQuery`s.

## Notes
- `Kind`/`SourceId`/`TypeId`/`HitPosition` args on the enqueue are gone — no
  callers reference them post-002.
- Because the payload is now an `in` (RO) component the collision job reads, it does
  not conflict with the RW it already holds on other components.

## Acceptance
- No producer copies damage/stack fields into `CombatHitEvent`.
- On-hit-spawn expansion behavior unchanged (still reads `OnHitSpawn`).
- Empty-payload hits still gated out at the producer.

## Dependencies
Requires 001 (payload component) + 002 (event shape). Scope: medium.
