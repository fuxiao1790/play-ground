using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial class CombatApplyFinalizeSystem : SystemBase
    {
        private const int MaxTargetStackEntries = 32;

        private static readonly ProfilerMarker Marker = new("CombatApplyFinalizeSystem");
        private static readonly ProfilerCounterValue<int> EntryEvictionCounter =
            new(ProfilerCategory.Scripts, "CombatApplyFinalizeSystem.StackEntryEvictions", ProfilerMarkerDataUnit.Count);

        internal NativeQueue<CombatHitEvent> HitQueue;
        internal JobHandle ProducerHandle;
        internal int AccrualFrame;

        private int entryEvictions;

        protected override void OnCreate()
        {
            HitQueue = new NativeQueue<CombatHitEvent>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            if (HitQueue.IsCreated)
            {
                HitQueue.Dispose();
            }
        }

        internal NativeQueue<CombatHitEvent>.ParallelWriter AsParallelWriter() =>
            HitQueue.AsParallelWriter();

        protected override void OnUpdate()
        {
            using (Marker.Auto())
            {
                ProducerHandle.Complete();
                ProducerHandle = default;
                AccrualFrame++;

                CombatApplyBridge bridge = World.GetExistingSystemManaged<CombatApplyBridge>();
                bridge?.DisposeFinalizedCombat();

                int hitCount = HitQueue.Count;
                if (hitCount == 0)
                {
                    HitQueue.Clear();
                    return;
                }

                NativeArray<CombatHitEvent> flatHits = HitQueue.ToArray(Allocator.TempJob);
                HitQueue.Clear();

                var hitIndexMap = new NativeParallelMultiHashMap<Entity, int>(flatHits.Length, Allocator.TempJob);
                JobHandle bucketHandle = new BucketHitsJob
                {
                    Hits = flatHits,
                    HitIndexWriter = hitIndexMap.AsParallelWriter()
                }.Schedule(flatHits.Length, 64);
                bucketHandle.Complete();

                int frameCount = UnityEngine.Time.frameCount;
                var (keys, keyCount) = hitIndexMap.GetUniqueKeyArray(Allocator.TempJob);
                NativeArray<CombatTickResult> results = new(keyCount, Allocator.Persistent);
                NativeArray<StatusStackSnapshot> statusSnapshots =
                    new(keyCount * MaxTargetStackEntries, Allocator.Persistent);
                var evictionCounts = new NativeArray<int>(keyCount, Allocator.TempJob);

                JobHandle rollHandle = new FinalizeCombatJob
                {
                    Hits = flatHits,
                    HitIndexMap = hitIndexMap,
                    Keys = keys,
                    Results = results,
                    HealthLookup = GetComponentLookup<TargetHealth>(),
                    StackBuffers = GetBufferLookup<TargetStackEntry>(),
                    StatusSnapshots = statusSnapshots,
                    EvictionCounts = evictionCounts,
                    AccrualFrame = AccrualFrame,
                    FrameCount = (uint)frameCount
                }.Schedule(keyCount, 32);

                rollHandle.Complete();
                int frameEvictions = 0;
                for (int i = 0; i < evictionCounts.Length; i++)
                {
                    frameEvictions += evictionCounts[i];
                }

                if (frameEvictions > 0)
                {
                    entryEvictions += frameEvictions;
                    EntryEvictionCounter.Value = entryEvictions;
                }

                evictionCounts.Dispose();
                keys.Dispose();
                hitIndexMap.Dispose();
                flatHits.Dispose();

                if (bridge != null)
                {
                    bridge.SetFinalizedCombat(results, statusSnapshots);
                }
                else
                {
                    results.Dispose();
                    statusSnapshots.Dispose();
                }
            }
        }

        [BurstCompile]
        private struct BucketHitsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CombatHitEvent> Hits;
            public NativeParallelMultiHashMap<Entity, int>.ParallelWriter HitIndexWriter;

            public void Execute(int index)
            {
                Entity target = Hits[index].TargetProxy;
                if (target != Entity.Null)
                {
                    HitIndexWriter.Add(target, index);
                }
            }
        }

        [BurstCompile]
        private struct FinalizeCombatJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CombatHitEvent> Hits;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, int> HitIndexMap;
            [ReadOnly] public NativeArray<Entity> Keys;
            public NativeArray<CombatTickResult> Results;
            [NativeDisableParallelForRestriction] public ComponentLookup<TargetHealth> HealthLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<TargetStackEntry> StackBuffers;
            [NativeDisableParallelForRestriction] public NativeArray<StatusStackSnapshot> StatusSnapshots;
            [WriteOnly] public NativeArray<int> EvictionCounts;
            public int AccrualFrame;
            public uint FrameCount;

            public void Execute(int index)
            {
                Entity target = Keys[index];
                CombatTickResult result = new()
                {
                    TargetProxy = target
                };
                int hitIndexInTarget = 0;
                int evictionCount = 0;
                float damageTaken = 0f;
                int hitCount = 0;
                int critCount = 0;
                bool hasHealth = HealthLookup.HasComponent(target);
                TargetHealth health = hasHealth
                    ? HealthLookup[target]
                    : default;
                bool hasStackBuffer = StackBuffers.HasBuffer(target);
                DynamicBuffer<TargetStackEntry> stackEntries = hasStackBuffer
                    ? StackBuffers[target]
                    : default;
                bool stackChanged = false;

                foreach (int flatHitIndex in HitIndexMap.GetValuesForKey(target))
                {
                    CombatHitEvent hit = Hits[flatHitIndex];
                    if (hit.StackEffect.Enabled && hasStackBuffer)
                    {
                        AccrueStack(stackEntries, hit.StackEffect, AccrualFrame, ref evictionCount);
                        stackChanged = true;
                    }

                    if (hit.DirectDamageEnabled)
                    {
                        uint seed = math.hash(new uint3((uint)target.Index, FrameCount, (uint)hitIndexInTarget));
                        if (seed == 0)
                        {
                            seed = 1;
                        }

                        var random = new Unity.Mathematics.Random(seed);
                        float baseAmount = math.max(0f, hit.DamageAmount);
                        bool isCrit = random.NextFloat() < hit.CritChance;
                        float rolledAmount = math.max(0f, isCrit ? baseAmount * hit.CritMultiplier : baseAmount);

                        damageTaken += rolledAmount;
                        hitCount++;
                        if (isCrit)
                        {
                            critCount++;
                        }
                    }

                    hitIndexInTarget++;
                }

                // Crit seed uses target entity, frame, and per-target enumeration index.
                // Bucket order is unspecified, but damage is per-hit and order-independent.
                if (stackChanged)
                {
                    int statusStart = index * MaxTargetStackEntries;
                    int statusCount = math.min(stackEntries.Length, MaxTargetStackEntries);
                    for (int i = 0; i < statusCount; i++)
                    {
                        TargetStackEntry entry = stackEntries[i];
                        StatusSnapshots[statusStart + i] = new StatusStackSnapshot(
                            entry.DebuffKey,
                            entry.Count,
                            entry.LifetimeRemaining);
                    }

                    result.StatusStart = statusStart;
                    result.StatusCount = statusCount;
                }

                if (hasHealth)
                {
                    health.Current -= damageTaken;
                    HealthLookup[target] = health;
                    result.Health = health.Current;
                }

                result.DamageTaken = damageTaken;
                result.HitCount = hitCount;
                result.CritCount = critCount;
                Results[index] = result;
                EvictionCounts[index] = evictionCount;
            }

            private static void AccrueStack(
                DynamicBuffer<TargetStackEntry> stackEntries,
                in StackEffectSnapshot stack,
                int accrualFrame,
                ref int evictionCount)
            {
                int entryIndex = FindEntryIndex(stackEntries, stack.DebuffKey);
                if (entryIndex < 0)
                {
                    entryIndex = AddEntry(stackEntries, stack, accrualFrame, ref evictionCount);
                }

                TargetStackEntry entry = stackEntries[entryIndex];
                entry.Threshold = math.max(1, stack.Threshold);
                entry.Count++;
                entry.LastAccruedFrame = accrualFrame;
                entry.SummedDamage += stack.Contribution.Damage;
                entry.SummedProjectileCount += stack.Contribution.ProjectileCount;
                entry.SummedArea += stack.Contribution.AreaSize;
                entry.LifetimeRemaining = math.max(0f, stack.Lifetime);
                entry.Detonation = stack.Detonation;
                stackEntries[entryIndex] = entry;
            }

            private static int AddEntry(
                DynamicBuffer<TargetStackEntry> stackEntries,
                in StackEffectSnapshot stack,
                int accrualFrame,
                ref int evictionCount)
            {
                if (stackEntries.Length >= MaxTargetStackEntries)
                {
                    stackEntries.RemoveAt(LeastLifetimeRemainingIndex(stackEntries));
                    evictionCount++;
                }

                stackEntries.Add(new TargetStackEntry
                {
                    DebuffKey = stack.DebuffKey,
                    Threshold = math.max(1, stack.Threshold),
                    Count = 0,
                    LastAccruedFrame = accrualFrame,
                    SummedDamage = 0f,
                    SummedProjectileCount = 0,
                    SummedArea = 0f,
                    LifetimeRemaining = math.max(0f, stack.Lifetime),
                    Detonation = stack.Detonation
                });

                return stackEntries.Length - 1;
            }

            private static int FindEntryIndex(DynamicBuffer<TargetStackEntry> stackEntries, int debuffKey)
            {
                for (int i = 0; i < stackEntries.Length; i++)
                {
                    if (stackEntries[i].DebuffKey == debuffKey)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static int LeastLifetimeRemainingIndex(DynamicBuffer<TargetStackEntry> stackEntries)
            {
                int index = 0;
                float leastLifetime = stackEntries[0].LifetimeRemaining;
                for (int i = 1; i < stackEntries.Length; i++)
                {
                    float lifetime = stackEntries[i].LifetimeRemaining;
                    if (lifetime < leastLifetime)
                    {
                        leastLifetime = lifetime;
                        index = i;
                    }
                }

                return index;
            }
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CombatBatchedRenderSystem))]
    public partial class CombatApplyBridge : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("CombatApplyBridge");
        private static readonly ProfilerMarker<int> TickReplayMarker =
            new("CombatApplyBridge.TickReplay", "Combat Tick Results");

        private static readonly List<StatusStackSnapshot> statusScratch = new();

        private NativeArray<CombatTickResult> finalizedResults;
        private NativeArray<StatusStackSnapshot> finalizedStatusSnapshots;

        protected override void OnDestroy()
        {
            DisposeFinalizedCombat();
        }

        internal void SetFinalizedCombat(
            NativeArray<CombatTickResult> results,
            NativeArray<StatusStackSnapshot> statusSnapshots)
        {
            DisposeFinalizedCombat();
            finalizedResults = results;
            finalizedStatusSnapshots = statusSnapshots;
        }

        internal void DisposeFinalizedCombat()
        {
            if (finalizedResults.IsCreated)
            {
                finalizedResults.Dispose();
            }

            if (finalizedStatusSnapshots.IsCreated)
            {
                finalizedStatusSnapshots.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!finalizedResults.IsCreated)
            {
                DisposeFinalizedCombat();
                return;
            }

            bool hasStatusSnapshots = HasAnyStatusRange(finalizedResults);
            if (finalizedResults.Length == 0 && !hasStatusSnapshots)
            {
                DisposeFinalizedCombat();
                return;
            }

            try
            {
                using (Marker.Auto())
                using (TickReplayMarker.Auto(finalizedResults.Length))
                {
                    ReplayCombat(
                        finalizedResults,
                        finalizedStatusSnapshots,
                        EntityManager);
                }
            }
            finally
            {
                DisposeFinalizedCombat();
            }
        }

        private static void ReplayCombat(
            NativeArray<CombatTickResult> results,
            NativeArray<StatusStackSnapshot> statusSnapshots,
            EntityManager entityManager)
        {
            for (int resultIndex = 0; resultIndex < results.Length; resultIndex++)
            {
                CombatTickResult result = results[resultIndex];
                if (result.HitCount <= 0 && result.StatusCount <= 0)
                {
                    continue;
                }

                ICombatTarget target = ResolveTarget(entityManager, result.TargetProxy);
                if (!IsTargetUsable(target))
                {
                    continue;
                }

                statusScratch.Clear();
                for (int i = 0; i < result.StatusCount; i++)
                {
                    statusScratch.Add(statusSnapshots[result.StatusStart + i]);
                }

                target.ReceiveCombatTick(in result, statusScratch);
            }

            statusScratch.Clear();
        }

        private static bool HasAnyStatusRange(NativeArray<CombatTickResult> results)
        {
            if (!results.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].StatusCount > 0)
                {
                    return true;
                }
            }

            return false;
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
    }

    public struct CombatTickResult
    {
        public Entity TargetProxy;
        public float Health;
        public float DamageTaken;
        public int HitCount;
        public int CritCount;
        public int StatusStart;
        public int StatusCount;
    }
}
