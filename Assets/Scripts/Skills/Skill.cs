using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class Skill : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float baseRecoveryTime = 0.2f;

        public float BaseRecoveryTime => baseRecoveryTime;
        public abstract SkillDefinitionTags Tags { get; }
        public abstract SkillDefinition Definition { get; }
    }
}
