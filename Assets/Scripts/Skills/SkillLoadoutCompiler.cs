using System.Collections.Generic;
using PlayGround.Skills.Runtime;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillLoadoutCompiler
    {
        public static CompiledLoadout Compile(SkillLoadout loadout, SkillStatSnapshot snapshot)
        {
            var warnings = new List<SkillValidationWarning>(SkillLoadoutValidator.Validate(loadout));
            IReadOnlyList<SkillLoadoutNode> nodes = loadout.Nodes;

            var rootNodeIndices = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                SkillSet skillSet = nodes[i]?.SkillSet;
                if (skillSet == null) continue;

                bool hasIncomingTrigger = i > 0
                    && nodes[i - 1]?.SkillSet != null
                    && nodes[i - 1]?.TriggerToNext != null;
                if (!hasIncomingTrigger)
                    rootNodeIndices.Add(i);
            }

            int maxSlots = Mathf.Min(rootNodeIndices.Count, loadout.MaxRootSets);
            var roots = new RuntimeSkillDefinition[maxSlots];
            var resolvedRootNodeIndices = new int[maxSlots];
            int count = 0;
            for (int i = 0; i < maxSlots; i++)
            {
                RuntimeSkillDefinition def = SkillSetCompiler.Compile(nodes, rootNodeIndices[i], snapshot);
                if (def == null) continue;

                roots[count] = def;
                resolvedRootNodeIndices[count] = rootNodeIndices[i];
                count++;
                AppendCompilerWarnings(def, rootNodeIndices[i], warnings);
            }

            if (count != maxSlots)
            {
                global::System.Array.Resize(ref roots, count);
                global::System.Array.Resize(ref resolvedRootNodeIndices, count);
            }

            return new CompiledLoadout(roots, resolvedRootNodeIndices, count, warnings);
        }

        private static void AppendCompilerWarnings(
            RuntimeSkillDefinition def,
            int slotIndex,
            List<SkillValidationWarning> warnings)
        {
            if (def == null || warnings == null)
                return;

            if (def is RuntimeStackingDetonation stacking)
            {
                AppendCompilerWarnings(stacking.Detonation, slotIndex, warnings);
                return;
            }

            if (def is RuntimeProjectileDefinition projectile)
            {
                if (projectile.SpawnBlocked)
                {
                    warnings.Add(new SkillValidationWarning(
                        SkillValidationWarningCode.ContinuousCollisionCannotTrack,
                        slotIndex,
                        "Projectile cannot enable both continuous collision and tracking.",
                        SkillValidationSeverity.Error));
                }

                if (projectile.TrackingMayTunnel)
                {
                    warnings.Add(new SkillValidationWarning(
                        SkillValidationWarningCode.TrackingProjectileMayTunnel,
                        slotIndex,
                        "Tracking projectile may tunnel at its compiled speed.",
                        SkillValidationSeverity.Warning));
                }

                AppendCompilerWarnings(projectile.ChildSpawnSetup?.ChildDefinition, slotIndex, warnings);
                AppendCompilerWarnings(projectile.AoeIntervalSpawnSetup?.ChildDefinition, slotIndex, warnings);
                AppendCompilerWarnings(projectile.StackingDetonation, slotIndex, warnings);
                return;
            }

            if (def is RuntimeAoeDefinition aoe)
            {
                AppendCompilerWarnings(aoe.ChildSpawnSetup?.ChildDefinition, slotIndex, warnings);
                AppendCompilerWarnings(aoe.AoeIntervalSpawnSetup?.ChildDefinition, slotIndex, warnings);
                AppendCompilerWarnings(aoe.StackingDetonation, slotIndex, warnings);
            }
        }
    }

    public readonly struct CompiledLoadout
    {
        public readonly RuntimeSkillDefinition[] Roots;
        public readonly int[] RootNodeIndices;
        public readonly int Count;
        public readonly List<SkillValidationWarning> Warnings;

        public CompiledLoadout(
            RuntimeSkillDefinition[] roots,
            int[] rootNodeIndices,
            int count,
            List<SkillValidationWarning> warnings)
        {
            Roots = roots;
            RootNodeIndices = rootNodeIndices;
            Count = count;
            Warnings = warnings;
        }
    }
}
