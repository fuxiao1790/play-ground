using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class Skill : PersistentScriptableObject
    {
        [Header("UI")]
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private Sprite icon;
        [SerializeField, Min(0.01f)] private float baseRate = 5f;
        [SerializeField, Min(1e-3f), Tooltip("Base energy deposited per hit when this skill is the source, and energy required to activate when this skill is triggered.")]
        private float triggerEnergy = 1f;
        [SerializeField, Min(0)] private int maxSupportCount = 6;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public float BaseRate => baseRate;
        public float TriggerEnergy => triggerEnergy;
        public int MaxSupportCount => maxSupportCount;
        public abstract SkillDefinitionTags Tags { get; }
        public abstract SkillDefinition Definition { get; }
    }
}
