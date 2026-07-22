# Task Execution Packet

## Task

001-runtime-energy-fields.md

## Goal

Replace interval/cooldown field vocabulary in timed-spawn ECS components and runtime setup/request types.

## Files Allowed To Modify

- `Assets/Scripts/System/Spawning/TimedSpawnComponents.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs`

## Behavior To Preserve

- Faction, source, child kind, template key, jitter seed, tick index, and struct field-order intent.

## Behavior To Change

- Replace interval fields with rate, threshold, and threshold-jitter fields; replace cooldown state with accumulated energy.

## Relevant Global Context

- This is only vocabulary. Producers and consumers change in tasks 002/003, so intermediate compilation failure is expected.
- No new component or data path. Threshold and rate must remain nonnegative in request construction.

## Dependencies Confirmed

- None; task 001 starts the plan.

## Acceptance Criteria

- Four types have energy vocabulary and no interval fields in the listed types.

## Validation Required

- Static inspection/search; full compile deferred until tasks 002/003.

## Hard Boundaries

- Do not modify files outside this list or implement later task behavior.
