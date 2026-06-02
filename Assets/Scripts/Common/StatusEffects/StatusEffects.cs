using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Common.StatusEffects
{
    public sealed class StatusEffects : MonoBehaviour
    {
        private Action<DamageSnapshot> takeDamage;
        private Func<bool> isActive;

        private readonly Dictionary<StatusEffectDef, ActiveStatusEffect> activeEffects = new();
        private readonly List<StatusEffectDef> tickKeys = new();

        public void Initialize(Action<DamageSnapshot> onApplyDamage, Func<bool> activeCheck)
        {
            takeDamage = onApplyDamage;
            isActive = activeCheck;
        }

        public StatusEffectTriggerResult AddEffect(StatusEffectDef def, int stacks, float damageContributionPerStack)
        {
            if (def == null || stacks <= 0 || !(isActive?.Invoke() ?? true))
            {
                return default;
            }

            if (!activeEffects.TryGetValue(def, out ActiveStatusEffect effect))
            {
                effect = new ActiveStatusEffect
                {
                    Stacks = 0,
                    AccumulatedDamage = 0f,
                    TickCooldownRemaining = def is StackingDoTDef ? StackingDoTDef.TickInterval : 0f
                };
            }

            effect.Stacks += stacks;
            effect.AccumulatedDamage += damageContributionPerStack * stacks;

            int triggerCount = 0;
            float totalTriggerDamage = 0f;

            if (def is StackingTriggerDef triggerDef)
            {
                while (effect.Stacks >= triggerDef.StackThreshold)
                {
                    float triggerDamage = effect.AccumulatedDamage * triggerDef.StackThreshold / effect.Stacks;
                    effect.AccumulatedDamage *= (effect.Stacks - triggerDef.StackThreshold) / (float)effect.Stacks;
                    effect.Stacks -= triggerDef.StackThreshold;
                    triggerCount++;
                    totalTriggerDamage += triggerDamage;
                }
            }

            if (effect.Stacks > 0)
            {
                activeEffects[def] = effect;
            }
            else
            {
                activeEffects.Remove(def);
            }

            return triggerCount > 0
                ? new StatusEffectTriggerResult(triggerCount, totalTriggerDamage, transform.position)
                : default;
        }

        public void Tick(float deltaTime)
        {
            if (activeEffects.Count == 0)
            {
                return;
            }

            tickKeys.Clear();
            foreach (StatusEffectDef def in activeEffects.Keys)
            {
                tickKeys.Add(def);
            }

            for (int i = 0; i < tickKeys.Count; i++)
            {
                StatusEffectDef def = tickKeys[i];
                if (def is not StackingDoTDef || !activeEffects.TryGetValue(def, out ActiveStatusEffect effect))
                {
                    continue;
                }

                effect.TickCooldownRemaining -= deltaTime;
                if (effect.TickCooldownRemaining <= 0f)
                {
                    effect.TickCooldownRemaining += StackingDoTDef.TickInterval;
                    if (effect.TickCooldownRemaining < 0f)
                    {
                        effect.TickCooldownRemaining = 0f;
                    }

                    takeDamage?.Invoke(new DamageSnapshot(effect.AccumulatedDamage));
                }

                if (effect.Stacks > 0)
                {
                    activeEffects[def] = effect;
                }
                else
                {
                    activeEffects.Remove(def);
                }
            }
        }

        public int GetStackCount(StatusEffectDef def)
        {
            return def != null && activeEffects.TryGetValue(def, out ActiveStatusEffect e) ? e.Stacks : 0;
        }

        public float GetAccumulatedDamage(StatusEffectDef def)
        {
            return def != null && activeEffects.TryGetValue(def, out ActiveStatusEffect e) ? e.AccumulatedDamage : 0f;
        }

        public void ClearEffect(StatusEffectDef def)
        {
            if (def != null)
            {
                activeEffects.Remove(def);
            }
        }

        public void ClearAll()
        {
            activeEffects.Clear();
        }
    }
}
