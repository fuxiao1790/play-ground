using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class SkillSupport : PersistentScriptableObject
    {
        [Header("UI")]
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private Sprite icon;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public virtual SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;
    }
}
