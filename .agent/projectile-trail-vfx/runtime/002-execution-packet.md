# Task Execution Packet

## Task
`002-runtime-and-registration-wiring.md`

## Goal
Carry authored trail id/width through runtime registration and skill-built projectile commands.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`

## Behavior To Preserve
- Direct `CombatRoot.ProjectileCommandFor` has no trail wiring and defaults id to `0`.
- VFX registration refreshes independently of projectile `TypeId` registration.

## Behavior To Change
- Runtime definition and command carry trail id/width.
- `SkillDriver` registers prefab trail VFX on every projectile-registration pass and copies values into interval templates.

## Relevant Global Context
- `CombatVfxRoot.Register` encodes shape and is idempotent per asset.
- Trail is optional; no asset or no VFX root resolves to id `0`.

## Dependencies Confirmed
- Task 001 properties `TrailEffect`, `TrailEffectShape`, and `TrailWidth` exist.

## Acceptance Criteria
- Skill-built commands preserve runtime trail id/width.
- Direct commands retain default id `0`.
- Re-registration refreshes trail values even after `TypeId` exists.

## Validation Required
- Static source inspection only. Unity tests deferred to user.

## Hard Boundaries
- Do not modify `CombatRoot.cs`.
- No source files outside allowed list.
