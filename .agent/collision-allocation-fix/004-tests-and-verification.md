# Task 04 — Tests + profiler verification

**Depends on:** 02 (run after the core fix), re-run after 03.

## Existing test homes (extend, don't add assemblies)

- AOE: [Assets/Tests/PlayMode/AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs),
  [Assets/Tests/PlayMode/AoePlayModeTests.cs](../../Assets/Tests/PlayMode/AoePlayModeTests.cs)
- Projectile: [Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs),
  [Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs)

## New tests (AOE) — add to `AoeSimulationTests.cs`

1. **Dedup preserved.** One AOE overlapping a single target registered into
   multiple cells (bounds straddling a `SpatialHashCellSize = 64` boundary).
   Assert exactly **one** hit/damage event for that target per tick.
2. **Hard cap = 32.** One AOE overlapping > 32 targets. Assert exactly **32** hit
   events that tick (assert count, not identity — accepted scan-order clamp).
3. **Under cap.** One AOE overlapping K ≤ 32 targets → all K hit.
4. **No double-hit across ticks.** Lingering AOE with cooldown: a target hit on
   tick 1 is not re-hit until its gate cooldown expires.

## New tests (projectile, after Task 03)

5. **Pierce 0 = one hit.** `PierceRemaining == 0` against ≥ 1 target → exactly
   **1** gated hit, then despawn.
6. **Pierce N = N+1 hits.** `PierceRemaining == N` against ≥ N+1 targets → exactly
   **N + 1** gated hits, then despawn. Locks the despawn-at-(-1) semantics.

## Profiler verification (the real acceptance gate)

1. Reproduce the scenario from `ProfilerCaptures/play-ground_2026-06-18_18-19-54.csv`
   (same scene, comparable entity counts; capture ~90 frames). Export to CSV.
2. Run, replacing `<new.csv>`:

```powershell
$d = Import-Csv "ProfilerCaptures\<new.csv>"
foreach ($job in 'AoeCollisionJob','ProjectileCollisionJob') {
  $rows = $d | Where-Object { $_.Function -eq 'UnsafeUtility.Malloc' -and $_.FunctionPath -match $job }
  [PSCustomObject]@{
    Job = $job
    Frames = $rows.Count
    TotalCalls = ($rows | ForEach-Object { [int]$_.Calls } | Measure-Object -Sum).Sum
    MaxCallsPerFrame = ($rows | ForEach-Object { [int]$_.Calls } | Measure-Object -Maximum).Maximum
  }
}
```

### Pass criteria (vs. baseline 2026-06-18 capture)
- `AoeCollisionJob`: malloc rows → **0**. Baseline was 75 frames / 707 calls /
  max 65.
- `ProjectileCollisionJob`: malloc calls/frame do **not** increase; any residual
  is per-frame writer churn (out of scope), not gate growth — confirm it does not
  scale with active projectile count.
- `AoeCollisionJob` `SelfMs` does not regress beyond noise (baseline avg 1.97 ms,
  max 6.09 ms).

## Acceptance

- All existing collision tests pass; new tests 1-6 pass.
- Profiler pass criteria met; attach the new CSV path to the PR and set the design
  doc ([collision-broadphase-malloc.md](collision-broadphase-malloc.md)) status to
  "implemented."
