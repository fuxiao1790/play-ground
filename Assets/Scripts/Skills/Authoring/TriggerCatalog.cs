using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/UI/Skill Trigger Catalog", fileName = "SkillUiTriggerCatalog")]
    public sealed class SkillUiTriggerCatalog : ScriptableObject
    {
        [SerializeField] private List<TriggerLink> triggers = new();

        public IReadOnlyList<TriggerLink> Triggers => triggers;

        private void OnValidate()
        {
            ValidateEntries(triggers, "trigger");
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
