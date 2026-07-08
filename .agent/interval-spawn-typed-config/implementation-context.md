# Implementation Context

## Architectural Decisions
- Keep projectile and AOE interval spawn as two trigger types.
- Make trigger fields child-type-specific: projectile trigger owns `projectileCount` plus `sideSpreadDegrees`; AOE trigger owns `echoCount` plus `scatterRadius`.
- Counts are additive with the child skill's own multiplicity and floored to 1.
- AOE interval `scatterRadius` is authoritative for interval burst geometry.

## Global Invariants
- Runtime ECS systems, `TimedSpawnComponent`, `TimedSpawnSystem`, and expansion jobs stay unchanged.
- `AoeSpawnCommand.ScatterRadius` already exists and is consumed by AOE expansion.
- Non-interval AOE template registration must keep using the child AOE definition's own `ScatterRadius`.
- Validator target tags remain split: projectile interval target is `Projectile`; AOE interval target is `Aoe`.

## Ownership Boundaries
- Authoring trigger fields live under `Assets/Scripts/Skills/Trigger`.
- Compile-time runtime setup fields live in `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`.
- Template registration/threading lives in `PlayerSkillDriver`.
- ECS expansion/apply code is out of scope.

## Data Flow
- Trigger fields are compiled by `SkillSetCompiler` into `RuntimeChildSpawnSetup` or `RuntimeAoeIntervalSpawnSetup`.
- `RuntimeAoeIntervalSpawnSetup.ScatterRadius` is passed by `PlayerSkillDriver.RegisterAoeIntervalTemplate` to `SkillIntervalTemplateBuilder.BuildAoeTemplate`.
- `BuildAoeTemplate` bakes `ScatterRadius` into `AoeSpawnCommand`.

## Lifecycle / Allocation Rules
- No new runtime allocation path, entity lifecycle, or template ownership change.

## ECS / Job / Threading Constraints
- Do not modify ECS jobs or structural-change behavior.
- Existing `AoeSpawnCommand.ScatterRadius` consumer remains the final scatter path.

## Determinism Requirements
- Existing template hash/dedup behavior must include scatter through the existing command hash.
- Jitter seed behavior is unchanged.

## Producer / Consumer Separation
- Keep spawn intent in spawn setup/template paths, not damage callbacks.

## Reused Mechanisms
- Existing two trigger types and compiler branch shape.
- Existing AOE template command and scatter field.

## Introduced Mechanisms
- `RuntimeAoeIntervalSpawnSetup.ScatterRadius`.
- Optional `scatterRadiusOverride` parameter on `BuildAoeTemplate`.

## Validation Requirements
- Search for stale interval-trigger `spawnCount`, stale AOE interval `sideSpreadDegrees`, and stale merged `IntervalSpawnTrigger`.
- Build or explain build limits.
- Update tests and docs to match shipped fields.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/PlayerSkillDriver.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Docs/reference/game-logic/skill-system.md`
