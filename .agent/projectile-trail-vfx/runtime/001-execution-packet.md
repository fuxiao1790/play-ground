# Task Execution Packet

## Task
`001-authoring-projectile-trail-slot.md`

## Goal
Add optional projectile-trail VFX authoring data and validator warnings.

## Files Allowed To Modify
- `Assets/Scripts/System/Authoring/BasicAttackPrefab.cs`
- `Assets/Scripts/Skills/SkillValidationWarning.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

## Behavior To Preserve
- Existing untrailed projectile prefabs remain valid and warning-free.
- `BasicAttackPrefab.IsValidTemplate` stays unchanged.

## Behavior To Change
- Expose optional trail asset, `LineSegment` shape default, and positive-width default.
- Warn only when an assigned trail has wrong shape and/or non-positive width.

## Dependencies Confirmed
- None; `BasicAttackPrefab`, validator, and warning enum exist.

## Acceptance Criteria
- Default fields/properties match task specification.
- Assigned invalid trail produces one `ProjectileVisualWarning` per invalid condition.
- No assigned trail produces no new warning.

## Validation Required
- Static source inspection only. Unity tests deferred to user.

## Hard Boundaries
- Do not modify `IsValidTemplate` or `Configure`.
- No architecture changes or source files outside allowed list.
