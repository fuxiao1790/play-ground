# Combat Pool Cleanup

## Context

Projectile and AOE entities use disable-in-place pooling. Expired entities keep
their archetype and reusable component storage, with `Active` disabled, so later
spawns can reuse them without hot-path structural churn.

Without cleanup, the world retains the highest historical projectile/AOE entity
count. Heavy scenes can leave behind large disabled pools, increasing idle scans
and retained chunk memory after combat calms down.

## Decision

`CombatPoolCleanupSystem` trims disabled combat pools in `LateSimulationSystemGroup`
with a chunk-local heuristic, evaluated by a Burst-compiled `IJobChunk` scheduled
in parallel:

- One query spans every reuse pool (projectiles + both AOE archetypes) with
  `IgnoreComponentEnabledState`, so each chunk holds its active and disabled
  entities together.
- Per chunk, the job counts enabled `Active` entities. If that count is below
  `ChunkActiveThreshold`, the chunk is "sparse" and every disabled entity in it is
  recorded for destruction on an `EntityCommandBuffer.ParallelWriter`.
- Chunks at or above the threshold keep their disabled entities as a warm reuse
  buffer, sized implicitly by current combat load.
- The system completes the job, plays the ECB back on the `EntityManager`, and
  disposes it — matching the repo's in-system `EntityCommandBuffer(Allocator.Temp/TempJob)`
  playback idiom (no `EndSimulation` ECB singleton).

This replaced the earlier ratio/retention/cap design. Frame-time gating had been
removed first (wall-clock `SmoothedFrameMs` includes vsync/GPU sleep, so a calm
scene reads as "over budget" and never trimmed); the ratio floors that remained
were still slow to *start* trimming. The chunk heuristic starts as soon as a
chunk falls below the active threshold and needs no round-robin, caps, or clock.
The now-dead `CombatFrameClock`/`CombatFrameClockSystem` were deleted with it.

Cleanup is pool-agnostic and does not key by `CombatRenderKindId`; spawn reuse can
claim any disabled slot in the matching archetype and overwrites render data on reset.

## Follow-up

There is no per-frame delete cap: a huge idle pool going fully sparse in one frame
records all its destroys at once, so the ECB playback can hitch. If that hitch
shows up in profiling, bound the work (e.g. process a rolling subset of chunks per
frame). Tune `ChunkActiveThreshold` from measured idle-frame recovery vs. re-spike
reuse churn: higher = drains more aggressively but keeps a smaller warm buffer.
