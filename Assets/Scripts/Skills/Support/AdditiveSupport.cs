using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class AdditiveSupport : SkillSupport
    {
        public abstract SkillDefinitionTags SupportedSkillTags { get; }
        public abstract void Apply(SkillDefinition def);
    }
}
