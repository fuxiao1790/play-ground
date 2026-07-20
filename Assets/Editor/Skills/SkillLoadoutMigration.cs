using System.Collections.Generic;
using PlayGround.Skills;
using UnityEditor;
using UnityEngine;

namespace PlayGround.Editor.Skills
{
    public static class SkillLoadoutMigration
    {
        [MenuItem("Tools/PlayGround/Skills/Migrate Loadouts To Normalized Nodes")]
        public static void MigrateAll()
        {
            string[] assetGuids = AssetDatabase.FindAssets("t:SkillLoadout");
            var issues = new List<string>();
            int migratedCount = 0;

            for (int i = 0; i < assetGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                var loadout = AssetDatabase.LoadAssetAtPath<SkillLoadout>(assetPath);
                if (loadout == null)
                {
                    issues.Add($"{assetPath}: AssetDatabase returned no SkillLoadout.");
                    continue;
                }

                List<SkillLoadoutNode> nodes = BuildNodes(loadout.Slots, assetPath, issues);
                loadout.ReplaceNodesFromMigration(nodes);
                EditorUtility.SetDirty(loadout);
                migratedCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Skill loadout migration complete. Migrated {migratedCount} loadouts; issues: {issues.Count}.");
            for (int i = 0; i < issues.Count; i++)
                Debug.LogWarning($"Skill loadout migration: {issues[i]}");
        }

        public static void MigrateAllInBatchMode()
        {
            MigrateAll();
        }

        private static List<SkillLoadoutNode> BuildNodes(
            IReadOnlyList<LoadoutSlot> slots,
            string assetPath,
            List<string> issues)
        {
            var nodes = new List<SkillLoadoutNode>();
            if (slots == null)
            {
                issues.Add($"{assetPath}: legacy slot list is null.");
                return nodes;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is SkillSetSlot skillSlot)
                {
                    TriggerLink triggerToNext = GetTriggerToNext(slots, i, assetPath, issues);
                    nodes.Add(new SkillLoadoutNode(skillSlot.skillSet, triggerToNext));
                    continue;
                }

                if (slots[i] is TriggerLinkSlot)
                {
                    if (!HasValidNeighbors(slots, i))
                        issues.Add($"{assetPath}: trigger slot {i} has no adjacent source and target skill slots.");
                    else if (((TriggerLinkSlot)slots[i]).link == null)
                        issues.Add($"{assetPath}: trigger slot {i} has no trigger asset.");

                    continue;
                }

                issues.Add($"{assetPath}: unsupported or null legacy slot at index {i}; no normalized node was created.");
            }

            return nodes;
        }

        private static TriggerLink GetTriggerToNext(
            IReadOnlyList<LoadoutSlot> slots,
            int sourceIndex,
            string assetPath,
            List<string> issues)
        {
            int triggerIndex = sourceIndex + 1;
            if (triggerIndex >= slots.Count || slots[triggerIndex] is not TriggerLinkSlot triggerSlot)
                return null;

            if (!HasValidNeighbors(slots, triggerIndex))
            {
                issues.Add($"{assetPath}: trigger slot {triggerIndex} cannot migrate because it has no adjacent target skill slot.");
                return null;
            }

            return triggerSlot.link;
        }

        private static bool HasValidNeighbors(IReadOnlyList<LoadoutSlot> slots, int triggerIndex)
        {
            return triggerIndex > 0
                && triggerIndex + 1 < slots.Count
                && slots[triggerIndex - 1] is SkillSetSlot
                && slots[triggerIndex + 1] is SkillSetSlot;
        }
    }
}
