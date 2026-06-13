using System;
using System.Collections.Generic;
using PlayGround.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    public sealed class CombatTargetSync<T> where T : class, ICombatTarget
    {
        private readonly CombatTargetRegistry<T> registry;
        private readonly Dictionary<int, T> targetsById = new();

        public CombatTargetSync(CombatTargetRegistry<T> registry)
        {
            this.registry = registry;
        }

        public IReadOnlyDictionary<int, T> TargetsById => targetsById;

        public void SyncToBuffer(
            DynamicBuffer<CombatTargetElement> buffer,
            int maxCount = int.MaxValue,
            Func<T, bool> additionalFilter = null)
        {
            buffer.Clear();
            targetsById.Clear();
            IReadOnlyList<T> targets = registry.Targets;
            int count = 0;
            for (int i = 0; i < targets.Count && count < maxCount; i++)
            {
                T target = targets[i];
                if (target == null || !target.IsCombatTargetActive)
                {
                    continue;
                }

                if (additionalFilter != null && !additionalFilter(target))
                {
                    continue;
                }

                float2 pos = new(target.CombatTargetPosition.x, target.CombatTargetPosition.y);
                float2 halfExtents = new(target.CombatTargetHalfExtents.x, target.CombatTargetHalfExtents.y);
                CombatCollisionMath.ComputeWorldBounds(
                    pos,
                    target.CombatTargetRadius,
                    halfExtents,
                    target.CombatTargetRotationRadians,
                    target.CombatTargetShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                buffer.Add(new CombatTargetElement
                {
                    TargetId = target.TargetId,
                    TargetMask = target.CombatTargetMask,
                    Position = pos,
                    ShapeType = target.CombatTargetShapeType,
                    Radius = target.CombatTargetRadius,
                    HalfExtents = halfExtents,
                    RotationRadians = target.CombatTargetRotationRadians,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                });
                targetsById[target.TargetId] = target;
                count++;
            }
        }
    }

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
