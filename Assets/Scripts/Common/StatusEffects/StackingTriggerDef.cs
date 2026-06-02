using UnityEngine;

namespace PlayGround.Common.StatusEffects
{
    [CreateAssetMenu(menuName = "PlayGround/Status Effects/Stacking Trigger", fileName = "StackingTriggerDef")]
    public sealed class StackingTriggerDef : StatusEffectDef
    {
        [SerializeField, Min(1)] private int stackThreshold = 3;

        public int StackThreshold => stackThreshold;
    }
}
