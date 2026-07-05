using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using PlayGround.System.Stats;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: singleton combat-hit dispatch queue; created by CombatApplyFinalizeSingleSystem
    // on create, drained every simulation update by the finalize job, disposed on destroy.
    public struct CombatHitDispatchSingleton : IComponentData
    {
        public NativeQueue<CombatHitEvent> HitQueue;
        public JobHandle ProducerHandle;
    }

    // Applies queued combat hit events to ECS target health and stack buffers, then
    // hands per-target results to CombatApplyBridge for presentation replay.
    //
    // The finalize work runs in a single Burst IJob over one pass of the hit array:
    //   - no bucketing job / multihashmap
    //   - no GetUniqueKeyArray
    //   - status snapshots are packed densely instead of on a fixed per-target stride
    // Multi-threading the workload was not worth it: the upfront main-thread setup to
    // enable the parallel split (flatten, bucket, unique-key extraction, oversized
    // persistent allocation) cost more than the parallel finalize ever saved. See the
    // commented-out CombatApplyFinalizeSystem for the retired multi-threaded variant.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(StatusProcessSystem))]
    public partial class CombatApplyFinalizeSingleSystem : SystemBase
    {
        private const int MaxTargetStackEntries = 32;

        private static readonly ProfilerMarker Marker = new("CombatApplyFinalizeSingleSystem");
        private static readonly ProfilerMarker CompleteProducersMarker =
            new("CombatApplyFinalizeSingleSystem.CompleteProducers");
        private static readonly ProfilerMarker DisposePreviousMarker =
            new("CombatApplyFinalizeSingleSystem.DisposePrevious");
        private static readonly ProfilerMarker PublishResultsMarker =
            new("CombatApplyFinalizeSingleSystem.PublishResults");
        private static readonly ProfilerCounterValue<int> EntryEvictionCounter =
            new(ProfilerCategory.Scripts, "CombatApplyFinalizeSingleSystem.StackEntryEvictions", ProfilerMarkerDataUnit.Count);

        internal int AccrualFrame;
        internal int LastHitEventCount;

        private int entryEvictions;
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(CombatHitDispatchSingleton));
            EntityManager.SetComponentData(singletonEntity, new CombatHitDispatchSingleton
            {
                HitQueue = new NativeQueue<CombatHitEvent>(Allocator.Persistent)
            });
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<CombatHitDispatchSingleton>(singletonEntity))
            {
                return;
            }

            CombatHitDispatchSingleton singleton =
                EntityManager.GetComponentData<CombatHitDispatchSingleton>(singletonEntity);
            singleton.ProducerHandle.Complete();
            if (singleton.HitQueue.IsCreated)
            {
                singleton.HitQueue.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            using (Marker.Auto())
            {
                RefRW<CombatHitDispatchSingleton> hitDispatch =
                    SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
                ref CombatHitDispatchSingleton singleton = ref hitDispatch.ValueRW;

                using (CompleteProducersMarker.Auto())
                {
                    singleton.ProducerHandle.Complete();
                    singleton.ProducerHandle = default;
                }

                AccrualFrame++;

                // Intentional managed lookup: CombatApplyBridge is presentation handoff, not a native container lane.
                CombatApplyBridge bridge = World.GetExistingSystemManaged<CombatApplyBridge>();
                using (DisposePreviousMarker.Auto())
                {
                    bridge?.DisposeFinalizedCombat();
                }

                int hitCount = singleton.HitQueue.Count;
                LastHitEventCount = hitCount;
                if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
                {
                    stats.ValueRW.HitEventsCreated += hitCount;
                }
                if (hitCount == 0)
                {
                    singleton.HitQueue.Clear();
                    return;
                }

                var resultsList = new NativeList<CombatTickResult>(math.max(16, hitCount / 4), Allocator.TempJob);
                var statusList = new NativeList<StatusStackSnapshot>(MaxTargetStackEntries, Allocator.TempJob);
                var evictionRef = new NativeReference<int>(Allocator.TempJob);

                Dependency = new FinalizeCombatSingleJob
                {
                    HitQueue = singleton.HitQueue,
                    HealthLookup = GetComponentLookup<TargetHealth>(),
                    StackBuffers = GetBufferLookup<TargetStackEntry>(),
                    Results = resultsList,
                    StatusSnapshots = statusList,
                    EvictionCount = evictionRef,
                    AccrualFrame = AccrualFrame,
                    FrameCount = (uint)UnityEngine.Time.frameCount
                }.Schedule(Dependency);

                Dependency.Complete();

                int frameEvictions = evictionRef.Value;
                if (frameEvictions > 0)
                {
                    entryEvictions += frameEvictions;
                    EntryEvictionCounter.Value = entryEvictions;
                }

                NativeArray<CombatTickResult> results = resultsList.ToArray(Allocator.Persistent);
                NativeArray<StatusStackSnapshot> statusSnapshots = statusList.ToArray(Allocator.Persistent);

                resultsList.Dispose();
                statusList.Dispose();
                evictionRef.Dispose();

                using (PublishResultsMarker.Auto())
                {
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
        }

        [BurstCompile]
        private struct FinalizeCombatSingleJob : IJob
        {
            public NativeQueue<CombatHitEvent> HitQueue;
            public ComponentLookup<TargetHealth> HealthLookup;
            public BufferLookup<TargetStackEntry> StackBuffers;
            public NativeList<CombatTickResult> Results;
            public NativeList<StatusStackSnapshot> StatusSnapshots;
            public NativeReference<int> EvictionCount;
            public int AccrualFrame;
            public uint FrameCount;

            public void Execute()
            {
                int evictionCount = 0;
                var map = new NativeHashMap<Entity, int>(HitQueue.Count, Allocator.Temp);
                var accums = new NativeList<TargetAccum>(Allocator.Temp);

                while (HitQueue.TryDequeue(out CombatHitEvent hit))
                {
                    Entity target = hit.TargetProxy;
                    if (target == Entity.Null)
                    {
                        continue;
                    }

                    if (!map.TryGetValue(target, out int idx))
                    {
                        idx = accums.Length;
                        map.Add(target, idx);
                        accums.Add(new TargetAccum
                        {
                            Target = target,
                            HasStackBuffer = StackBuffers.HasBuffer(target) ? (byte)1 : (byte)0
                        });
                    }

                    TargetAccum acc = accums[idx];

                    if (hit.StackEffect.Enabled && acc.HasStackBuffer == 1)
                    {
                        DynamicBuffer<TargetStackEntry> buffer = StackBuffers[target];
                        AccrueStack(buffer, hit.StackEffect, AccrualFrame, ref evictionCount);
                        acc.StackChanged = 1;
                    }

                    if (hit.DirectDamageEnabled)
                    {
                        // Crit seed uses target entity, frame, and per-target hit index.
                        // Hit order is unspecified, but damage is per-hit and order-independent.
                        uint seed = math.hash(new uint3((uint)target.Index, FrameCount, (uint)acc.HitIndex));
                        if (seed == 0)
                        {
                            seed = 1;
                        }

                        var random = new Unity.Mathematics.Random(seed);
                        float baseAmount = math.max(0f, hit.DamageAmount);
                        bool isCrit = random.NextFloat() < hit.CritChance;
                        float rolledAmount = math.max(0f, isCrit ? baseAmount * hit.CritMultiplier : baseAmount);

                        acc.DamageTaken += rolledAmount;
                        acc.HitCount++;
                        if (isCrit)
                        {
                            acc.CritCount++;
                        }
                    }

                    acc.HitIndex++;
                    accums[idx] = acc;
                }

                for (int i = 0; i < accums.Length; i++)
                {
                    TargetAccum acc = accums[i];
                    CombatTickResult result = new()
                    {
                        TargetProxy = acc.Target,
                        DamageTaken = acc.DamageTaken,
                        HitCount = acc.HitCount,
                        CritCount = acc.CritCount
                    };

                    if (acc.StackChanged == 1 && acc.HasStackBuffer == 1)
                    {
                        DynamicBuffer<TargetStackEntry> buffer = StackBuffers[acc.Target];
                        int statusCount = math.min(buffer.Length, MaxTargetStackEntries);
                        int statusStart = StatusSnapshots.Length;
                        for (int j = 0; j < statusCount; j++)
                        {
                            TargetStackEntry entry = buffer[j];
                            StatusSnapshots.Add(new StatusStackSnapshot(
                                entry.DebuffKey,
                                entry.Count,
                                entry.LifetimeRemaining));
                        }

                        result.StatusStart = statusStart;
                        result.StatusCount = statusCount;
                    }

                    if (HealthLookup.HasComponent(acc.Target))
                    {
                        TargetHealth health = HealthLookup[acc.Target];
                        health.Current -= acc.DamageTaken;
                        HealthLookup[acc.Target] = health;
                        result.Health = health.Current;
                    }

                    Results.Add(result);
                }

                EvictionCount.Value = evictionCount;
                accums.Dispose();
                map.Dispose();
            }

            private struct TargetAccum
            {
                public Entity Target;
                public float DamageTaken;
                public int HitCount;
                public int CritCount;
                public int HitIndex;
                public byte StackChanged;
                public byte HasStackBuffer;
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
                entry.Count += math.max(1, stack.StacksPerHit);
                entry.LastAccruedFrame = accrualFrame;
                entry.SummedDamage += stack.Contribution.Damage;
                entry.SummedProjectileCount += stack.Contribution.ProjectileCount;
                entry.SummedArea += stack.Contribution.AreaSize;
                entry.LifetimeRemaining = math.max(0f, stack.Lifetime);
                entry.Detonation = DetonationFor(in stack);
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
                    Detonation = DetonationFor(in stack)
                });

                return stackEntries.Length - 1;
            }

            private static DetonationSnapshot DetonationFor(in StackEffectSnapshot stack) =>
                new()
                {
                    Kind = stack.DetonationKind,
                    Faction = stack.Faction,
                    TemplateKey = stack.DetonationKey
                };

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
