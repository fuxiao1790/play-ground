using System.Collections.Generic;
using PlayGround.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Common
{
    // The only approved reader that crosses to managed ICombatTarget callbacks (design section 8.4).
    // Groups DamageReplayEvent hits by target proxy Entity, rolls crit on the main thread,
    // calls ReceiveHits on each live target, then clears the queue.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class DamageDispatchBridge : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("DamageDispatchBridge");
        private static readonly ProfilerMarker<int> DamageReplayMarker =
            new("DamageDispatchBridge.Damage", "Damage Events");

        private static readonly List<CombatHitData> hitDataScratch = new();
        private static readonly List<DamageReplayEvent> damageEventScratch = new();
        private static readonly DamageReplayEventTargetComparer comparer = new();

        internal NativeQueue<DamageReplayEvent> DamageQueue;
        internal JobHandle ProducerHandle;

        protected override void OnCreate()
        {
            DamageQueue = new NativeQueue<DamageReplayEvent>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            if (DamageQueue.IsCreated)
            {
                DamageQueue.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            ProducerHandle.Complete();
            ProducerHandle = default;

            int damageCount = DamageQueue.Count;
            if (damageCount == 0)
            {
                return;
            }

            var damageEvents = new NativeArray<DamageReplayEvent>(damageCount, Allocator.Temp);
            try
            {
                int offset = 0;
                while (DamageQueue.TryDequeue(out DamageReplayEvent damageEvent) && offset < damageEvents.Length)
                {
                    damageEvents[offset++] = damageEvent;
                }

                DamageQueue.Clear();

                using (Marker.Auto())
                using (DamageReplayMarker.Auto(offset))
                {
                    ReplayDamage(damageEvents, offset, EntityManager);
                }
            }
            finally
            {
                damageEvents.Dispose();
            }
        }

        private static void ReplayDamage(
            NativeArray<DamageReplayEvent> damageEvents,
            int damageCount,
            EntityManager entityManager)
        {
            damageEventScratch.Clear();
            if (damageEventScratch.Capacity < damageCount)
            {
                damageEventScratch.Capacity = damageCount;
            }

            for (int i = 0; i < damageCount; i++)
            {
                DamageReplayEvent damageEvent = damageEvents[i];
                if (damageEvent.TargetProxy != Entity.Null)
                {
                    damageEventScratch.Add(damageEvent);
                }
            }

            damageEventScratch.Sort(comparer);

            int hitCount = damageEventScratch.Count;
            int groupIndex = 0;
            while (groupIndex < hitCount)
            {
                Entity targetProxy = damageEventScratch[groupIndex].TargetProxy;
                ICombatTarget target = ResolveTarget(entityManager, targetProxy);
                hitDataScratch.Clear();

                while (groupIndex < hitCount && damageEventScratch[groupIndex].TargetProxy == targetProxy)
                {
                    DamageReplayEvent damageEvent = damageEventScratch[groupIndex++];
                    DamageSnapshot damage = RollDamage(damageEvent);
                    hitDataScratch.Add(new CombatHitData(
                        damageEvent.Kind, damage,
                        new Vector2(damageEvent.HitPosition.x, damageEvent.HitPosition.y),
                        damageEvent.DirectDamageEnabled, damageEvent.StackEffect,
                        damageEvent.SourceNodeId));
                }

                if (IsTargetUsable(target))
                {
                    target.ReceiveHits(hitDataScratch);
                }
            }

            damageEventScratch.Clear();
            hitDataScratch.Clear();
        }

        private static DamageSnapshot RollDamage(in DamageReplayEvent hit)
        {
            float baseAmount = Mathf.Max(0f, hit.DamageAmount);
            bool isCrit = UnityEngine.Random.value < hit.CritChance;
            float rolledAmount = isCrit ? baseAmount * hit.CritMultiplier : baseAmount;
            return new DamageSnapshot(Mathf.Max(0f, rolledAmount), isCrit);
        }

        private static ICombatTarget ResolveTarget(EntityManager entityManager, Entity targetProxy)
        {
            if (targetProxy == Entity.Null
                || !entityManager.Exists(targetProxy)
                || !entityManager.HasComponent<TargetCompanion>(targetProxy))
            {
                return null;
            }

            TargetCompanion companion = entityManager.GetComponentObject<TargetCompanion>(targetProxy);
            return companion?.Target;
        }

        private static bool IsTargetUsable(ICombatTarget target) =>
            target != null
            && (target is not UnityEngine.Object unityObject || unityObject != null)
            && target.IsCombatTargetActive;

        private sealed class DamageReplayEventTargetComparer : IComparer<DamageReplayEvent>
        {
            public int Compare(DamageReplayEvent x, DamageReplayEvent y)
            {
                int indexCompare = x.TargetProxy.Index.CompareTo(y.TargetProxy.Index);
                return indexCompare != 0
                    ? indexCompare
                    : x.TargetProxy.Version.CompareTo(y.TargetProxy.Version);
            }
        }
    }
}
