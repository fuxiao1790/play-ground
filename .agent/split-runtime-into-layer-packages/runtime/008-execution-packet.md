# Task Execution Packet

## Task

008-invert-stats-display-coupling.md

## Goal

Validate the user-completed pull inversion: `PerformanceText` reads `CombatStatsSingleton` from the ECS world and the simulation no longer references Debugging.

## Files Allowed To Modify

- None. This task is already implemented in the working tree.

## Dependencies Confirmed

- `CombatStatsGatherSystem` publishes `CombatStatsSingleton`.
- `PerformanceText` reads that singleton from `World.DefaultGameObjectInjectionWorld`.

## Acceptance Criteria

- No `PerformanceText` under `Assets/Scripts/System`.
- No `CombatStatsBinding` under `Assets` or `Packages`.

## Validation Required

- Run both searches and `git diff --check`.

## Hard Boundaries

- Do not alter the user-completed direct ECS-world pull design.
