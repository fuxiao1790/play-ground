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
                var directHitCounts = new NativeArray<int>(keyCount, Allocator.TempJob);

                JobHandle countHandle = new CountHitsJob
                {
                    Hits = flatHits,
                    HitIndexMap = hitIndexMap,
                    Keys = keys,
                    DirectHitCounts = directHitCounts
                }.Schedule(keyCount, 32);

                countHandle.Complete();

                NativeArray<TargetResultRange> ranges = new(keyCount, Allocator.Persistent);
                int totalDirectHitCount = 0;
                for (int i = 0; i < keyCount; i++)
                {
                    int directHitCount = directHitCounts[i];
                    ranges[i] = new TargetResultRange
                    {
                        TargetProxy = keys[i],
                        HitStart = totalDirectHitCount,
                        HitCount = directHitCount,
                        StatusStart = 0,
                        StatusCount = 0
                    };
                    totalDirectHitCount += directHitCount;
                }

                NativeList<RolledHit> rolledHits = new(totalDirectHitCount, Allocator.Persistent);
                rolledHits.ResizeUninitialized(totalDirectHitCount);
                NativeArray<StatusStackSnapshot> statusSnapshots =
                    new(keyCount * MaxTargetStackEntries, Allocator.Persistent);
                var evictionCounts = new NativeArray<int>(keyCount, Allocator.TempJob);

                JobHandle rollHandle = new FinalizeCombatJob
                {
                    Hits = flatHits,
                    HitIndexMap = hitIndexMap,
                    Keys = keys,
                    Ranges = ranges,
                    RolledHits = rolledHits.AsArray(),
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
                directHitCounts.Dispose();
                keys.Dispose();
                hitIndexMap.Dispose();
                flatHits.Dispose();

                if (bridge != null)
                {
                    bridge.SetFinalizedCombat(rolledHits, ranges, statusSnapshots);
                }
                else
                {
                    rolledHits.Dispose();
                    ranges.Dispose();
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
        private struct CountHitsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CombatHitEvent> Hits;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, int> HitIndexMap;
            [ReadOnly] public NativeArray<Entity> Keys;
            [WriteOnly] public NativeArray<int> DirectHitCounts;

            public void Execute(int index)
            {
                Entity target = Keys[index];
                int count = 0;
                foreach (int hitIndex in HitIndexMap.GetValuesForKey(target))
                {
                    if (Hits[hitIndex].DirectDamageEnabled)
                    {
                        count++;
                    }
                }

                DirectHitCounts[index] = count;
            }
        }

        [BurstCompile]
        private struct FinalizeCombatJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CombatHitEvent> Hits;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, int> HitIndexMap;
            [ReadOnly] public NativeArray<Entity> Keys;
            public NativeArray<TargetResultRange> Ranges;
            [NativeDisableParallelForRestriction] public NativeArray<RolledHit> RolledHits;
            [NativeDisableParallelForRestriction] public BufferLookup<TargetStackEntry> StackBuffers;
            [NativeDisableParallelForRestriction] public NativeArray<StatusStackSnapshot> StatusSnapshots;
            [WriteOnly] public NativeArray<int> EvictionCounts;
            public int AccrualFrame;
            public uint FrameCount;

            public void Execute(int index)
            {
                Entity target = Keys[index];
                TargetResultRange range = Ranges[index];
                int hitIndexInTarget = 0;
                int rolledHitIndex = 0;
                int evictionCount = 0;
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

                        RolledHits[range.HitStart + rolledHitIndex] = new RolledHit
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

                    range.StatusStart = statusStart;
                    range.StatusCount = statusCount;
                }

                Ranges[index] = range;
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
        private static readonly ProfilerMarker<int> HitReplayMarker =
            new("CombatApplyBridge.HitReplay", "Rolled Hits");

        private static readonly List<CombatHitData> hitDataScratch = new();
        private static readonly List<StatusStackSnapshot> statusScratch = new();

        private NativeList<RolledHit> finalizedHits;
        private NativeArray<TargetResultRange> finalizedRanges;
        private NativeArray<StatusStackSnapshot> finalizedStatusSnapshots;

        protected override void OnDestroy()
        {
            DisposeFinalizedCombat();
        }

        internal void SetFinalizedCombat(
            NativeList<RolledHit> hits,
            NativeArray<TargetResultRange> ranges,
            NativeArray<StatusStackSnapshot> statusSnapshots)
        {
            DisposeFinalizedCombat();
            finalizedHits = hits;
            finalizedRanges = ranges;
            finalizedStatusSnapshots = statusSnapshots;
        }

        internal void DisposeFinalizedCombat()
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
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!finalizedHits.IsCreated || !finalizedRanges.IsCreated)
            {
                DisposeFinalizedCombat();
                return;
            }

            bool hasStatusSnapshots = HasAnyStatusRange(finalizedRanges);
            if (finalizedHits.Length == 0 && !hasStatusSnapshots)
            {
                DisposeFinalizedCombat();
                return;
            }

            try
            {
                using (Marker.Auto())
                using (HitReplayMarker.Auto(finalizedHits.Length))
                {
                    ReplayCombat(
                        finalizedHits.AsArray(),
                        finalizedRanges,
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
            NativeArray<RolledHit> hits,
            NativeArray<TargetResultRange> ranges,
            NativeArray<StatusStackSnapshot> statusSnapshots,
            EntityManager entityManager)
        {
            for (int rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex++)
            {
                TargetResultRange range = ranges[rangeIndex];
                if (range.HitCount <= 0 && range.StatusCount <= 0)
                {
                    continue;
                }

                ICombatTarget target = ResolveTarget(entityManager, range.TargetProxy);
                if (!IsTargetUsable(target))
                {
                    continue;
                }

                hitDataScratch.Clear();
                for (int i = 0; i < range.HitCount; i++)
                {
                    RolledHit hit = hits[range.HitStart + i];
                    hitDataScratch.Add(new CombatHitData(
                        hit.Kind,
                        new DamageSnapshot(hit.DamageAmount, hit.IsCrit),
                        new Vector2(hit.HitPosition.x, hit.HitPosition.y),
                        hit.DirectDamageEnabled,
                        hit.StackEffect,
                        hit.SourceNodeId));
                }

                statusScratch.Clear();
                for (int i = 0; i < range.StatusCount; i++)
                {
                    statusScratch.Add(statusSnapshots[range.StatusStart + i]);
                }

                target.ReceiveCombat(hitDataScratch, statusScratch);
            }

            hitDataScratch.Clear();
            statusScratch.Clear();
        }

        private static bool HasAnyStatusRange(NativeArray<TargetResultRange> ranges)
        {
            if (!ranges.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < ranges.Length; i++)
            {
                if (ranges[i].StatusCount > 0)
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

    public struct TargetResultRange
    {
        public Entity TargetProxy;
        public int HitStart;
        public int HitCount;
        public int StatusStart;
        public int StatusCount;
    }
}
