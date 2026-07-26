# 004 — Tag-filter the support picker

## Goal

Per user direction: "picker should only show valid choices. there should be a tag
system. skills should have tags and only supports sharing a tag with the skill
should be shown." The tag system already exists (`SkillDefinitionTags`,
`Skill.Tags`) but the compatibility property (`SupportedSkillTags`) only lives on
`StatModifierSupport`, and the picker doesn't use it at all — it lists every
catalog support unconditionally. Generalize the property to the `SkillSupport`
base and filter the picker with it.

## Changes

1. **`Assets/Scripts/Skills/Support/SkillSupport.cs`** — add a virtual default so
   every support type has a queryable compatibility, defaulting to "always shown":
   ```csharp
   using PlayGround.Common;

   namespace PlayGround.Skills
   {
       public abstract class SkillSupport : PersistentScriptableObject
       {
           public virtual SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;
       }
   }
   ```

2. **`Assets/Scripts/Skills/Support/StatModifierSupport.cs`** — re-abstract the
   override so concrete stat-modifier supports still must declare their tags
   explicitly (behavior-preserving — this is the one subclass that already had the
   property):
   ```csharp
   namespace PlayGround.Skills
   {
       public abstract class StatModifierSupport : SkillSupport
       {
           public abstract override SkillDefinitionTags SupportedSkillTags { get; }
       }
   }
   ```
   `ConversionSupport`/`StackingSupport` and any other non-stat-modifier support
   keep the base `Any` default — identical to their current (unfiltered) runtime
   behavior.

3. **`Assets/Scripts/Skills/SkillLoadoutValidator.cs`** — simplify
   `ValidateSkillSet`'s support loop (lines 98-109) now that every `SkillSupport`
   exposes the property; drop the `StatModifierSupport`-only type check:
   ```csharp
   SkillSupport[] supports = skillSet.Supports;
   for (int i = 0; i < supports.Length; i++)
   {
       SkillSupport support = supports[i];
       if (support == null) continue;
       if (SkillDefinitionTagUtility.HasAny(skillTags, support.SupportedSkillTags))
           continue;

       AddWarning(warnings, SkillValidationWarningCode.UnsupportedSupportForSkill, slotIndex,
           $"Support '{support.name}' on skill set '{skillSet.name}' supports {SkillDefinitionTagUtility.Format(support.SupportedSkillTags)}, but skill '{skill.name}' is {SkillDefinitionTagUtility.Format(skillTags)}. Support will be ignored.");
   }
   ```
   Behavior-preserving: non-`StatModifierSupport` types default to `Any`, so
   `HasAny` is always true for them and they're never flagged — same as being
   skipped by the old type check.

4. **`Assets/Scripts/SkillUi/SkillLoadoutUi.cs`** — in `OpenPicker` (lines 220-225),
   filter the `Support` branch by tag compatibility with the target node's current
   skill:
   ```csharp
   if (target.Kind == PickerKind.Support)
   {
       SkillDefinitionTags skillTags = GetSkillSet(target.NodeIndex)?.Skill?.Tags ?? SkillDefinitionTags.None;
       foreach (var entry in catalog.Supports)
       {
           if (!SkillDefinitionTagUtility.HasAny(skillTags, entry.Definition.SupportedSkillTags))
               continue;
           AddChoice(choices, entry.DisplayName,
               new SkillLoadoutEditCommand(skillDriver.Revision, SkillLoadoutEditKind.SetSupport,
                   target.NodeIndex, target.SupportIndex, support: entry.Definition));
       }
   }
   ```
   Leave the `Skill` and `Trigger` branches unfiltered (out of scope — see
   index.md open questions). `AddClear` is untouched — Clear/None always shows
   regardless of tags. No new `using` needed: `SkillLoadoutUi` is already in
   `namespace PlayGround.Skills`, same as `SkillDefinitionTagUtility`.

## Notes

- The support picker is only reachable from an already-equipped node's support
  button (`AddNodeColumn` only creates support buttons when `skillSet != null`),
  so `GetSkillSet(target.NodeIndex)?.Skill` is expected non-null in practice; the
  `?? SkillDefinitionTags.None` fallback is defensive only, matching the existing
  null-safe style used elsewhere in this file (e.g. `nodes[i]?.SkillSet`).
- This makes "shown in picker" and "accepted without a validator warning" the same
  predicate (`HasAny(skillTags, support.SupportedSkillTags)`) — no second,
  drifting definition of compatibility to maintain.

## Acceptance Criteria

- Open the support picker on a node whose skill has `Tags = Projectile`: only
  catalog supports with `SupportedSkillTags` including `Projectile` (or `Any`)
  appear. A support with `SupportedSkillTags = Aoe` does not appear.
- `Clear` always appears in the support picker regardless of tags.
- `ConversionSupport`/`StackingSupport`-derived catalog entries (no explicit tag
  override) appear for every skill, matching their current unfiltered behavior.
- `SkillValidationEditModeTests.cs` (existing tag-mismatch coverage for
  `StatModifierSupport`) still passes unmodified after the validator
  simplification.
- Skill picker and trigger picker are unaffected — still list every catalog entry.

## Dependencies

None functionally. Touches the same `OpenPicker`/`AddChoice` region as task 003 —
implement after 003 (see index.md ordering note) to avoid reworking the same
methods twice.

## Scope

Small — one property moved up a class hierarchy with a default, one validator
loop simplified (behavior-preserving), one picker branch gains a filter.
