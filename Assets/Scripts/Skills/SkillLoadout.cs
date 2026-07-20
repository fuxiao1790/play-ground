using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Skills
{
    [global::System.Serializable]
    public sealed class SkillLoadoutNode
    {
        [SerializeField] private SkillSet skillSet;
        [SerializeField] private TriggerLink triggerToNext;

        public SkillSet SkillSet => skillSet;
        public TriggerLink TriggerToNext => triggerToNext;

        public SkillLoadoutNode(SkillSet skillSet, TriggerLink triggerToNext = null)
        {
            this.skillSet = skillSet;
            this.triggerToNext = triggerToNext;
        }

        internal SkillLoadoutNode CreateRuntimeClone()
        {
            return new SkillLoadoutNode(skillSet == null ? null : skillSet.CreateRuntimeClone(), triggerToNext);
        }

        internal void SetSkillSet(SkillSet value) => skillSet = value;
        internal void SetTriggerToNext(TriggerLink value) => triggerToNext = value;
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Skill Loadout", fileName = "NewSkillLoadout")]
    public sealed class SkillLoadout : ScriptableObject
    {
        [SerializeField] private List<SkillLoadoutNode> nodes = new();

        // Temporary serialized migration source. Task 003 removes this field and
        // its legacy types after the compiler reads Nodes.
        [SerializeField, SerializeReference] private List<LoadoutSlot> slots = new();
        [SerializeField, Min(1)] private int maxRootSets = 8;

        public IReadOnlyList<SkillLoadoutNode> Nodes => nodes;
        public IReadOnlyList<LoadoutSlot> Slots => slots;
        public int MaxRootSets => maxRootSets;

        internal SkillLoadout CreateRuntimeClone()
        {
            var clone = CreateInstance<SkillLoadout>();
            clone.name = $"{name} (Runtime)";
            clone.hideFlags = HideFlags.DontSave;
            clone.maxRootSets = maxRootSets;
            clone.nodes = new List<SkillLoadoutNode>(nodes.Count);

            for (int i = 0; i < nodes.Count; i++)
                clone.nodes.Add(nodes[i] == null ? null : nodes[i].CreateRuntimeClone());

            return clone;
        }

        internal static SkillLoadout CreateEmptyRuntime()
        {
            var runtime = CreateInstance<SkillLoadout>();
            runtime.name = "Empty Skill Loadout (Runtime)";
            runtime.hideFlags = HideFlags.DontSave;
            return runtime;
        }

        internal void EnsureRuntimeNodeCount(int count)
        {
            while (nodes.Count < count)
                nodes.Add(new SkillLoadoutNode(null));
        }

        internal void ReplaceRuntimeNodes(List<SkillLoadoutNode> replacement)
        {
            nodes = replacement ?? new List<SkillLoadoutNode>();
        }

#if UNITY_EDITOR
        public void ReplaceNodesFromMigration(List<SkillLoadoutNode> migratedNodes)
        {
            nodes = migratedNodes ?? new List<SkillLoadoutNode>();
        }
#endif
    }
}
