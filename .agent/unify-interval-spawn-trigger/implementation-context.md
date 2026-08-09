# Implementation Context

## Architectural Decisions
- One concrete `IntervalSpawnTrigger` replaces projectile, AOE, and targeted interval trigger variants.
- Interval child setup dispatch uses compiled child runtime type.
- `SkillDefinitionTags.Interval` identifies duration-capable interval sources; it is not part of `Any`.

## Global Invariants
- `Any` remains `Projectile | Aoe | Targeted`.
- Only `ProjectileSkill` and `LingeringAoeSkill` carry `Interval`.
- Pulse AOE and targeted skills remain invalid interval sources.
- Existing setup types, their host fields, and ECS consumers remain unchanged.
- Do not edit Unity `.asset` or `.meta` YAML. User rebinds four affected trigger assets in Unity Editor.

## Ownership Boundaries
- Player-facing authoring: `IntervalSpawnTrigger` and validation.
- Compilation: `SkillSetCompiler` creates existing runtime interval setup types.
- ECS/runtime spawn systems untouched.

## Data Flow
- Trigger link -> generic source/target tag validation -> compiler compiles child -> runtime-type switch -> matching existing setup field.

## Lifecycle / Allocation Rules
- No lifecycle or allocation changes.

## ECS / Job / Threading Constraints
- No ECS/job/threading changes.

## Determinism Requirements
- Preserve existing `nextChildJitterSeed` increment behavior and formulas.

## Producer / Consumer Separation
- Preserve existing runtime setup producer/consumer boundaries.

## Reused Mechanisms
- Generic tag validation, existing interval energy/mana helpers, existing runtime setup classes and host fields.

## Introduced Mechanisms
- `SkillDefinitionTags.Interval`.

## Validation Requirements
- Static source/search checks by agent.
- User runs Unity EditMode and PlayMode tests, exports XML. Agent reviews XML before reporting test success.

## Files / Systems Mentioned By The Plan
- Skill tags and skill subclasses; interval trigger classes; compiler; validator; five test files; three documentation files.
