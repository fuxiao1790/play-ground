using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class Skill : ScriptableObject
    {
        public abstract SkillDefinitionTags Tags { get; }
        public abstract SkillDefinition Definition { get; }
    }
}
