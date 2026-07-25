# Task Execution Packet

## Task
004-player-mana-ecs-resource.md

## Goal
Author player max mana, seed ECS `TargetMana` with target proxy, and create the managed `PlayerMana` mirror.

## Files Allowed To Modify
- Assets/Scripts/Common/Stats/UnitStatSheet.cs
- Assets/Scripts/System/Targets/ICombatTarget.cs
- Assets/Scripts/System/Targets/CombatTargetProxy.cs
- Assets/Scripts/Player/PlayerMana.cs (new)
- Assets/Scripts/Player/PlayerRoot.cs
- Directly affected PlayMode tests.

## Relevant Global Context
- Copy `TargetHealth` seed/ownership and `PlayerHealth` holder patterns exactly.
- `TargetMana` belongs to every target proxy; interface defaults keep mobs at zero.
- Do not add consumption or ECS-to-MB update code yet.

## Dependencies Confirmed
- None.

## Acceptance Criteria
- Player proxy has max/current mana from stat sheet.
- Existing health path stays unchanged.

## Validation Required
- Focused PlayMode proxy test when project lock permits; static check otherwise.

## Hard Boundaries
- No drain, regeneration, or spawning gate.
