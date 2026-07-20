using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class Skill : PersistentScriptableObject
    {
        [SerializeField, Min(0.01f)] private float baseRate = 5f;

        public float BaseRate => baseRate;
        public abstract SkillDefinitionTags Tags { get; }
        public abstract SkillDefinition Definition { get; }
    }
}
