using System;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Skill Set", fileName = "NewSkillSet")]
    public sealed class SkillSet : ScriptableObject
    {
        [SerializeField] private Skill skill;
        [SerializeField] private SkillSupport[] supports = Array.Empty<SkillSupport>();
        [SerializeField] private int supportSlotCount = -1;

        public Skill Skill => skill;
        public SkillSupport[] Supports => supports;
        public int MaxSupportCount => skill?.MaxSupportCount ?? 0;
        public int SupportSlotCount => supportSlotCount < 0 ? MaxSupportCount : Mathf.Min(supportSlotCount, MaxSupportCount);

        internal SkillSet CreateRuntimeClone()
        {
            var clone = CreateInstance<SkillSet>();
            clone.name = $"{name} (Runtime)";
            clone.hideFlags = HideFlags.DontSave;
            clone.skill = skill;
            clone.supports = supports == null ? Array.Empty<SkillSupport>() : (SkillSupport[])supports.Clone();
            clone.supportSlotCount = SupportSlotCount;
            return clone;
        }

        internal void SetSkill(Skill value)
        {
            skill = value;
            supportSlotCount = MaxSupportCount;
        }

        internal void SetSupport(int index, SkillSupport value)
        {
            if (index < 0 || index >= SupportSlotCount) return;
            if (index >= supports.Length)
            {
                var expanded = new SkillSupport[index + 1];
                Array.Copy(supports, expanded, supports.Length);
                supports = expanded;
            }

            supports[index] = value;
        }

        internal bool TrySetSupportSlotCount(int value)
        {
            if (value < 0 || value > MaxSupportCount) return false;
            supportSlotCount = value;
            if (supports.Length > supportSlotCount)
            {
                Array.Resize(ref supports, supportSlotCount);
            }

            return true;
        }

        internal bool TryIncreaseSupportSlotCount() =>
            TrySetSupportSlotCount(SupportSlotCount + 1);

        internal bool TryDecreaseSupportSlotCount() =>
            TrySetSupportSlotCount(SupportSlotCount - 1);
    }
}
