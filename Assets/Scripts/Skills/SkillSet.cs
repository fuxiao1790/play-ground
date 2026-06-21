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
    }
}
