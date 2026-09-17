# 003 — Emit keyed projectile trail commands

Scope: single ECS lifecycle for `Begin`, `Append`, `End`. Complexity: high
because all deactivation routes must be covered. Dependency: 002.

Work:

1. Add one 32-byte GPU command value:
   `uint KeyLow, KeyHigh, Sequence, Kind; float2 Position; float Width;
   uint Padding`. Wrap in `ProjectileTrailVfxEvent { int VfxId;
   ProjectileTrailGpuCommand Command; }`. Define command kinds `Begin`,
   `Append`, `End`. Keep key 0 reserved and reject sequence wrap.
2. Allocate a persistent next-key counter on
   `ProjectileSpawnEventSingleton`. The single `ProjectileExpansionJob` claims
   one fresh key for every expanded projectile command with trail id of new
   shape, then places it in `ProjectileSpawnCommand`. Dispose counter with
   singleton. This covers root casts, interval children, and on-hit children
   without relying on nonunique `ProjectileId`.
3. Extend existing `ProjectileTrailVfxComponent` with `TrailKey`,
   `NextSequence`, and `Started`; reset all fields in shared
   `ProjectileSpawnApplyUtility.WriteCommon` on every pool reuse. No new
   per-projectile component or archetype split.
4. In `ProjectileMovementSystem`, first unarmed movement emits `Begin` at
   `LastEmitPosition`. Continue at most one `Append` when step distance is
   reached; update `LastEmitPosition`. `TrailId == 0` remains a fast return.
5. Add one Burst-safe `ProjectileTrailEnd` helper that emits final `End`
   with current position and increments sequence only if `Started`.
   Call it before disabling `Active` in `CombatLifetimeSystem` and in the
   `ProjectileHitEmission.Deactivate` funnel used by discrete and continuous
   collision. Include all early deactivation branches. A death before the
   first movement emits no trail commands.
6. All producer jobs take the same trail queue's `ParallelWriter` and combine
   their handles into existing `CombatAoeVfxDispatchSingleton.ProducerHandle`.
   Remove projectile use of `EnqueueLineSegment`; targeted links retain it.

Acceptance:

- Every activated trail has one unique key; pooled slot reuse gets a new key.
- Per-key sequence is monotonic across Begin/Append/End; same-frame death
  emits End after Begin even though queue order is arbitrary.
- Lifetime expiry, both collision lanes, and short-lived/arming cases have
  correct Begin/End behavior. No duplicate End for one activation.
- A rejected/missing visual id changes no projectile gameplay behavior.
- No managed allocation or structural change added to projectile hot path.

Tests: user runs **PlayMode** `ProjectileSpawnPipelineTests`
(`ReusedProjectile_GetsNewTrailKey`) and
`ProjectileCollisionSimulationTests` / `ProjectileContinuousSimulationTests`
(`Death_EmitsSingleTrailEnd` for each lane), plus an **EditMode** trail
command sequencing test for same-frame Begin/End. Export XML under `Logs/`;
agent reviews it.
