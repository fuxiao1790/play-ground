# Task Execution Packet

## Task

010-trigger-links.md

## Goal

Add projectile/AOE-to-targeted impact links and targeted interval-child links, including compiler setup, runtime template registration, validation, and collision event routing.

## Files Allowed To Modify/Create

- `Assets/Scripts/Skills/Trigger/OnImpactTargetedTrigger.cs` (new)
- `Assets/Scripts/Skills/Trigger/TargetedIntervalSpawnTrigger.cs` (new)
- `Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- projectile/AOE collision dispatch files and directly related tests.

## Dependencies Confirmed

- Task 009 runtime targeted definition/compiler/template path exists.
- Targeted event lanes already exist and currently reject unhandled on-hit targeted kinds.

## Hard Boundaries

- Targeted-as-source V2 links remain declarations only.
- Do not widen `Any`; task 011 owns that.
- Do not implement validation warning heuristics from task 013.
- No Unity YAML or authored assets.
