# Task Execution Packet

## Task

001-onhittrigger-collapse.md

## Goal

Replace four subtype-specific immediate on-hit triggers with fieldless `OnHitTrigger`; attach compiled target by compiled runtime source/target types.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

## Files Allowed To Create

- `Assets/Scripts/Skills/Trigger/OnHitTrigger.cs`

## Files Allowed To Delete

- Four legacy on-hit trigger `.cs` files and their `.meta` files.

## Files Likely Needed For Reading

- `TriggerLink`, tag definitions, runtime projectile/AOE/targeted definitions, compiler, validator.

## Behavior To Preserve

- Interval and stack compile paths; forward compilation; trigger mana stamping only after a successful attachment; target-as-source remains unwired.

## Behavior To Change

- All six Projectile/AOE source-to-Projectile/AOE/Targeted target cells use one trigger and attachment helper.
- Trigger no longer modifies projectile child count or spread.

## Relevant Global Context

- Trigger says when; child skill set says what. Generic tag validation replaces bespoke AOE-hit validation.

## Dependencies Confirmed

- None; legacy trigger types and existing compile/validator branches exist.

## Step-By-Step Instructions

1. Add fieldless `OnHitTrigger` with Projectile|Aoe source and Any target tags.
2. Delete four old trigger scripts and metadata.
3. Replace four compiler branches with `OnHitTrigger` plus `AttachOnHitTarget`.
4. Remove bespoke `OnAoeHitSpawnTrigger` validator path.

## Acceptance Criteria

- One on-hit branch and helper covers six runtime source/target combinations; helper writes fields only.
- Generic validation is used; old type/validator symbols are absent from source.

## Validation Required

- Static source/search verification now. Unity runtime/test compilation after required task 003 is user-run.

## Hard Boundaries

- No assets, ECS systems, runtime field changes, `SkillDriver`, stack, interval, or expire changes.
