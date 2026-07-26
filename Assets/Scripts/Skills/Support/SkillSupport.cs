using PlayGround.Common;

namespace PlayGround.Skills
{
    public abstract class SkillSupport : PersistentScriptableObject
    {
        public virtual SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;
    }
}
