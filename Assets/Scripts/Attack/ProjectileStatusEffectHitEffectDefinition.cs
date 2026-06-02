using PlayGround.Common;
using PlayGround.Common.StatusEffects;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/Hit Effects/Status Effect AOE Trigger", fileName = "ProjectileStatusEffectHitEffect")]
    public sealed class ProjectileStatusEffectHitEffectDefinition : ProjectileHitEffectDefinition
    {
        [SerializeField] private StatusEffectDef effectDef;
        [SerializeField, Min(1)] private int stacksPerHit = 1;
        [SerializeField] private float dotDamagePerStack;

        public override void Apply(
            in ProjectileHitContext hit,
            global::System.Action<ProjectileAoeSpawnRequest> emitAoe)
        {
            if (effectDef == null || hit.Target is not Component ownerComponent)
            {
                return;
            }

            StatusEffects statusEffects = ownerComponent.GetComponent<StatusEffects>();
            if (statusEffects == null)
            {
                return;
            }

            float contrib = effectDef is StackingTriggerDef triggerDef
                ? triggerDef.DamageContributionPerStack
                : dotDamagePerStack;

            statusEffects.AddEffect(effectDef, stacksPerHit, contrib);
        }
    }
}
