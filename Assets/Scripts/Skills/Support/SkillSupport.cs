using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class SkillSupport : ScriptableObject
    {
        public virtual float RecoverySpeedMultiplier => 1f;
    }
}
