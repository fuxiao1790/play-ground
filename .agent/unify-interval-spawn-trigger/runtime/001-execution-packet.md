# Task Execution Packet

## Task

001-add-interval-tag.md

## Goal

Add interval capability tag and apply it only to duration-capable skill shapes.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillDefinitionTags.cs`
- `Assets/Scripts/Skills/Skill/ProjectileSkill.cs`
- `Assets/Scripts/Skills/Skill/AoeSkill.cs`
- `Assets/Scripts/Skills/Skill/LingeringAoeSkill.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Behavior To Preserve

- `Any` excludes `Interval`; pulse AOE and targeted remain non-interval.

## Behavior To Change

- Projectile and lingering AOE tags include `Interval`.

## Dependencies Confirmed

- `SkillDefinitionTags`, skill overrides, and UI bitwise compatibility filter exist.

## Acceptance Criteria

- Tag values and overrides match task specification; `Any` compatibility remains intact.

## Validation Required

- Static inspection/search. Unity tests deferred to user.

## Hard Boundaries

- No unrelated changes; no architecture changes; do not modify target skill.
