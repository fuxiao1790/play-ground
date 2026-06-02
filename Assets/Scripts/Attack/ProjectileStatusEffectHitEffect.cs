using System;
using PlayGround.Common;
using PlayGround.Common.StatusEffects;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class ProjectileStatusEffectHitEffect : ProjectileHitEffect
    {
        [SerializeField] private StatusEffectDef effectDef;
        [SerializeField, Min(1)] private int stacksPerHit = 1;
        [SerializeField] private float dotDamagePerStack;
        [SerializeField] private int triggerAoeTypeId = -1;
        [SerializeField] private float triggerAoeDamage;
        [SerializeField] private float triggerAoeLifetimeSeconds;
        [SerializeField] private float triggerAoeTickIntervalSeconds;
        [SerializeField] private bool triggerAoeAtTargetPosition = true;

        public override void Apply(in ProjectileHitContext hit, Action<ProjectileAoeSpawnRequest> emitAoe)
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

            float contrib = effectDef is StackingTriggerDef td
                ? triggerAoeDamage / td.StackThreshold
                : dotDamagePerStack;

            StatusEffectTriggerResult result = statusEffects.AddEffect(effectDef, stacksPerHit, contrib);

            if (!result.Triggered || triggerAoeTypeId < 0 || emitAoe == null)
            {
                return;
            }

            float damagePerFire = result.TriggerCount > 0
                ? result.TotalTriggerDamage / result.TriggerCount
                : 0f;
            Vector2 spawnPos = triggerAoeAtTargetPosition ? result.OwnerPosition : hit.Position;

            for (int i = 0; i < result.TriggerCount; i++)
            {
                emitAoe.Invoke(new ProjectileAoeSpawnRequest(
                    triggerAoeTypeId,
                    spawnPos,
                    new DamageSnapshot(Mathf.Max(0f, damagePerFire)),
                    triggerAoeLifetimeSeconds,
                    triggerAoeTickIntervalSeconds));
            }
        }
    }
}
