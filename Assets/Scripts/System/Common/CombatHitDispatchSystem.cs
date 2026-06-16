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
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = GetEntityQuery(ComponentType.ReadWrite<CombatDamageElement>());
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            using (Marker.Auto())
            {
                Entity scope = scopeQuery.GetSingletonEntity();
                DynamicBuffer<CombatDamageElement> damage = EntityManager.GetBuffer<CombatDamageElement>(scope);
                using (DamageReplayMarker.Auto(damage.Length))
                {
                    ReplayDamageAndClear(damage);
                }
            }
        }

        // Groups by (TargetId, Faction) — TargetId alone is not globally unique
        // across factions, so the Faction tag disambiguates which faction's
        // target dictionary to resolve the live target from.
        private static void ReplayDamageAndClear(DynamicBuffer<CombatDamageElement> damageBuffer)
        {
            int hitCount = damageBuffer.Length;
            int i = 0;
            while (i < hitCount)
            {
                int groupTargetId = damageBuffer[i].TargetId;
                CombatFaction groupFaction = damageBuffer[i].Faction;
                ICombatTarget target = null;
                if (CombatRoot.TryGetByFaction(groupFaction, out CombatRoot root) && root.TargetsById != null)
                {
                    TryGetLiveTarget(root.TargetsById, groupTargetId, out target);
                }
                hitDataScratch.Clear();

                while (i < hitCount
                    && damageBuffer[i].TargetId == groupTargetId
                    && damageBuffer[i].Faction == groupFaction)
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
