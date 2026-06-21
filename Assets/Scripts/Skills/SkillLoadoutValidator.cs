using System.Collections.Generic;
using PlayGround.System.Common;

namespace PlayGround.Skills
{
    public static class SkillLoadoutValidator
    {
        public static SkillValidationWarning[] Validate(PlayerLoadout loadout)
        {
            if (loadout == null) return global::System.Array.Empty<SkillValidationWarning>();

            var warnings = new List<SkillValidationWarning>();
            Validate(loadout.Slots, warnings);
            return warnings.ToArray();
        }

        public static void Validate(
            IReadOnlyList<LoadoutSlot> slots,
            List<SkillValidationWarning> warnings)
        {
            if (slots == null || warnings == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is SkillSetSlot skillSlot)
                {
                    ValidateSkillSetSlot(skillSlot, i, warnings);
                    continue;
                }

                if (slots[i] is TriggerLinkSlot triggerSlot)
                    ValidateTriggerLinkSlot(slots, triggerSlot, i, warnings);
            }

        }

        private static void ValidateSkillSetSlot(
            SkillSetSlot slot,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (slot.skillSet == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.MissingSkillSet, slotIndex,
                    $"Skill slot {slotIndex} has no skill set.");
                return;
            }

            Skill skill = slot.skillSet.Skill;
            if (skill == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.MissingSkill, slotIndex,
                    $"Skill set '{slot.skillSet.name}' has no skill.");
                return;
            }

            SkillDefinitionTags skillTags = skill.Tags;
            AdditiveSupport[] supports = slot.skillSet.Supports;
            for (int i = 0; i < supports.Length; i++)
            {
                AdditiveSupport support = supports[i];
                if (support == null) continue;
                if (SkillDefinitionTagUtility.HasAny(skillTags, support.SupportedSkillTags))
                    continue;

                AddWarning(warnings, SkillValidationWarningCode.UnsupportedSupportForSkill, slotIndex,
                    $"Support '{support.name}' on skill set '{slot.skillSet.name}' supports {SkillDefinitionTagUtility.Format(support.SupportedSkillTags)}, but skill '{skill.name}' is {SkillDefinitionTagUtility.Format(skillTags)}. Support will be ignored.");
            }
        }

        private static void ValidateTriggerLinkSlot(
            IReadOnlyList<LoadoutSlot> slots,
            TriggerLinkSlot slot,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (slot.link == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.DanglingTriggerLink, slotIndex,
                    $"Trigger slot {slotIndex} has no trigger link.");
                return;
            }

            if (slotIndex <= 0 || slotIndex + 1 >= slots.Count
                || slots[slotIndex - 1] is not SkillSetSlot causeSlot
                || slots[slotIndex + 1] is not SkillSetSlot effectSlot
                || causeSlot.skillSet == null
                || effectSlot.skillSet == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.DanglingTriggerLink, slotIndex,
                    $"Trigger '{slot.link.name}' at slot {slotIndex} is not between two valid skill sets. Link will be ignored.");
                return;
            }

            Skill causeSkill = causeSlot.skillSet.Skill;
            Skill effectSkill = effectSlot.skillSet.Skill;
            if (causeSkill == null || effectSkill == null)
                return;

            if (slot.link.SourceSkillTags == SkillDefinitionTags.None
                || slot.link.TargetSkillTags == SkillDefinitionTags.None)
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerLink, slotIndex,
                    $"Trigger '{slot.link.name}' has no runtime-compatible skill tags. Link will be ignored.");
                return;
            }

            if (!SkillDefinitionTagUtility.HasAny(causeSkill.Tags, slot.link.SourceSkillTags))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
                    $"Trigger '{slot.link.name}' expects {SkillDefinitionTagUtility.Format(slot.link.SourceSkillTags)} source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.");
            }

            if (!SkillDefinitionTagUtility.HasAny(effectSkill.Tags, slot.link.TargetSkillTags))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerTarget, slotIndex,
                    $"Trigger '{slot.link.name}' expects {SkillDefinitionTagUtility.Format(slot.link.TargetSkillTags)} target, but target skill '{effectSkill.name}' is {SkillDefinitionTagUtility.Format(effectSkill.Tags)}. Link will do nothing.");
            }
        }

        private static void AddWarning(
            List<SkillValidationWarning> warnings,
            SkillValidationWarningCode code,
            int slotIndex,
            string message)
        {
            warnings.Add(new SkillValidationWarning(code, slotIndex, message));
        }
    }
}
