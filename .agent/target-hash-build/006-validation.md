# 006 — Validation

## Behavior parity (must be byte-for-byte)
The refactor is behavior-preserving. Confirm:
- Insertion order into each map is array order `0..count-1`, identical to the old per-system
  loops → multihashmap iteration order unchanged → overflow "first-N in cell-scan order" and
  tracking reservoir selection are deterministic-identical.
- Cell sizes unchanged: 1 (projectile collision), 64 (tracking), 64 (aoe).
- `MaxTargetRadius` reduction identical (`BoundingRadius` over all shapes).
- `TrackingIndicesById` populated identically (`TryAdd`, first-wins on duplicate id).

## Tests
- `ProjectileTrackingSimulationTests` — tracking acquisition/steering unaffected.
- Any projectile/AOE collision PlayMode tests — hits, pierce, gates, bursts, VFX.
- `CombatPoolCleanupSystemTests` (currently modified in working tree) — ensure the new
  producer system does not disturb pool/reuse counts.
- Add a targeted test: with N targets and one AOE covering them, hit set is identical before/
  after (same targets, same order up to `MaxAoeTargetsPerTick`).

## Profiling
- Reuse `ProjectileTrackingSystem.TargetSpatialHashBuild` marker; add
  `TargetSpatialHashSystem.Gather` and `TargetSpatialHashSystem.Build` markers.
- Expect: 4 `CompleteDependencyBeforeRO` main-thread stalls → 1 (or 0 if gather is fully
  jobified); 4 gathers → 1; 2 AOE hash builds → 1.
- Confirm the build overlaps subsequent main-thread work (jobs on worker threads) rather than
  stalling `OnUpdate`.

## Safety checks
- Editor job-safety on: no "container read/write hazard" errors from consumers reading the
  singleton maps — confirms `BuildHandle`/`ConsumerHandle` threading is correct.
- Verify a consumer's `GetSingleton` does not force-complete the build (check the producer
  build job still shows as running past the consumer's `OnUpdate` in the profiler timeline).
- Zero-target frames: singleton valid, maps empty, no exceptions.

## Depends on
003, 004, 005.
