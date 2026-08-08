using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/UI/Skill UI Catalog", fileName = "SkillUiCatalog")]
    public sealed class SkillUiCatalog : ScriptableObject
    {
        [SerializeField] private List<Skill> skills = new();

        public IReadOnlyList<Skill> Skills => skills;

        private void OnValidate()
        {
            ValidateEntries(skills, "skill");
        }

        private void ValidateEntries<TDefinition>(List<TDefinition> entries, string kind)
            where TDefinition : UnityEngine.Object
        {
            var definitions = new HashSet<TDefinition>();
            for (int i = 0; i < entries.Count; i++)
            {
                TDefinition asset = entries[i];
                if (asset == null)
                    Debug.LogError($"{name}: {kind} entry {i} needs a definition asset.", this);
                else if (!definitions.Add(asset))
                    Debug.LogError($"{name}: duplicate {kind} definition '{asset.name}'.", this);
            }
        }
    }
}
