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
    public partial class HitApplyFinalizeSystem : SystemBase
    {
        private const int MinimumHitCapacity = 256;
        private const int MaxTargetStackEntries = 32;
        private const float CapacityWarningRatio = 0.9f;

        private static readonly ProfilerMarker Marker = new("HitApplyFinalizeSystem");
        private static readonly ProfilerCounterValue<int> EntryEvictionCounter =
            new(ProfilerCategory.Scripts, "HitApplyFinalizeSystem.StackEntryEvictions", ProfilerMarkerDataUnit.Count);

        internal NativeParallelMultiHashMap<Entity, CombatHitEvent> HitMap;
        internal JobHandle ProducerHandle;
        internal int AccrualFrame;

        private int hitHighWater;
        private int entryEvictions;
        private bool capacityWarningLogged;

        protected override void OnCreate()
        {
            HitMap = new NativeParallelMultiHashMap<Entity, CombatHitEvent>(MinimumHitCapacity, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            if (HitMap.IsCreated)
            {
                HitMap.Dispose();
            }
        }

        internal NativeParallelMultiHashMap<Entity, CombatHitEvent>.ParallelWriter AsParallelWriter() =>
            HitMap.AsParallelWriter();

        protected override void OnUpdate()
        {
            using (Marker.Auto())
            {
                ProducerHandle.Complete();
                ProducerHandle = default;
                AccrualFrame++;

                HitApplyBridge bridge = World.GetExistingSystemManaged<HitApplyBridge>();
                bridge?.DisposeFinalizedHits();

                int hitCount = HitMap.Count();
                if (hitCount == 0)
                {
                    HitMap.Clear();
                    return;
                }

                int frameCount = UnityEngine.Time.frameCount;
                var (keys, keyCount) = HitMap.GetUniqueKeyArray(Allocator.TempJob);
                var counts = new NativeArray<int>(keyCount, Allocator.TempJob);

                JobHandle countHandle = new CountHitsJob
                {
                    HitMap = HitMap,
                    Keys = keys,
                    Counts = counts
                }.Schedule(keyCount, 32);

                countHandle.Complete();

                NativeArray<TargetHitRange> ranges = new(keyCount, Allocator.Persistent);
                int totalHitCount = 0;
                for (int i = 0; i < keyCount; i++)
                {
                    int count = counts[i];
                    ranges[i] = new TargetHitRange
                    {
                        TargetProxy = keys[i],
                        Start = totalHitCount,
                        Count = count
                    };
                    totalHitCount += count;
                }

                NativeList<RolledHit> rolledHits = new(totalHitCount, Allocator.Persistent);
                rolledHits.ResizeUninitialized(totalHitCount);
                NativeArray<StatusStackSnapshot> statusSnapshots =
                    new(keyCount * MaxTargetStackEntries, Allocator.Persistent);
                NativeArray<TargetHitRange> statusRanges = new(keyCount, Allocator.Persistent);
                var evictionCounts = new NativeArray<int>(keyCount, Allocator.TempJob);

                JobHandle rollHandle = new RollHitsJob
                {
                    HitMap = HitMap,
                    Keys = keys,
                    Ranges = ranges,
                    RolledHits = rolledHits.AsArray(),
                    StackBuffers = GetBufferLookup<TargetStackEntry>(),
                    StatusSnapshots = statusSnapshots,
                    StatusRanges = statusRanges,
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
                counts.Dispose();
                HitMap.Clear();
                EnsureCapacityAfterFrame(hitCount);

                if (bridge != null)
                {
                    bridge.SetFinalizedHits(rolledHits, ranges, statusSnapshots, statusRanges);
                }
                else
                {
                    rolledHits.Dispose();
                    ranges.Dispose();
                    statusSnapshots.Dispose();
                    statusRanges.Dispose();
                }
            }
        }

        private void EnsureCapacityAfterFrame(int observedHitCount)
        {
            hitHighWater = math.max(hitHighWater, observedHitCount);
            int capacity = HitMap.Capacity;
            if (capacity > 0 && observedHitCount >= capacity * CapacityWarningRatio && !capacityWarningLogged)
            {
                Debug.LogWarning(
                    $"HitApplyFinalizeSystem hit map reached {observedHitCount}/{capacity} entries; growing next-frame capacity.");
                capacityWarningLogged = true;
            }

            int desiredCapacity = math.max(MinimumHitCapacity, hitHighWater + (hitHighWater >> 1));
            if (desiredCapacity > capacity)
            {
                HitMap.Capacity = desiredCapacity;
            }
        }

        [BurstCompile]
        private struct CountHitsJob : IJobParallelFor
        {
            [ReadOnly] public NativeParallelMultiHashMap<Entity, CombatHitEvent> HitMap;
            [ReadOnly] public NativeArray<Entity> Keys;
            [WriteOnly] public NativeArray<int> Counts;

            public void Execute(int index)
            {
                Entity target = Keys[index];
                int count = 0;
                foreach (CombatHitEvent hit in HitMap.GetValuesForKey(target))
                {
                    if (hit.DirectDamageEnabled)
                    {
                        count++;
                    }
                }

                Counts[index] = count;
            }
        }

        [BurstCompile]
        private struct RollHitsJob : IJobParallelFor
        {
            [ReadOnly] public NativeParallelMultiHashMap<Entity, CombatHitEvent> HitMap;
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeArray<TargetHitRange> Ranges;
            [NativeDisableParallelForRestriction] public NativeArray<RolledHit> RolledHits;
            [NativeDisableParallelForRestriction] public BufferLookup<TargetStackEntry> StackBuffers;
            [NativeDisableParallelForRestriction] public NativeArray<StatusStackSnapshot> StatusSnapshots;
            [NativeDisableParallelForRestriction] public NativeArray<TargetHitRange> StatusRanges;
            [WriteOnly] public NativeArray<int> EvictionCounts;
            public int AccrualFrame;
            public uint FrameCount;

            public void Execute(int index)
            {
                Entity target = Keys[index];
                TargetHitRange range = Ranges[index];
                int hitIndex = 0;
                int rolledHitIndex = 0;
                int evictionCount = 0;
                bool hasStackBuffer = StackBuffers.HasBuffer(target);
                DynamicBuffer<TargetStackEntry> stackEntries = hasStackBuffer
                    ? StackBuffers[target]
                    : default;
                bool stackChanged = false;

                foreach (CombatHitEvent hit in HitMap.GetValuesForKey(target))
                {
                    if (hit.StackEffect.Enabled && hasStackBuffer)
                    {
                        AccrueStack(stackEntries, hit.StackEffect, AccrualFrame, ref evictionCount);
                        stackChanged = true;
                    }

                    if (hit.DirectDamageEnabled)
                    {
                        uint seed = math.hash(new uint3((uint)target.Index, FrameCount, (uint)hitIndex));
                        if (seed == 0)
                        {
                            seed = 1;
                        }

                        var random = new Unity.Mathematics.Random(seed);
                        float baseAmount = math.max(0f, hit.DamageAmount);
                        bool isCrit = random.NextFloat() < hit.CritChance;
                        float rolledAmount = math.max(0f, isCrit ? baseAmount * hit.CritMultiplier : baseAmount);

                        RolledHits[range.Start + rolledHitIndex] = new RolledHit
                        {
                            Kind = hit.Kind,
                            DamageAmount = rolledAmount,
                            IsCrit = isCrit,
                            HitPosition = hit.HitPosition,
                            DirectDamageEnabled = hit.DirectDamageEnabled,
                            SourceNodeId = hit.SourceNodeId,
                            SourceId = hit.SourceId,
                            TypeId = hit.TypeId,
                            StackEffect = hit.StackEffect
                        };

                        rolledHitIndex++;
                    }

                    hitIndex++;
                }

                // Status snapshot is frozen post-accrual and before StatusProcessSystem reduces,
                // detonates, or decays entries later in the same simulation frame.
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

                    StatusRanges[index] = new TargetHitRange
                    {
                        TargetProxy = target,
                        Start = statusStart,
                        Count = statusCount
                    };
                }
                else
                {
                    StatusRanges[index] = new TargetHitRange
                    {
                        TargetProxy = target,
                        Start = 0,
                        Count = 0
                    };
                }

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
    public partial class HitApplyBridge : SystemBase
    {
        private static readonly ProfilerMarker Marker = new("HitApplyBridge");
        private static readonly ProfilerMarker<int> HitReplayMarker =
            new("HitApplyBridge.HitReplay", "Rolled Hits");

        private static readonly List<CombatHitData> hitDataScratch = new();
        private static readonly List<StatusStackSnapshot> statusScratch = new();

        private NativeList<RolledHit> finalizedHits;
        private NativeArray<TargetHitRange> finalizedRanges;
        private NativeArray<StatusStackSnapshot> finalizedStatusSnapshots;
        private NativeArray<TargetHitRange> finalizedStatusRanges;

        protected override void OnDestroy()
        {
            DisposeFinalizedHits();
        }

        internal void SetFinalizedHits(
            NativeList<RolledHit> hits,
            NativeArray<TargetHitRange> ranges,
            NativeArray<StatusStackSnapshot> statusSnapshots,
            NativeArray<TargetHitRange> statusRanges)
        {
            DisposeFinalizedHits();
            finalizedHits = hits;
            finalizedRanges = ranges;
            finalizedStatusSnapshots = statusSnapshots;
            finalizedStatusRanges = statusRanges;
        }

        internal void DisposeFinalizedHits()
        {
            if (finalizedHits.IsCreated)
            {
                finalizedHits.Dispose();
            }

            if (finalizedRanges.IsCreated)
            {
                finalizedRanges.Dispose();
            }

            if (finalizedStatusSnapshots.IsCreated)
            {
                finalizedStatusSnapshots.Dispose();
            }

            if (finalizedStatusRanges.IsCreated)
            {
                finalizedStatusRanges.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!finalizedHits.IsCreated || !finalizedRanges.IsCreated)
            {
                DisposeFinalizedHits();
                return;
            }

            bool hasStatusSnapshots = HasAnyStatusRange(finalizedStatusRanges);
            if (finalizedHits.Length == 0 && !hasStatusSnapshots)
            {
                DisposeFinalizedHits();
                return;
            }

            try
            {
                using (Marker.Auto())
                using (HitReplayMarker.Auto(finalizedHits.Length))
                {
                    ReplayHits(
                        finalizedHits.AsArray(),
                        finalizedRanges,
                        finalizedStatusSnapshots,
                        finalizedStatusRanges,
                        EntityManager);
                }
            }
            finally
            {
                DisposeFinalizedHits();
            }
        }

        private static void ReplayHits(
            NativeArray<RolledHit> hits,
            NativeArray<TargetHitRange> ranges,
            NativeArray<StatusStackSnapshot> statusSnapshots,
            NativeArray<TargetHitRange> statusRanges,
            EntityManager entityManager)
        {
            for (int rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex++)
            {
                TargetHitRange range = ranges[rangeIndex];
                TargetHitRange statusRange = statusRanges.IsCreated && rangeIndex < statusRanges.Length
                    ? statusRanges[rangeIndex]
                    : default;
                if (range.Count <= 0 && statusRange.Count <= 0)
                {
                    continue;
                }

                ICombatTarget target = ResolveTarget(entityManager, range.TargetProxy);
                if (!IsTargetUsable(target))
                {
                    continue;
                }

                if (range.Count > 0)
                {
                    hitDataScratch.Clear();
                    for (int i = 0; i < range.Count; i++)
                    {
                        RolledHit hit = hits[range.Start + i];
                        hitDataScratch.Add(new CombatHitData(
                            hit.Kind,
                            new DamageSnapshot(hit.DamageAmount, hit.IsCrit),
                            new Vector2(hit.HitPosition.x, hit.HitPosition.y),
                            hit.DirectDamageEnabled,
                            hit.StackEffect,
                            hit.SourceNodeId));
                    }

                    target.ReceiveHits(hitDataScratch);
                }

                if (statusRange.Count > 0)
                {
                    statusScratch.Clear();
                    for (int i = 0; i < statusRange.Count; i++)
                    {
                        statusScratch.Add(statusSnapshots[statusRange.Start + i]);
                    }

                    target.ReceiveStatus(statusScratch);
                }
            }

            hitDataScratch.Clear();
            statusScratch.Clear();
        }

        private static bool HasAnyStatusRange(NativeArray<TargetHitRange> ranges)
        {
            if (!ranges.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < ranges.Length; i++)
            {
                if (ranges[i].Count > 0)
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

    public struct RolledHit
    {
        public CombatHitKind Kind;
        public float DamageAmount;
        public bool IsCrit;
        public float2 HitPosition;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
        public int SourceId;
        public int TypeId;
        public StackEffectSnapshot StackEffect;
    }

    public struct TargetHitRange
    {
        public Entity TargetProxy;
        public int Start;
        public int Count;
    }
}
