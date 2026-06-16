using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class AdditiveSupport : ScriptableObject
    {
        public abstract SkillDefinitionTags SupportedSkillTags { get; }
        public abstract void Apply(SkillDefinition def);
    }
}
