# Task Execution Packet

## Task

003-tick-system.md

## Goal

Rewrite timed spawning as deterministic energy accrual, initialize energy empty, and update CombatRoot gates/fallback.

## Files Allowed To Modify

- `Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Core/CombatRoot.cs`

## Behavior To Preserve

- Burst parallel `IJobEntity`, unchanged three enqueue bodies, deterministic ids from tick index, source-id jitter hash, template/event/apply payloads, and 256 tick cap.

## Behavior To Change

- Accrue `EnergyPerSecond * DeltaTime`; emit while accumulated energy meets the per-tick jittered threshold; subtract threshold on each emit.
- Use positive `1e-3f` threshold floor; rates at or below zero do not accrue or emit.
- Begin timed-spawn state at zero energy/tick zero, with no initial jitter seed.
- All CombatRoot gates need positive rate and threshold.

## Relevant Global Context

- 001+002 completed: all config/runtime setup producers use `EnergyPerSecond`, `EnergyThreshold`, and `EnergyThresholdJitter`.
- Per-tick jitter means `DeterministicJitter(sourceId, seed, tickIndex + 1, thresholdJitter)` remains the desync mechanism.
- No interval compatibility path may survive.

## Legacy Fallback Evidence

- Search found no production `new ProjectileChildSpawnConfig`/`ChildSpawn` assignment in `Assets/Scripts`; it is used by current play-mode legacy request tests.
- Therefore retain `CombatRoot.TimedSpawnFor`'s fallback public-request branch and map its energy config fields. Do not remove `ChildSpawn` wiring.

## Dependencies Confirmed

- 001: energy component/state fields and request config exist.
- 002: compiler/driver producers write energy fields and energy enabled gates.

## Acceptance Criteria

- Cost/rate cadence, bounded loop, zero-rate disabled behavior, deterministic ids, positive threshold guard, and energy CombatRoot path.

## Validation Required

- Static searches plus compilation/test validation after consumer update. Report anything that requires task 004 test edits.

## Hard Boundaries

- Do not modify tests or docs in this task, even if they need later vocabulary updates. Do not alter enqueue branch contents or create a second data path.
