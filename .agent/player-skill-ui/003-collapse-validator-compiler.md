# 003 - Collapse validator and compiler onto normalized nodes

## Change

Refactor `SkillLoadoutValidator`, `SkillSetCompiler`, and the custom loadout
editor to consume normalized nodes directly.

- Root rule: equipped node `i` is root when `i == 0`, previous node is empty, or
  previous node has no valid `triggerToNext`.
- Chain rule: node `i` can trigger only node `i + 1`; recursion always advances
  and cannot cycle.
- Empty nodes are valid only with no supports owned by a runtime set and no
  adjacent trigger depending on them.
- Reuse the current support/tag, trigger source/target, stacking, interval-spawn,
  and max-depth predicates. Expose the same predicates to edit eligibility so
  picker and compile cannot disagree.
- Keep authored validation warnings for broken assets. Add edit rejection data
  with stable code and player-facing reason; invalid candidates never compile.
- Update the custom inspector to edit normalized nodes and outgoing triggers in
  one row/card.
- Delete `LoadoutSlot`, `SkillSetSlot`, `TriggerLinkSlot`, `TriggerChain`,
  `ParseChains`, old overloads, and temporary migration code after all assets and
  tests move.

## Acceptance criteria

- Validator, compiler, inspector, and runtime editor use one node topology.
- No adapter converts normalized nodes back into flat slots.
- Existing valid loadouts compile to equivalent runtime definitions.
- Existing invalid-support/trigger/stacking warnings remain covered.
- Empty nodes, three roots, one-link chains, and two-link chains have explicit
  tests.
- Repository search finds no final references to removed topology types.

## Dependencies

- 002 normalized model and migrated fixtures/assets.

## Scope / complexity

High. Broad refactor, but localized to skill authoring/game logic and tests.

