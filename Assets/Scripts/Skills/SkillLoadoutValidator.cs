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

            var stackTriggerEffectIndices = new HashSet<int>();
            for (int i = 1; i + 1 < slots.Count; i++)
            {
                if (slots[i] is TriggerLinkSlot { link: StackTrigger }
                    && slots[i - 1] is SkillSetSlot { skillSet: not null }
                    && slots[i + 1] is SkillSetSlot { skillSet: not null })
                {
                    stackTriggerEffectIndices.Add(i + 1);
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is SkillSetSlot skillSlot)
                {
                    ValidateSkillSetSlot(skillSlot, i, warnings);
                    ValidateStackingSupportReachability(skillSlot, i, stackTriggerEffectIndices, warnings);
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
            SkillSupport[] supports = slot.skillSet.Supports;
            for (int i = 0; i < supports.Length; i++)
            {
                if (supports[i] is not AdditiveSupport support)
                    continue;

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

            if (slot.link is StackTrigger)
            {
                ValidateStackTriggerTarget(slot, slotIndex, effectSlot.skillSet, warnings);
                return;
            }

            if (HasStackingSupport(effectSlot.skillSet))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, slotIndex,
                    $"Trigger '{slot.link.name}' targets stacking set '{effectSlot.skillSet.name}' but is not a StackTrigger. Link will do nothing.");
            }

            if (slot.link.SourceSkillTags == SkillDefinitionTags.None
                || slot.link.TargetSkillTags == SkillDefinitionTags.None)
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerLink, slotIndex,
                    $"Trigger '{slot.link.name}' has no runtime-compatible skill tags. Link will be ignored.");
                return;
            }

            if (slot.link is OnAoeHitSpawnTrigger)
            {
                ValidateAoeHitSpawnLink(slot, slotIndex, causeSkill, effectSkill, warnings);
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

        private static void ValidateAoeHitSpawnLink(
            TriggerLinkSlot slot,
            int slotIndex,
            Skill causeSkill,
            Skill effectSkill,
            List<SkillValidationWarning> warnings)
        {
            if (!CanSourceAoeHitSpawn(causeSkill.Definition))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
                    $"Trigger '{slot.link.name}' expects an AOE source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.");
            }

            if (!CanTargetAoeHitSpawn(effectSkill.Definition))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerTarget, slotIndex,
                    $"Trigger '{slot.link.name}' expects an AOE target, but target skill '{effectSkill.name}' is {SkillDefinitionTagUtility.Format(effectSkill.Tags)}. Link will do nothing.");
            }
        }

        private static bool CanSourceAoeHitSpawn(SkillDefinition definition) =>
            definition is AoeDefinitionBase;

        private static bool CanTargetAoeHitSpawn(SkillDefinition definition) =>
            definition is AoeDefinitionBase;

        private static void ValidateStackingSupportReachability(
            SkillSetSlot slot,
            int slotIndex,
            HashSet<int> stackTriggerEffectIndices,
            List<SkillValidationWarning> warnings)
        {
            if (slot.skillSet == null || !HasStackingSupport(slot.skillSet))
                return;

            if (stackTriggerEffectIndices.Contains(slotIndex))
                return;

            AddWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, slotIndex,
                $"Stacking set '{slot.skillSet.name}' is not the effect of a StackTrigger. Set will never fire.");
        }

        private static void ValidateStackTriggerTarget(
            TriggerLinkSlot slot,
            int slotIndex,
            SkillSet effectSet,
            List<SkillValidationWarning> warnings)
        {
            if (HasStackingSupport(effectSet))
                return;

            AddWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, slotIndex,
                $"StackTrigger '{slot.link.name}' targets skill set '{effectSet.name}' with no StackingSupport. Nothing will be baked.");
        }

        private static bool HasStackingSupport(SkillSet set)
        {
            if (set == null)
                return false;

            SkillSupport[] supports = set.Supports;
            if (supports == null)
                return false;

            for (int i = 0; i < supports.Length; i++)
            {
                if (supports[i] is StackingSupport)
                    return true;
            }

            return false;
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
