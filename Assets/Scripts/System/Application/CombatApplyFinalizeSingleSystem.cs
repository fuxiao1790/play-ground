using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using PlayGround.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Combat.Application
{
    // ECS Lifecycle: singleton combat-hit dispatch queue; created by CombatApplyFinalizeSingleSystem
    // on create, drained every simulation update by the finalize job, disposed on destroy.
    public struct CombatHitDispatchSingleton : IComponentData
    {
        public NativeQueue<CombatHitEvent> HitQueue;
        public JobHandle ProducerHandle;
    }

    // Applies queued combat hit events to ECS target health and stack buffers, then
    // writes compact native results for presentation systems to replay later.
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
    [UpdateAfter(typeof(StatusProcessSystem))]
    public partial class CombatApplyFinalizeSingleSystem : SystemBase
    {
        private const int MaxTargetStackEntries = 32;

        private static readonly ProfilerMarker Marker = new("CombatApplyFinalizeSingleSystem");
        private static readonly ProfilerMarker CompleteProducersMarker =
            new("CombatApplyFinalizeSingleSystem.CompleteProducers");
        private static readonly ProfilerMarker ClearResultsMarker =
            new("CombatApplyFinalizeSingleSystem.ClearResults");
        private static readonly ProfilerCounterValue<int> EntryEvictionCounter =
            new(ProfilerCategory.Scripts, "CombatApplyFinalizeSingleSystem.StackEntryEvictions", ProfilerMarkerDataUnit.Count);

        internal int LastHitEventCount;

        private int entryEvictions;
        private Entity singletonEntity;
        private Entity resultEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(CombatHitDispatchSingleton));
            EntityManager.SetComponentData(singletonEntity, new CombatHitDispatchSingleton
            {
                HitQueue = new NativeQueue<CombatHitEvent>(Allocator.Persistent)
            });

            resultEntity = EntityManager.CreateEntity(typeof(CombatApplyResultSingleton));
            EntityManager.SetComponentData(resultEntity, new CombatApplyResultSingleton
            {
                Results = new NativeList<CombatTickResult>(Allocator.Persistent),
                StatusSnapshots = new NativeList<StatusStackSnapshot>(Allocator.Persistent)
            });
        }

        protected override void OnDestroy()
        {
            DisposeHitDispatch();
            DisposeApplyResults();
        }

        private void DisposeHitDispatch()
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

        private void DisposeApplyResults()
        {
            if (resultEntity == Entity.Null
                || !EntityManager.Exists(resultEntity)
                || !EntityManager.HasComponent<CombatApplyResultSingleton>(resultEntity))
            {
                return;
            }

            CombatApplyResultSingleton results =
                EntityManager.GetComponentData<CombatApplyResultSingleton>(resultEntity);
            results.ProducerHandle.Complete();
            if (results.Results.IsCreated)
            {
                results.Results.Dispose();
            }

            if (results.StatusSnapshots.IsCreated)
            {
                results.StatusSnapshots.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            using (Marker.Auto())
            {
                RefRW<CombatHitDispatchSingleton> hitDispatch =
                    SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
                ref CombatHitDispatchSingleton singleton = ref hitDispatch.ValueRW;
                RefRW<CombatApplyResultSingleton> resultDispatch =
                    SystemAPI.GetSingletonRW<CombatApplyResultSingleton>();
                ref CombatApplyResultSingleton applyResults = ref resultDispatch.ValueRW;

                using (CompleteProducersMarker.Auto())
                {
                    singleton.ProducerHandle.Complete();
                    singleton.ProducerHandle = default;
                    applyResults.ProducerHandle.Complete();
                    applyResults.ProducerHandle = default;
                }

                using (ClearResultsMarker.Auto())
                {
                    applyResults.Clear();
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

                applyResults.EnsureCapacity(
                    math.max(16, hitCount / 4),
                    MaxTargetStackEntries);
                var evictionRef = new NativeReference<int>(Allocator.TempJob);

                Dependency = new FinalizeCombatSingleJob
                {
                    HitQueue = singleton.HitQueue,
                    HealthLookup = GetComponentLookup<TargetHealth>(),
                    StackBuffers = GetBufferLookup<TargetStackEntry>(),
                    Results = applyResults.Results,
                    StatusSnapshots = applyResults.StatusSnapshots,
                    EvictionCount = evictionRef,
                    Now = SystemAPI.Time.ElapsedTime,
                    FrameCount = (uint)UnityEngine.Time.frameCount
                }.Schedule(Dependency);
                applyResults.ProducerHandle = Dependency;

                Dependency.Complete();
                applyResults.ProducerHandle = default;

                int frameEvictions = evictionRef.Value;
                if (frameEvictions > 0)
                {
                    entryEvictions += frameEvictions;
                    EntryEvictionCounter.Value = entryEvictions;
                }

                evictionRef.Dispose();
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
            public double Now;
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
                        AccrueStack(buffer, hit.StackEffect, Now, ref evictionCount);
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
                                (float)math.max(0.0, entry.ExpiryTime - Now)));
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
                double now,
                ref int evictionCount)
            {
                int entryIndex = FindEntryIndex(stackEntries, stack.DebuffKey);
                if (entryIndex < 0)
                {
                    entryIndex = AddEntry(stackEntries, stack, now, ref evictionCount);
                }

                TargetStackEntry entry = stackEntries[entryIndex];
                entry.Threshold = math.max(1, stack.Threshold);
                entry.Count += math.max(1, stack.StacksPerHit);
                entry.SummedDamage += stack.Contribution.Damage;
                entry.SummedProjectileCount += stack.Contribution.ProjectileCount;
                entry.SummedArea += stack.Contribution.AreaSize;
                entry.ExpiryTime = now + math.max(0f, stack.Lifetime);
                entry.Detonation = DetonationFor(in stack);
                stackEntries[entryIndex] = entry;
            }

            private static int AddEntry(
                DynamicBuffer<TargetStackEntry> stackEntries,
                in StackEffectSnapshot stack,
                double now,
                ref int evictionCount)
            {
                if (stackEntries.Length >= MaxTargetStackEntries)
                {
                    stackEntries.RemoveAt(EarliestExpiryIndex(stackEntries));
                    evictionCount++;
                }

                stackEntries.Add(new TargetStackEntry
                {
                    DebuffKey = stack.DebuffKey,
                    Threshold = math.max(1, stack.Threshold),
                    Count = 0,
                    SummedDamage = 0f,
                    SummedProjectileCount = 0,
                    SummedArea = 0f,
                    ExpiryTime = now + math.max(0f, stack.Lifetime),
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

            private static int EarliestExpiryIndex(DynamicBuffer<TargetStackEntry> stackEntries)
            {
                int index = 0;
                double earliest = stackEntries[0].ExpiryTime;
                for (int i = 1; i < stackEntries.Length; i++)
                {
                    double expiry = stackEntries[i].ExpiryTime;
                    if (expiry < earliest)
                    {
                        earliest = expiry;
                        index = i;
                    }
                }

                return index;
            }
        }
    }
}
