using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class Skill : ScriptableObject
    {
        public abstract SkillDefinition Definition { get; }
    }
}
