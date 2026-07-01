# 004 — Tests

## Goal
Lock the behavior contract: drains when idle, suppressed under load, always bounded,
never below retention.

## Where
PlayMode, alongside existing combat sim tests
(`Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`,
`ProjectileCollisionSimulationTests.cs` are the models — same world/scope setup,
`Active`/`CombatRenderBatchId` assertions). Inject an aggressive
`CombatPoolCleanupConfig` (small `RetentionTarget`, `BudgetMs` high so gates pass, or
low so they fail) to make outcomes deterministic within a few ticks.

## Cases
1. **Drains when idle.** Spawn N projectiles of one batch, let them all expire
   (disabled), then tick with gates forced open. Assert disabled count decreases by at
   most `PerBatchDeleteCap` per frame and converges to `floor`
   (`max(RetentionTarget, active*ratio)`), not zero.
2. **Suppressed under load.** Force gate failure (set `SmoothedFrameMs` above
   `BudgetMs`, or drive `FrameStartTime` so `elapsedMs` exceeds budget). Assert zero
   deletions even with a huge disabled pool.
3. **Per-frame global cap.** Two+ oversized batches whose combined excess exceeds
   `MaxDeletesPerFrame`. Assert total deletions in one frame `<= MaxDeletesPerFrame`.
4. **Retention floor respected.** Disabled just above `RetentionTarget` but ratio
   condition false (active high) → no trim; and disabled far above → trims but stops
   at floor.
5. **Reuse still wins the frame.** In a frame that both spawns (reusing disabled
   slots) and trims, assert the trimmer runs after reuse and never deletes a slot the
   spawn claimed (entity count consistent; no reuse fell back to cold-create because
   the trimmer stole a slot).
6. **Delete-safety smoke.** After trimming, active projectiles/AOEs continue to
   simulate, collide, and render (no dangling-entity errors), confirming nothing held
   a stale handle.

## Acceptance criteria
- All cases pass deterministically.
- No safety-system errors (entity validity, job dependency) during trimming.

## Dependencies
- 001, 002, 003.

## Scope
Medium — one test fixture, ~6 cases.
