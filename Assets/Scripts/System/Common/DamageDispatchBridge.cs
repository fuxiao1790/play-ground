using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    public partial class DamageFinalizeSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystemManaged<DamageDispatchBridge>()?.FinalizeDamageQueue();
        }
    }

    // The only approved reader that crosses to managed ICombatTarget callbacks (design section 8.4).
    // Groups DamageReplayEvent hits by target proxy Entity, rolls crit on the main thread,
    // and calls ReceiveHits on each live target after DamageFinalizeSystem freezes the queue.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class DamageDispatchBridge : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("DamageDispatchBridge");
        private static readonly ProfilerMarker<int> DamageReplayMarker =
            new("DamageDispatchBridge.DamageReplay", "Damage Events");

        private static readonly List<CombatHitData> hitDataScratch = new();
        private static readonly List<DamageReplayEvent> damageEventScratch = new();
        private static readonly DamageReplayEventTargetComparer comparer = new();

        internal NativeQueue<DamageReplayEvent> DamageQueue;
        internal JobHandle ProducerHandle;
        internal NativeArray<DamageReplayEvent> FinalizedDamageEvents;
        internal int FinalizedDamageCount;

        protected override void OnCreate()
        {
            DamageQueue = new NativeQueue<DamageReplayEvent>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            DisposeFinalizedDamageEvents();
            if (DamageQueue.IsCreated)
            {
                DamageQueue.Dispose();
            }
        }

        internal void FinalizeDamageQueue()
        {
            ProducerHandle.Complete();
            ProducerHandle = default;
            DisposeFinalizedDamageEvents();

            int damageCount = DamageQueue.Count;
            if (damageCount == 0)
            {
                DamageQueue.Clear();
                return;
            }

            FinalizedDamageEvents = new NativeArray<DamageReplayEvent>(damageCount, Allocator.Persistent);
            int offset = 0;
            while (DamageQueue.TryDequeue(out DamageReplayEvent damageEvent) && offset < FinalizedDamageEvents.Length)
            {
                FinalizedDamageEvents[offset++] = damageEvent;
            }

            FinalizedDamageCount = offset;
            DamageQueue.Clear();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!FinalizedDamageEvents.IsCreated || FinalizedDamageCount == 0)
            {
                DisposeFinalizedDamageEvents();
                return;
            }

            try
            {
                using (Marker.Auto())
                using (DamageReplayMarker.Auto(FinalizedDamageCount))
                {
                    ReplayDamage(FinalizedDamageEvents, FinalizedDamageCount, EntityManager);
                }
            }
            finally
            {
                DisposeFinalizedDamageEvents();
            }
        }

        private void DisposeFinalizedDamageEvents()
        {
            if (FinalizedDamageEvents.IsCreated)
            {
                FinalizedDamageEvents.Dispose();
            }

            FinalizedDamageCount = 0;
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
                        damageEvent.DirectDamageEnabled,
                        sourceNodeId: damageEvent.SourceNodeId));
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
