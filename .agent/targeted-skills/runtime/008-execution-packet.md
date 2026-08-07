# Task Execution Packet

## Task

008-authoring-prefab-and-skill-types.md

## Goal

Add Game Logic authoring types for VFX-only targeted chain skills. No ECS runtime registration or authored Unity asset creation.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Validator/TargetedPrefab.cs`
- `Assets/Scripts/Skills/Skill/TargetedSkill.cs`
- `Assets/Scripts/Skills/SkillDefinition.cs`
- `Assets/Scripts/Skills/SkillDefinitionTags.cs`
- `Assets/Tests/EditMode/TargetedAuthoringEditModeTests.cs`
- `.agent/targeted-skills/implementation-log.md`

## Files Allowed To Create

- `Assets/Scripts/Skills/Validator/TargetedPrefab.cs`
- `Assets/Scripts/Skills/Skill/TargetedSkillBase.cs`
- `Assets/Scripts/Skills/Skill/TargetedSkill.cs`
- `Assets/Scripts/Skills/Skill/LingeringTargetedSkill.cs`
- `Assets/Tests/EditMode/TargetedAuthoringEditModeTests.cs`

## Behavior To Preserve

- `SkillDefinitionTags.Any` remains Projectile | Aoe until task 011.
- Editor assets, VFX graphs, atlas entries, and loadout wiring remain user work.

## Behavior To Change

- Add Targeted skill tag, targeted prefab validation, single/lingering definitions, and Create menu skill classes.

## Relevant Global Context

- Game Logic owns authoring. Targeted has no hurtbox or gameplay shape. VFX-only prefab is valid. Link VFX shape is LineSegment.

## Dependencies Confirmed

- None. Task is pure authoring.

## Acceptance Criteria

- VFX-only and valid `Visual` renderer prefabs validate.
- Invalid child name, non-instanced material, and `Hurtbox` fail clearly.
- Both skills report `SkillDefinitionTags.Targeted`; definition copies are independent.

## Validation Required

- Focused EditMode test, whitespace/static audit, or report exact blocker.

## Hard Boundaries

- No runtime `TargetedTypeDefinition`; task 007 owns it.
- No Unity YAML or authored asset creation.
