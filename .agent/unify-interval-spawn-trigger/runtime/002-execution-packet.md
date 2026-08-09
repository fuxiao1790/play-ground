# Task Execution Packet

## Task

002-merge-trigger-class.md

## Goal

Promote `IntervalSpawnTrigger` to one concrete, creatable player-facing interval trigger.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`

## Files Allowed To Delete

- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs` and `.meta`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs` and `.meta`
- `Assets/Scripts/Skills/Trigger/TargetedIntervalSpawnTrigger.cs` and `.meta`

## Behavior To Preserve

- Existing energy and mana helper methods; all serialized field names and attributes.

## Behavior To Change

- One trigger accepts interval sources and any skill-shape target.

## Dependencies Confirmed

- Task 001 added `SkillDefinitionTags.Interval` and applied it to valid sources.

## Acceptance Criteria

- Concrete creatable trigger owns all fields and tags; obsolete source/meta files deleted; assets untouched.

## Validation Required

- Static inspection/search. Unity tests deferred to user.

## Hard Boundaries

- Do not edit any `.asset`; editor rebind happens only by user.
