# Determinism And Order Finding

## Consumers Checked

- `CombatApplyFinalizeSingleSystem`: hit events are flattened from a queue, then
  accumulated by target proxy in a temporary hash map. Simulation-visible outputs
  are per-target totals, hit counts, crit counts, and status stack snapshots.
  Damage and stack contribution accumulation are additive per target. Crit rolls
  are based on target, frame, and per-target hit index; the source comment already
  treats hit order as unspecified. Parallel spawn apply does not change hit
  payload values, only which entity slot receives a command.
- Retired `CombatApplyFinalizeSystem`: disabled under `#if false`. It bucketed
  by target and also documented bucket order as unspecified, so it is not a live
  ordering dependency.
- `CombatVfxDispatchSystem` / `CombatVfxRoot`: VFX pending spawns are drained
  from a native queue into `CombatVfxDispatcher.StageSpawn` and then dispatched.
  This is presentation-only; accepted events are counted, not fed back into
  simulation. Queue order can affect visual staging order when caps apply, but
  not gameplay state.
- Collision producers (`ProjectileCollisionSystem`, AOE collision core): these
  emit hit/spawn/VFX payloads from component data. Spawn apply writes identical
  component values on reuse and cold-create, so producer payloads do not depend
  on whether a command reused a slot or overflowed.

## Result

No live gameplay consumer requires freshly spawned projectiles or AOEs to occupy
a specific entity slot or chunk. The remaining order-sensitive surface is visual
queue staging under caps, which is presentation-only and already queue-based.

## Test Coverage Added

- `ProjectileSpawnPipelineTests.ParallelApply_ForcedOverflowKeepsDeterministicIdsAcrossRepeatedRuns`
  creates exactly one reusable disabled projectile slot, emits four deterministic
  commands, and repeats that setup three times. Each run must report one reuse
  and three cold creates while the expected deterministic child IDs are present.
