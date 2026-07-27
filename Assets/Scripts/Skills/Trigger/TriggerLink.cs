using System;
using PlayGround.Common;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    // Runtime-only: parsed from LoadoutSlot list during compilation; not serialized
    public sealed class TriggerChain
    {
        public int causeIndex;
        public TriggerLink link;
        public int effectIndex;
    }

    [Serializable]
    public abstract class LoadoutSlot { }

    [Serializable]
    public sealed class SkillSetSlot : LoadoutSlot
    {
        public SkillSet skillSet;
    }

    [Serializable]
    public sealed class TriggerLinkSlot : LoadoutSlot
    {
        public TriggerLink link;
    }

    public abstract class TriggerLink : PersistentScriptableObject
    {
        [Header("UI")]
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private Sprite icon;

        // One multiplier for this link: interval-child energy, the initial
        // active skill chain cost, and this link's triggered skill cost.
        [FormerlySerializedAs("manaToEnergyCostMultiplier")]
        [FormerlySerializedAs("triggerLinkManaCostMultiplier")]
        [FormerlySerializedAs("manaToEnergyRatio")]
        [Min(0f)] public float manaCostMultiplier = 1f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public abstract SkillDefinitionTags SourceSkillTags { get; }
        public abstract SkillDefinitionTags TargetSkillTags { get; }
    }
}
