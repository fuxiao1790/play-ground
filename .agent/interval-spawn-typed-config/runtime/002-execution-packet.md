# Task Execution Packet

## Task
002-runtime-setup-and-compiler.md

## Goal
Swap AOE interval setup from dead `SideSpreadDegrees` to live `ScatterRadius` and update compiler references to typed trigger fields.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs` (comment compile fix from current merged state)
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs` (compile/adaptation fix from current merged state)

## Behavior To Preserve
- Projectile interval count remains additive and spread remains authoritative.
- AOE interval count remains additive.
- Pulse AOE sources remain no-op/warn.

## Behavior To Change
- AOE interval setup carries `ScatterRadius`.
- AOE interval scatter uses trigger `scatterRadius` authoritatively.
- Current merged compiler/validator paths are restored to two trigger types.

## Dependencies Confirmed
- 001 complete: both trigger types exist with typed fields.

## Validation Required
- Search for old merged trigger references in compiler/validator.
- Search for stale interval `.spawnCount` and `SideSpreadDegrees`.

## Hard Boundaries
- Do not update template threading/tests/docs here except compile fixes caused by restored two-trigger shape.
