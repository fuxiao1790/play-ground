using System.Collections.Generic;
using PlayGround.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatHitDispatchSystem : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("CombatHitDispatchSystem");
        private static readonly ProfilerMarker<int> DamageReplayMarker =
            new("CombatHitDispatch.Damage", "Damage Events");

        private static readonly List<CombatHitData> hitDataScratch = new();
        private EntityQuery damageQuery;

        protected override void OnCreate()
        {
            damageQuery = GetEntityQuery(
                ComponentType.ReadWrite<CombatDamageElement>(),
                ComponentType.ReadOnly<CombatDamageTargetSource>());
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            using (Marker.Auto())
            {
                int totalDamageEvents = 0;
                using NativeArray<Entity> damageScopes = damageQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < damageScopes.Length; i++)
                {
                    totalDamageEvents += EntityManager.GetBuffer<CombatDamageElement>(damageScopes[i]).Length;
                }

                using (DamageReplayMarker.Auto(totalDamageEvents))
                {
                    for (int i = 0; i < damageScopes.Length; i++)
                    {
                        Entity scope = damageScopes[i];
                        DynamicBuffer<CombatDamageElement> damage = EntityManager.GetBuffer<CombatDamageElement>(scope);
                        CombatDamageTargetSource targetSource =
                            EntityManager.GetComponentObject<CombatDamageTargetSource>(scope);
                        if (targetSource.TargetsById == null)
                        {
                            damage.Clear();
                            continue;
                        }

                        ReplayDamageAndClear(damage, targetSource.TargetsById);
                    }
                }
            }
        }

        private static void ReplayDamageAndClear(
            DynamicBuffer<CombatDamageElement> damageBuffer,
            IReadOnlyDictionary<int, ICombatTarget> targetsById)
        {
            int hitCount = damageBuffer.Length;
            int i = 0;
            while (i < hitCount)
            {
                int groupTargetId = damageBuffer[i].TargetId;
                TryGetLiveTarget(targetsById, groupTargetId, out ICombatTarget target);
                hitDataScratch.Clear();

                while (i < hitCount && damageBuffer[i].TargetId == groupTargetId)
                {
                    CombatDamageElement damageEvent = damageBuffer[i++];
                    DamageSnapshot damage = RollDamage(in damageEvent);
                    hitDataScratch.Add(new CombatHitData(
                        damageEvent.Kind, damage,
                        new Vector2(damageEvent.Position.x, damageEvent.Position.y),
                        damageEvent.DirectDamageEnabled, damageEvent.StackEffect,
                        damageEvent.SourceNodeId));
                }

                if (IsTargetUsable(target))
                {
                    target.ReceiveHits(hitDataScratch);
                }
            }

            damageBuffer.Clear();
        }

        private static DamageSnapshot RollDamage(in CombatDamageElement hit)
        {
            float baseAmount = Mathf.Max(0f, hit.DamageAmount);
            bool isCrit = UnityEngine.Random.value < hit.CritChance;
            float rolledAmount = isCrit ? baseAmount * hit.CritMultiplier : baseAmount;
            return new DamageSnapshot(Mathf.Max(0f, rolledAmount), isCrit);
        }

        private static bool TryGetLiveTarget(
            IReadOnlyDictionary<int, ICombatTarget> targetsById,
            int targetId,
            out ICombatTarget target)
        {
            if (targetsById.TryGetValue(targetId, out target) && IsTargetUsable(target))
            {
                return true;
            }

            target = null;
            return false;
        }

        private static bool IsTargetUsable(ICombatTarget target) =>
            target != null
            && (target is not UnityEngine.Object unityObject || unityObject != null)
            && target.IsCombatTargetActive;
    }
}
