# Task Execution Packet

## Task

004-tag-filtered-support-picker.md

## Goal

Generalize support tag compatibility to `SkillSupport`, retain explicit tags for stat modifiers, reuse the same predicate in validation and hide incompatible support choices in the picker.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Support/SkillSupport.cs`
- `Assets/Scripts/Skills/Support/StatModifierSupport.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- The four allowed files
- `Assets/Scripts/Skills/SkillDefinitionTags.cs`
- `Assets/Scripts/Skills/Skill.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`

## Behavior To Preserve

- Non-stat modifier supports retain universal (`Any`) behavior.
- Existing stat modifier support tag declarations remain mandatory.
- Skill and trigger picker branches remain unfiltered; Clear remains visible.
- Validation warning wording and mismatch behavior remain intact.

## Behavior To Change

- All `SkillSupport` instances expose `SupportedSkillTags` with `Any` default.
- Validator evaluates every support against that property.
- Support picker lists only entries whose mask shares a tag with the selected node skill.

## Relevant Global Context

Task 003 is complete. Use only `SkillDefinitionTagUtility.HasAny` for compatibility—no second predicate, no validator-driven disabled UI, and no trigger filtering.

## Dependencies Confirmed

- `SkillDefinitionTags` and `SkillDefinitionTagUtility.HasAny` exist.
- Every concrete `StatModifierSupport` currently overrides the property; base non-stat supports inherit `Any`.
- Task 003 picker submit path exists and must remain untouched except the support branch filter.

## Step-By-Step Instructions

1. Add virtual `SupportedSkillTags => Any` to `SkillSupport`.
2. Re-abstract it as an override in `StatModifierSupport`.
3. Simplify validator loop to read `SkillSupport` and evaluate the base property.
4. Expand only the support picker branch to filter with target skill tags and `HasAny`.

## Acceptance Criteria

- Projectile supports show for projectile skills and AOE-only supports do not.
- Clear remains visible.
- Untagged non-stat support types show universally via `Any`.
- Existing skill validation tag tests remain valid.

## Validation Required

- `git diff --check`, static searches, and the relevant EditMode test if Unity lock is cleared.

## Hard Boundaries

- No new tag system, UXML/USS, eligibility reason UI, or trigger picker changes.
- Modify only the allowed files.
