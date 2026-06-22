using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Collections;
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
        private const float CapacityWarningRatio = 0.9f;

        private static readonly ProfilerMarker Marker = new("HitApplyFinalizeSystem");

        internal NativeParallelMultiHashMap<Entity, CombatHitEvent> HitMap;
        internal JobHandle ProducerHandle;

        private int hitHighWater;
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

                HitApplyBridge bridge = World.GetExistingSystemManaged<HitApplyBridge>();
                bridge?.DisposeFinalizedHits();

                int hitCount = HitMap.Count();
                if (hitCount == 0)
                {
                    HitMap.Clear();
                    return;
                }

                int frameCount = Time.frameCount;
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

                JobHandle rollHandle = new RollHitsJob
                {
                    HitMap = HitMap,
                    Keys = keys,
                    Ranges = ranges,
                    RolledHits = rolledHits.AsArray(),
                    FrameCount = (uint)frameCount
                }.Schedule(keyCount, 32);

                rollHandle.Complete();
                keys.Dispose();
                counts.Dispose();
                HitMap.Clear();
                EnsureCapacityAfterFrame(hitCount);

                if (bridge != null)
                {
                    bridge.SetFinalizedHits(rolledHits, ranges);
                }
                else
                {
                    rolledHits.Dispose();
                    ranges.Dispose();
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
                foreach (CombatHitEvent ignored in HitMap.GetValuesForKey(target))
                {
                    count++;
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
            public uint FrameCount;

            public void Execute(int index)
            {
                Entity target = Keys[index];
                TargetHitRange range = Ranges[index];
                int hitIndex = 0;
                foreach (CombatHitEvent hit in HitMap.GetValuesForKey(target))
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

                    RolledHits[range.Start + hitIndex] = new RolledHit
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

                    hitIndex++;
                }
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

        private NativeList<RolledHit> finalizedHits;
        private NativeArray<TargetHitRange> finalizedRanges;

        protected override void OnDestroy()
        {
            DisposeFinalizedHits();
        }

        internal void SetFinalizedHits(NativeList<RolledHit> hits, NativeArray<TargetHitRange> ranges)
        {
            DisposeFinalizedHits();
            finalizedHits = hits;
            finalizedRanges = ranges;
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
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            if (!finalizedHits.IsCreated || !finalizedRanges.IsCreated || finalizedHits.Length == 0)
            {
                DisposeFinalizedHits();
                return;
            }

            try
            {
                using (Marker.Auto())
                using (HitReplayMarker.Auto(finalizedHits.Length))
                {
                    ReplayHits(finalizedHits.AsArray(), finalizedRanges, EntityManager);
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
            EntityManager entityManager)
        {
            for (int rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex++)
            {
                TargetHitRange range = ranges[rangeIndex];
                ICombatTarget target = ResolveTarget(entityManager, range.TargetProxy);
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

                if (IsTargetUsable(target))
                {
                    target.ReceiveHits(hitDataScratch);
                }
            }

            hitDataScratch.Clear();
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
