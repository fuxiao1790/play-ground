using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class Skill : PersistentScriptableObject
    {
        [SerializeField, Min(0.01f)] private float baseRate = 5f;
        [SerializeField, Min(0)] private int maxSupportCount = 6;

        public float BaseRate => baseRate;
        public int MaxSupportCount => maxSupportCount;
        public abstract SkillDefinitionTags Tags { get; }
        public abstract SkillDefinition Definition { get; }
    }
}
