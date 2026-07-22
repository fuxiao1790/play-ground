# 003 — Simulation: energy-accrual tick

## Goal
Convert the runtime tick from cooldown-countdown to energy-accrual, set the correct initial
state, and update the `CombatRoot` gate + legacy fallback. Preserve determinism and the
anti-freeze guards.

## Changes

### `Assets/Scripts/System/Spawning/TimedSpawnSystem.cs` (`TimedSpawnJob`)
- Replace the constant `MinIntervalSeconds` with `MinEnergyThreshold = 1e-3f`. Keep
  `MaxTicksPerUpdate = 256`.
- Rewrite `Execute`:
  - Early-out unchanged for `lifetime.Remaining <= 0` / `Faction == None`. Also early-out if
    `spawn.EnergyPerSecond <= 0f` (disabled / no accrual).
  - `state.EnergyAccumulated += spawn.EnergyPerSecond * DeltaTime;`
  - Loop:
    ```
    int tickIndex = state.TickIndex;
    int ticks = 0;
    float threshold = ThresholdFor(tickIndex + 1);
    while (state.EnergyAccumulated >= threshold && ticks < MaxTicksPerUpdate) {
        ticks++; tickIndex++;
        Enqueue<child>(... DeterministicIdTickIndex = tickIndex ...);   // same enqueue bodies
        state.EnergyAccumulated -= threshold;
        threshold = ThresholdFor(tickIndex + 1);
    }
    state.TickIndex = tickIndex;
    ```
  - `ThresholdFor(tick) = max(MinEnergyThreshold, spawn.EnergyThreshold +
    DeterministicJitter(spawn.SourceId, spawn.JitterSeed, tick, spawn.EnergyThresholdJitter))`.
  - Keep `DeterministicJitter` exactly as-is (now interpreted in energy units); drop the
    `NextIntervalSeconds` helper (folded into `ThresholdFor`).
  - The three `Enqueue` branches (Projectile / ImpactAoe / LingeringAoe) are unchanged except
    they run inside the new loop.

### `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `InitialTimedSpawnStateFor`: return `new TimedSpawnStateComponent { EnergyAccumulated = 0f,
  TickIndex = 0 }`. Remove the interval-based `CooldownRemaining` seed and its
  `DeterministicJitter` call. Starting empty gives first-spawn-at `cost/rate` seconds; the
  per-tick threshold jitter (which hashes `SourceId`) preserves cross-source desync, so no
  initial offset is needed.

### `Assets/Scripts/System/Core/CombatRoot.cs`
- `IsTimedSpawnEnabled`: retest on `JitterSeed > 0 && EnergyPerSecond > 0f &&
  EnergyThreshold > 0f && TemplateKey != default`.
- `TimedSpawnFor` legacy `ProjectileChildSpawnConfig` fallback: first **verify whether it is
  live** (grep for any producer that sets `request.ChildSpawn` with a real config in the
  compiled flow). If live, populate the fallback `TimedSpawnComponent` from
  `ChildSpawn.EnergyPerSecond`/`EnergyThreshold`/`EnergyThresholdJitter`. If dead, remove the
  fallback branch and the `ChildSpawn` wiring. Do not leave interval semantics behind.

## Acceptance criteria
- Traveling projectiles / lingering AOEs emit children when accumulated energy crosses the
  threshold; higher `spawnEnergyCost` => slower cadence; higher `energyPerSecond` => faster.
- Deterministic child ids unchanged (still from `tickIndex`).
- Zero/negative cost cannot spin the loop (min-threshold clamp); one update stays bounded by
  `MaxTicksPerUpdate`.

## Scope
Medium. Core loop rewrite in one job + two small edits.

## Dependencies
001 (fields). Coordinated with 002 for a compiling build.
