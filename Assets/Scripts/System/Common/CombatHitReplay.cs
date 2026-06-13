using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Common
{
    internal interface ICombatHitReplayAdapter<TTarget>
        where TTarget : class
    {
        DamageSnapshot RollDamage(in CombatHitElement hit);
        void ReplayEffect(
            in CombatHitElement hit,
            in CombatHitEffectElement effect,
            TTarget target,
            in DamageSnapshot damage);
        void ReplayBatch(IReadOnlyList<CombatHitData> data, TTarget target);
    }

    internal static class CombatHitReplay
    {
        private static readonly List<CombatHitData> hitDataScratch = new();

        public static void ReplayAndClear<TTarget, TAdapter>(
            DynamicBuffer<CombatHitElement> hitBuffer,
            DynamicBuffer<CombatHitPayloadElement> payloadBuffer,
            DynamicBuffer<CombatHitEffectElement> effectBuffer,
            IReadOnlyDictionary<int, TTarget> targetsById,
            ref TAdapter adapter)
            where TTarget : class
            where TAdapter : struct, ICombatHitReplayAdapter<TTarget>
        {
            int hitCount = hitBuffer.Length;
            int i = 0;
            while (i < hitCount)
            {
                int groupTargetId = hitBuffer[i].TargetId;
                TryGetLiveTarget(targetsById, groupTargetId, out TTarget target);
                hitDataScratch.Clear();

                while (i < hitCount && hitBuffer[i].TargetId == groupTargetId)
                {
                    CombatHitElement hit = hitBuffer[i++];
                    DamageSnapshot damage = adapter.RollDamage(in hit);
                    CombatHitPayloadElement payload = hit.PayloadIndex >= 0
                        ? payloadBuffer[hit.PayloadIndex] : default;

                    if (hit.EffectIndex >= 0)
                    {
                        CombatHitEffectElement effect = effectBuffer[hit.EffectIndex];
                        adapter.ReplayEffect(in hit, in effect, target, in damage);
                    }

                    hitDataScratch.Add(new CombatHitData(
                        hit.Kind, damage,
                        new Vector2(hit.Position.x, hit.Position.y),
                        hit.DirectDamageEnabled, payload.StackEffect,
                        hit.SourceNodeId));
                }

                adapter.ReplayBatch(hitDataScratch, target);
            }

            hitBuffer.Clear();
            payloadBuffer.Clear();
            effectBuffer.Clear();
        }

        public static bool IsTargetUsable<TTarget>(TTarget target)
            where TTarget : class
        {
            return target != null
                && (target is not UnityEngine.Object unityObject || unityObject != null)
                && target is ICombatTarget combatTarget
                && combatTarget.IsCombatTargetActive;
        }

        private static bool TryGetLiveTarget<TTarget>(
            IReadOnlyDictionary<int, TTarget> targetsById,
            int targetId,
            out TTarget target)
            where TTarget : class
        {
            if (targetsById.TryGetValue(targetId, out target) && IsTargetUsable(target))
            {
                return true;
            }

            target = null;
            return false;
        }
    }
}
