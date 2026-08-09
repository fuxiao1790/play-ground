# Task Execution Packet

## Task

004-update-validator.md

## Goal

Use generic source-tag validation for interval sources and report only interval source mismatches as errors.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Skills/SkillDefinitionTags.cs`

## Behavior To Preserve

- Other trigger source/target mismatch messages and Warning severity; targeted-energy reachability behavior.

## Behavior To Change

- Remove bespoke pulse-AOE interval source check; interval source tag mismatch becomes Error with readable Interval format.

## Dependencies Confirmed

- `Interval` tag and concrete merged trigger from tasks 001-002 exist.

## Acceptance Criteria

- No deleted trigger or ad-hoc helper references; generic Error behavior only for interval source mismatch.

## Validation Required

- Static inspection/search. Unity tests deferred to user.

## Hard Boundaries

- Do not change generic target validation or unrelated trigger behavior.
