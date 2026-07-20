using System;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Skill Set", fileName = "NewSkillSet")]
    public sealed class SkillSet : ScriptableObject
    {
        [SerializeField] private Skill skill;
        [SerializeField] private SkillSupport[] supports = Array.Empty<SkillSupport>();

        public Skill Skill => skill;
        public SkillSupport[] Supports => supports;

        internal SkillSet CreateRuntimeClone()
        {
            var clone = CreateInstance<SkillSet>();
            clone.name = $"{name} (Runtime)";
            clone.hideFlags = HideFlags.DontSave;
            clone.skill = skill;
            clone.supports = supports == null ? Array.Empty<SkillSupport>() : (SkillSupport[])supports.Clone();
            return clone;
        }

        internal void SetSkill(Skill value) => skill = value;

        internal void SetSupport(int index, SkillSupport value)
        {
            if (index < 0) return;
            if (index >= supports.Length)
            {
                var expanded = new SkillSupport[index + 1];
                Array.Copy(supports, expanded, supports.Length);
                supports = expanded;
            }

            supports[index] = value;
        }
    }
}
