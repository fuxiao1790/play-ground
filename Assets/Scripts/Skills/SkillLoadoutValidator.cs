using System.Collections.Generic;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;

namespace PlayGround.Skills
{
    public static class SkillLoadoutValidator
    {
        // Temporary test-fixture compatibility while the existing EditMode cases
        // move from managed-reference slots to normalized nodes.
        [global::System.Obsolete("Tests must use SkillLoadoutNode lists.")]
        public static void Validate(
            IReadOnlyList<LoadoutSlot> slots,
            List<SkillValidationWarning> warnings)
        {
            var nodes = new List<SkillLoadoutNode>();
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] is not SkillSetSlot skillSlot) continue;
                    TriggerLink trigger = i + 2 < slots.Count
                        && slots[i + 1] is TriggerLinkSlot triggerSlot
                        && slots[i + 2] is SkillSetSlot
                        ? triggerSlot.link
                        : null;
                    nodes.Add(new SkillLoadoutNode(skillSlot.skillSet, trigger));
                }
            }

            Validate(nodes, warnings);
        }

        public static SkillValidationWarning[] Validate(SkillLoadout loadout)
        {
            if (loadout == null) return global::System.Array.Empty<SkillValidationWarning>();

            var warnings = new List<SkillValidationWarning>();
            Validate(loadout.Nodes, warnings);
            return warnings.ToArray();
        }

        public static void Validate(
            IReadOnlyList<SkillLoadoutNode> nodes,
            List<SkillValidationWarning> warnings)
        {
            if (nodes == null || warnings == null) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                SkillLoadoutNode node = nodes[i];
                ValidateSkillSet(node?.SkillSet, i, warnings);
                if (node?.TriggerToNext != null)
                    ValidateTriggerLink(nodes, node.TriggerToNext, i, warnings);
            }
        }

        private static void ValidateSkillSet(
            SkillSet skillSet,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (skillSet == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.MissingSkillSet, slotIndex,
                    $"Skill slot {slotIndex} has no skill set.");
                return;
            }

            Skill skill = skillSet.Skill;
            if (skill == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.MissingSkill, slotIndex,
                    $"Skill set '{skillSet.name}' has no skill.");
                return;
            }

            SkillDefinitionTags skillTags = skill.Tags;
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

            if (skill.Definition is TargetedDefinition targeted)
                ValidateTargetedDefinition(targeted, slotIndex, warnings);
        }

        private static void ValidateTargetedDefinition(
            TargetedDefinition definition,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (definition.chainCount < 1 || definition.chainCount > 32)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                    $"Targeted chainCount {definition.chainCount} is outside [1, 32] and will be clamped.");
            }

            if (definition.echoCount < 1)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                    $"Targeted echoCount {definition.echoCount} is below 1 and will be clamped to 1.");
            }

            if (definition.chainDelay < 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                    "Targeted chainDelay is negative and will be clamped to 0.");
            }

            // chainDistance governs every hop, link 0 included, so a non-positive value cannot
            // even acquire a first target. Blocking beats firing a silent no-op.
            if (definition.chainDistance <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedConfigurationError, slotIndex,
                    "Targeted chainDistance must be greater than 0; this skill will not spawn.",
                    SkillValidationSeverity.Error);
            }

            if (definition.chainCount > 1 && definition.chainDamageFalloff <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedChainWarning, slotIndex,
                    "Targeted chainDamageFalloff is not positive; links after the first deal zero damage.");
            }

            TargetedPrefab prefab = definition.Prefab;
            if (prefab == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedVisualWarning, slotIndex,
                    "Targeted prefab is missing; link VFX cannot be configured.");
                return;
            }

            if (!prefab.IsValidTemplate(out string reason))
            {
                SkillValidationSeverity severity = reason.Contains("Hurtbox")
                    ? SkillValidationSeverity.Error
                    : SkillValidationSeverity.Warning;
                AddWarning(warnings, SkillValidationWarningCode.TargetedVisualWarning, slotIndex,
                    $"Targeted prefab is invalid: {reason}", severity);
            }

            if (prefab.LinkEffect == null || prefab.LinkEffectShape != VfxDataShape.LineSegment)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedVisualWarning, slotIndex,
                    "Targeted link VFX is missing or is not a LineSegment.");
            }

            if ((prefab.SpawnEffect != null || prefab.HitEffect != null
                    || prefab.ExpireEffect != null || prefab.ArmingEffect != null)
                && prefab.VfxEffectSize <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedVisualWarning, slotIndex,
                    "Targeted circular VFX are assigned but vfxEffectSize is not positive.");
            }

            if (prefab.LinkEffect != null && prefab.LinkWidth <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedVisualWarning, slotIndex,
                    "Targeted link VFX is assigned but linkWidth is not positive.");
            }
        }

        private static void ValidateTriggerLink(
            IReadOnlyList<SkillLoadoutNode> nodes,
            TriggerLink link,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (slotIndex + 1 >= nodes.Count
                || nodes[slotIndex]?.SkillSet == null
                || nodes[slotIndex + 1]?.SkillSet == null)
            {
                AddWarning(warnings, SkillValidationWarningCode.DanglingTriggerLink, slotIndex,
                    $"Trigger '{link.name}' at node {slotIndex} is not between two valid skill sets. Link will be ignored.");
                return;
            }

            SkillSet causeSet = nodes[slotIndex].SkillSet;
            SkillSet effectSet = nodes[slotIndex + 1].SkillSet;
            Skill causeSkill = causeSet.Skill;
            Skill effectSkill = effectSet.Skill;
            if (causeSkill == null || effectSkill == null)
                return;

            if (link.SourceSkillTags == SkillDefinitionTags.None
                || link.TargetSkillTags == SkillDefinitionTags.None)
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerLink, slotIndex,
                    $"Trigger '{link.name}' has no runtime-compatible skill tags. Link will be ignored.");
                return;
            }

            if (link is OnAoeHitSpawnTrigger)
            {
                ValidateAoeHitSpawnLink(link, slotIndex, causeSkill, effectSkill, warnings);
                return;
            }

            if (link is IntervalSpawnTrigger intervalTrigger
                && effectSkill.Definition is TargetedDefinition targetedEffect)
            {
                ValidateTargetedIntervalEnergyReachability(
                    intervalTrigger,
                    causeSkill.Definition,
                    targetedEffect,
                    slotIndex,
                    warnings);
            }

            if (!SkillDefinitionTagUtility.HasAny(causeSkill.Tags, link.SourceSkillTags))
            {
                SkillValidationSeverity severity = link is IntervalSpawnTrigger
                    ? SkillValidationSeverity.Error
                    : SkillValidationSeverity.Warning;
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
                    $"Trigger '{link.name}' expects {SkillDefinitionTagUtility.Format(link.SourceSkillTags)} source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.",
                    severity);
            }

            if (!SkillDefinitionTagUtility.HasAny(effectSkill.Tags, link.TargetSkillTags))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerTarget, slotIndex,
                    $"Trigger '{link.name}' expects {SkillDefinitionTagUtility.Format(link.TargetSkillTags)} target, but target skill '{effectSkill.name}' is {SkillDefinitionTagUtility.Format(effectSkill.Tags)}. Link will do nothing.");
            }
        }

        private static void ValidateAoeHitSpawnLink(
            TriggerLink link,
            int slotIndex,
            Skill causeSkill,
            Skill effectSkill,
            List<SkillValidationWarning> warnings)
        {
            if (!CanSourceAoeHitSpawn(causeSkill.Definition))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
                    $"Trigger '{link.name}' expects an AOE source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.");
            }

            if (!CanTargetAoeHitSpawn(effectSkill.Definition))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerTarget, slotIndex,
                    $"Trigger '{link.name}' expects an AOE target, but target skill '{effectSkill.name}' is {SkillDefinitionTagUtility.Format(effectSkill.Tags)}. Link will do nothing.");
            }
        }

        private static bool CanSourceAoeHitSpawn(SkillDefinition definition) =>
            definition is AoeDefinitionBase;

        private static bool CanTargetAoeHitSpawn(SkillDefinition definition) =>
            definition is AoeDefinitionBase;

        private static void ValidateTargetedIntervalEnergyReachability(
            IntervalSpawnTrigger trigger,
            SkillDefinition source,
            TargetedDefinition effect,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (!TryGetIntervalSourceLifetime(source, out float sourceLifetime))
                return;

            float sourceCapacity = trigger.ResolveEnergyPerSecond(SkillStatSnapshot.Identity) * sourceLifetime;
            float energyThreshold = trigger.ManaToEnergyCost(effect.manaCost);
            if (energyThreshold <= sourceCapacity)
                return;

            AddWarning(warnings, SkillValidationWarningCode.TargetedIntervalWarning, slotIndex,
                "Targeted interval child energy threshold exceeds what the source can accrue over its lifetime; it will never spawn.");
        }

        private static bool TryGetIntervalSourceLifetime(SkillDefinition source, out float lifetime)
        {
            switch (source)
            {
                case ProjectileDefinition projectile:
                    lifetime = projectile.lifetime > 0f ? projectile.lifetime : 0f;
                    return lifetime > 0f;

                case LingeringAoeDefinition lingeringAoe:
                    lifetime = lingeringAoe.lifetimeSeconds > 0f ? lingeringAoe.lifetimeSeconds : 0f;
                    return lifetime > 0f;

                default:
                    lifetime = 0f;
                    return false;
            }
        }

        private static void AddWarning(
            List<SkillValidationWarning> warnings,
            SkillValidationWarningCode code,
            int slotIndex,
            string message,
            SkillValidationSeverity severity = SkillValidationSeverity.Warning)
        {
            warnings.Add(new SkillValidationWarning(code, slotIndex, message, severity));
        }
    }
}
