# 002 - Normalize loadout topology and migrate assets

## Change

Refactor `SkillLoadout` to one ordered, unbounded node list. Each serializable
node contains:

- nullable `SkillSet skillSet` (`null` means empty position);
- nullable `TriggerLink triggerToNext` (`null` means next equipped node is a new
  root);
- no UI counts or cooldown state.

Keep `SkillSet` as the reusable authored skill-plus-support template. Add an
internal deep runtime-clone path that clones every referenced `SkillSet` per
node, even when two nodes reference the same authored asset. Skill, support, and
trigger definition assets remain shared/read-only.

Perform serialized migration in two controlled phases:

1. Temporarily retain the legacy flat slot field/types only for editor migration.
   Add a menu/batch command that converts every `SkillLoadout` asset in place,
   preserving slot order, independent roots, trigger adjacency, repeated skill
   set references, `maxRootSets`, and empty/malformed entries as reported issues.
2. Validate migrated assets, prefabs, scenes, and tests. After task 003 consumes
   the normalized model, remove the legacy field/types and migration command.

Do not leave a runtime fallback that reads old and new fields.

## Acceptance criteria

- Every loadout asset has a normalized node list with behavior equivalent to its
  old flat slots.
- Multiple roots and deep forward chains retain the same cause/effect order.
- Repeated `SkillSet` references become distinct runtime clones.
- Original loadout and skill-set assets remain unchanged during Play mode edits.
- A validation report lists migrated count and any malformed legacy asset; no
  silent drops.
- Final YAML contains no legacy managed-reference slot records after cleanup.

## Dependencies

- 001 documentation defines final serialized/runtime meaning.

## Scope / complexity

High. Serialized migration is the main compatibility risk.

