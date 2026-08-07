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

            var stackTriggerEffectIndices = new HashSet<int>();
            for (int i = 0; i + 1 < nodes.Count; i++)
            {
                if (nodes[i]?.TriggerToNext is StackTrigger
                    && nodes[i]?.SkillSet != null
                    && nodes[i + 1]?.SkillSet != null)
                {
                    stackTriggerEffectIndices.Add(i + 1);
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                SkillLoadoutNode node = nodes[i];
                ValidateSkillSet(node?.SkillSet, i, warnings);
                ValidateStackingSupportReachability(node?.SkillSet, i, stackTriggerEffectIndices, warnings);
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

            if (skill.Definition is TargetedDefinitionBase targeted)
                ValidateTargetedDefinition(targeted, slotIndex, warnings);
        }

        private static void ValidateTargetedDefinition(
            TargetedDefinitionBase definition,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (definition.maxTargets < 1 || definition.maxTargets > 32)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                    $"Targeted maxTargets {definition.maxTargets} is outside [1, 32] and will be clamped.");
            }

            if (definition.count < 1)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                    $"Targeted count {definition.count} is below 1 and will be clamped to 1.");
            }

            if (definition.chainDelaySeconds < 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                    "Targeted chainDelaySeconds is negative and will be clamped to 0.");
            }

            if (definition.acquireRadius <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedConfigurationError, slotIndex,
                    "Targeted acquireRadius must be greater than 0; this skill will not spawn.",
                    SkillValidationSeverity.Error);
            }

            if (definition.maxTargets > 1 && definition.chainRadius <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedChainWarning, slotIndex,
                    "Targeted maxTargets is greater than 1 but chainRadius is not positive; no jump can occur.");
            }

            if (definition.maxTargets > 1 && definition.chainDamageFalloff <= 0f)
            {
                AddWarning(warnings, SkillValidationWarningCode.TargetedChainWarning, slotIndex,
                    "Targeted chainDamageFalloff is not positive; links after the first deal zero damage.");
            }

            if (definition is LingeringTargetedDefinition lingering)
            {
                float chainDuration = TargetedChainDuration(definition);
                if (lingering.tickIntervalSeconds <= 0f)
                {
                    AddWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, slotIndex,
                        "Lingering targeted tickIntervalSeconds must be positive and will be clamped to 0.01.");
                }

                if (lingering.tickIntervalSeconds > lingering.lifetimeSeconds)
                {
                    AddWarning(warnings, SkillValidationWarningCode.TargetedIntervalWarning, slotIndex,
                        "Lingering targeted tickIntervalSeconds exceeds lifetimeSeconds; the walk fires once.");
                }

                if (chainDuration > lingering.tickIntervalSeconds)
                {
                    AddWarning(warnings, SkillValidationWarningCode.TargetedIntervalWarning, slotIndex,
                        "Targeted chain duration exceeds tickIntervalSeconds; later links are cut off by the next walk.");
                }

                if (chainDuration > lingering.lifetimeSeconds)
                {
                    AddWarning(warnings, SkillValidationWarningCode.TargetedIntervalWarning, slotIndex,
                        "Targeted chain duration exceeds lifetimeSeconds; the instance expires before one walk finishes.");
                }
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

        private static float TargetedChainDuration(TargetedDefinitionBase definition)
        {
            int maxTargets = definition.maxTargets < 1
                ? 1
                : definition.maxTargets > 32 ? 32 : definition.maxTargets;
            float delay = definition.chainDelaySeconds > 0f
                ? definition.chainDelaySeconds
                : 0f;
            return maxTargets * delay;
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

            if (link is StackTrigger)
            {
                ValidateStackTriggerTarget(link, slotIndex, effectSet, warnings);
                return;
            }

            if (HasStackingSupport(effectSet))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, slotIndex,
                    $"Trigger '{link.name}' targets stacking set '{effectSet.name}' but is not a StackTrigger. Link will do nothing.");
            }

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

            if (IsIntervalSpawnTrigger(link))
                ValidateIntervalSpawnSource(link, slotIndex, causeSkill, warnings);

            if (link is TargetedIntervalSpawnTrigger targetedInterval
                && effectSkill.Definition is TargetedDefinitionBase targetedEffect)
            {
                ValidateTargetedIntervalEnergyReachability(
                    targetedInterval,
                    causeSkill.Definition,
                    targetedEffect,
                    slotIndex,
                    warnings);
            }

            if (!SkillDefinitionTagUtility.HasAny(causeSkill.Tags, link.SourceSkillTags))
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
                    $"Trigger '{link.name}' expects {SkillDefinitionTagUtility.Format(link.SourceSkillTags)} source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.");
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

        private static void ValidateIntervalSpawnSource(
            TriggerLink link,
            int slotIndex,
            Skill causeSkill,
            List<SkillValidationWarning> warnings)
        {
            if (causeSkill.Definition is AoeDefinitionBase and not LingeringAoeDefinition)
            {
                AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
                    $"Trigger '{link.name}' uses AOE source skill '{causeSkill.name}', but interval spawn sources must be projectiles or lingering AOEs. Pulse AOEs have no duration to tick, so this link will do nothing.");
            }
        }

        private static void ValidateTargetedIntervalEnergyReachability(
            TargetedIntervalSpawnTrigger trigger,
            SkillDefinition source,
            TargetedDefinitionBase effect,
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

        private static bool IsIntervalSpawnTrigger(TriggerLink link) =>
            link is ProjectileIntervalSpawnTrigger or AoeIntervalSpawnTrigger or TargetedIntervalSpawnTrigger;

        private static void ValidateStackingSupportReachability(
            SkillSet skillSet,
            int slotIndex,
            HashSet<int> stackTriggerEffectIndices,
            List<SkillValidationWarning> warnings)
        {
            if (skillSet == null || !HasStackingSupport(skillSet))
                return;

            if (stackTriggerEffectIndices.Contains(slotIndex))
                return;

            AddWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, slotIndex,
                $"Stacking set '{skillSet.name}' is not the effect of a StackTrigger. Set will never fire.");
        }

        private static void ValidateStackTriggerTarget(
            TriggerLink link,
            int slotIndex,
            SkillSet effectSet,
            List<SkillValidationWarning> warnings)
        {
            if (HasStackingSupport(effectSet))
                return;

            AddWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, slotIndex,
                $"StackTrigger '{link.name}' targets skill set '{effectSet.name}' with no StackingSupport. Nothing will be baked.");
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
            string message,
            SkillValidationSeverity severity = SkillValidationSeverity.Warning)
        {
            warnings.Add(new SkillValidationWarning(code, slotIndex, message, severity));
        }
    }
}
