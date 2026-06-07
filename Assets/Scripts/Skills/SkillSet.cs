using System;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Skill Set", fileName = "NewSkillSet")]
    public sealed class SkillSet : ScriptableObject
    {
        [SerializeField] private Skill skill;
        [SerializeField] private AdditiveSupport[] supports = Array.Empty<AdditiveSupport>();
        [SerializeField, Min(0.01f)] private float baseRecoveryTime = 0.2f;

        public Skill Skill => skill;
        public AdditiveSupport[] Supports => supports;
        public float BaseRecoveryTime => baseRecoveryTime;
    }
}
