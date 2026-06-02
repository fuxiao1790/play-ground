using UnityEngine;

namespace PlayGround.Common.StatusEffects
{
    [CreateAssetMenu(menuName = "PlayGround/Status Effects/Stacking DoT", fileName = "StackingDoTDef")]
    public sealed class StackingDoTDef : StatusEffectDef
    {
        public const float TickInterval = 1f;
    }
}
