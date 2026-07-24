using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Skill UI Catalog", fileName = "SkillUiCatalog")]
    public sealed class SkillUiCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class SkillEntry
        {
            [SerializeField] private Skill definition;
            [SerializeField] private string displayName;
            [SerializeField, TextArea] private string description;
            [SerializeField] private Sprite icon;

            public Skill Definition => definition;
            public string DisplayName => displayName;
            public string Description => description;
            public Sprite Icon => icon;
        }

        [Serializable]
        public sealed class SupportEntry
        {
            [SerializeField] private SkillSupport definition;
            [SerializeField] private string displayName;
            [SerializeField, TextArea] private string description;
            [SerializeField] private Sprite icon;

            public SkillSupport Definition => definition;
            public string DisplayName => displayName;
            public string Description => description;
            public Sprite Icon => icon;
        }

        [Serializable]
        public sealed class TriggerEntry
        {
            [SerializeField] private TriggerLink definition;
            [SerializeField] private string displayName;
            [SerializeField, TextArea] private string description;
            [SerializeField] private Sprite icon;

            public TriggerLink Definition => definition;
            public string DisplayName => displayName;
            public string Description => description;
            public Sprite Icon => icon;
        }

        [SerializeField] private List<SkillEntry> skills = new();
        [SerializeField] private List<SupportEntry> supports = new();
        [SerializeField] private List<TriggerEntry> triggers = new();

        public IReadOnlyList<SkillEntry> Skills => skills;
        public IReadOnlyList<SupportEntry> Supports => supports;
        public IReadOnlyList<TriggerEntry> Triggers => triggers;

        private void OnValidate()
        {
            ValidateEntries(skills, entry => entry.Definition, "skill");
            ValidateEntries(supports, entry => entry.Definition, "support");
            ValidateEntries(triggers, entry => entry.Definition, "trigger");
        }

        private void ValidateEntries<TEntry, TDefinition>(
            List<TEntry> entries,
            Func<TEntry, TDefinition> definition,
            string kind)
            where TEntry : class
            where TDefinition : UnityEngine.Object
        {
            var definitions = new HashSet<TDefinition>();
            for (int i = 0; i < entries.Count; i++)
            {
                TEntry entry = entries[i];
                TDefinition asset = entry == null ? null : definition(entry);
                if (asset == null)
                    Debug.LogError($"{name}: {kind} entry {i} needs a definition asset.", this);
                else if (!definitions.Add(asset))
                    Debug.LogError($"{name}: duplicate {kind} definition '{asset.name}'.", this);
            }
        }
    }
}
