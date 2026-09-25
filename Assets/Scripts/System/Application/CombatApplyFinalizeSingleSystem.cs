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

    // Applies queued combat hit events to ECS target health and hit-energy buffers, then
    // writes compact native results for presentation systems to replay later.
    //
    // The finalize work runs in a single Burst IJob over one pass of the hit array:
    //   - no bucketing job / multihashmap
    //   - no GetUniqueKeyArray
    //   - hit-energy progress is packed densely instead of on a fixed per-target stride
    // Multi-threading the workload was not worth it: the upfront main-thread setup to
    // enable the parallel split (flatten, bucket, unique-key extraction, oversized
    // persistent allocation) cost more than the parallel finalize ever saved. See the
    // commented-out CombatApplyFinalizeSystem for the retired multi-threaded variant.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileDiscreteCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(HitEnergyActivationSystem))]
    public partial class CombatApplyFinalizeSingleSystem : SystemBase
    {
        private const int MaxTargetHitEnergyEntries = 32;

        private static readonly ProfilerMarker Marker = new("CombatApplyFinalizeSingleSystem");
        private static readonly ProfilerMarker CompleteProducersMarker =
            new("CombatApplyFinalizeSingleSystem.CompleteProducers");
        private static readonly ProfilerMarker ClearResultsMarker =
            new("CombatApplyFinalizeSingleSystem.ClearResults");

        internal int LastHitEventCount;

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
                HitEnergyProgress = new NativeList<HitEnergyProgress>(Allocator.Persistent),
                DropCount = new NativeReference<int>(Allocator.Persistent)
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

            if (results.HitEnergyProgress.IsCreated)
            {
                results.HitEnergyProgress.Dispose();
            }

            if (results.DropCount.IsCreated)
            {
                results.DropCount.Dispose();
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
                    MaxTargetHitEnergyEntries);

                Dependency = new FinalizeCombatSingleJob
                {
                    HitQueue = singleton.HitQueue,
                    PayloadLookup = GetComponentLookup<CombatHitPayload>(isReadOnly: true),
                    HealthLookup = GetComponentLookup<Health>(),
                    HitEnergyBuffers = GetBufferLookup<TargetHitEnergy>(),
                    Results = applyResults.Results,
                    HitEnergyProgress = applyResults.HitEnergyProgress,
                    DropCount = applyResults.DropCount,
                    Now = SystemAPI.Time.ElapsedTime,
                    FrameCount = (uint)UnityEngine.Time.frameCount,
                    DeltaTime = SystemAPI.Time.DeltaTime
                }.Schedule(Dependency);
                applyResults.ProducerHandle = Dependency;
            }
        }

        [BurstCompile]
        private struct FinalizeCombatSingleJob : IJob
        {
            public NativeQueue<CombatHitEvent> HitQueue;
            [ReadOnly] public ComponentLookup<CombatHitPayload> PayloadLookup;
            public ComponentLookup<Health> HealthLookup;
            public BufferLookup<TargetHitEnergy> HitEnergyBuffers;
            public NativeList<CombatTickResult> Results;
            public NativeList<HitEnergyProgress> HitEnergyProgress;
            public NativeReference<int> DropCount;
            public double Now;
            public uint FrameCount;
            public float DeltaTime;

            public void Execute()
            {
                int dropCount = 0;
                var map = new NativeHashMap<Entity, int>(HitQueue.Count, Allocator.Temp);
                var accums = new NativeList<TargetAccum>(Allocator.Temp);

                while (HitQueue.TryDequeue(out CombatHitEvent hit))
                {
                    Entity target = hit.Target;
                    if (target == Entity.Null)
                    {
                        continue;
                    }

                    // Producers only enqueue source archetypes carrying this component.
                    // Spawn-apply dependency completion prevents same-frame reuse until
                    // this read-only lookup job finishes.
                    CombatHitPayload payload = PayloadLookup[hit.Source];

                    if (!map.TryGetValue(target, out int idx))
                    {
                        idx = accums.Length;
                        map.Add(target, idx);
                        accums.Add(new TargetAccum
                        {
                            Target = target,
                            HasHitEnergyBuffer = HitEnergyBuffers.HasBuffer(target) ? (byte)1 : (byte)0
                        });
                    }

                    TargetAccum acc = accums[idx];

                    if (payload.HitEnergy.Enabled && acc.HasHitEnergyBuffer == 1)
                    {
                        DynamicBuffer<TargetHitEnergy> buffer = HitEnergyBuffers[target];
                        if (DepositHitEnergy(buffer, payload.HitEnergy, Now, ref dropCount))
                        {
                            acc.HitEnergyChanged = 1;
                        }
                    }

                    if (payload.DirectDamageEnabled)
                    {
                        // Crit seed uses target entity, frame, and per-target hit index.
                        // Hit order is unspecified, but damage is per-hit and order-independent.
                        uint seed = math.hash(new uint3((uint)target.Index, FrameCount, (uint)acc.HitIndex));
                        if (seed == 0)
                        {
                            seed = 1;
                        }

                        var random = new Unity.Mathematics.Random(seed);
                        float scale = hit.DamageScale <= 0f ? 1f : hit.DamageScale;
                        float baseAmount = math.max(0f, payload.DamageAmount * scale);
                        bool isCrit = random.NextFloat() < payload.CritChance;
                        float rolledAmount = math.max(0f, isCrit ? baseAmount * payload.CritMultiplier : baseAmount);

                        acc.DamageTaken += rolledAmount;
                        if (isCrit)
                        {
                            acc.CritCount++;
                        }
                    }

                    acc.HitCount++;
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
                        CritCount = acc.CritCount,
                        TickDeltaSeconds = DeltaTime
                    };

                    if (acc.HitEnergyChanged == 1 && acc.HasHitEnergyBuffer == 1)
                    {
                        DynamicBuffer<TargetHitEnergy> buffer = HitEnergyBuffers[acc.Target];
                        int progressCount = math.min(buffer.Length, MaxTargetHitEnergyEntries);
                        int progressStart = HitEnergyProgress.Length;
                        for (int j = 0; j < progressCount; j++)
                        {
                            TargetHitEnergy entry = buffer[j];
                            HitEnergyProgress.Add(new HitEnergyProgress(
                                entry.AccumulatorId,
                                entry.StoredEnergy,
                                entry.EnergyRequired,
                                (float)math.max(0.0, entry.ExpiresAt - Now)));
                        }

                        result.HitEnergyStart = progressStart;
                        result.HitEnergyCount = progressCount;
                    }

                    if (HealthLookup.HasComponent(acc.Target))
                    {
                        Health health = HealthLookup[acc.Target];
                        health.Current -= acc.DamageTaken;
                        HealthLookup[acc.Target] = health;
                        result.Health = health.Current;
                    }

                    Results.Add(result);
                }

                DropCount.Value = dropCount;
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
                public byte HitEnergyChanged;
                public byte HasHitEnergyBuffer;
            }

            private static bool DepositHitEnergy(
                DynamicBuffer<TargetHitEnergy> entries,
                in HitEnergyPayload payload,
                double now,
                ref int dropCount)
            {
                int entryIndex = FindEntryIndex(entries, payload.AccumulatorId);
                if (entryIndex < 0)
                {
                    entryIndex = AddEntry(entries, payload, now, ref dropCount);
                    if (entryIndex < 0)
                    {
                        return false;
                    }
                }

                TargetHitEnergy entry = entries[entryIndex];
                entry.StoredEnergy += payload.EnergyPerHit;
                entry.EnergyRequired = payload.EnergyRequired;
                entry.ExpiresAt = now + payload.RetentionSeconds;
                entry.HitEnergySpawn = payload.Spawn;
                entries[entryIndex] = entry;
                return true;
            }

            private static int AddEntry(
                DynamicBuffer<TargetHitEnergy> entries,
                in HitEnergyPayload payload,
                double now,
                ref int dropCount)
            {
                if (entries.Length >= MaxTargetHitEnergyEntries)
                {
                    dropCount++;
                    return -1;
                }

                entries.Add(new TargetHitEnergy
                {
                    AccumulatorId = payload.AccumulatorId,
                    StoredEnergy = 0f,
                    EnergyRequired = payload.EnergyRequired,
                    ExpiresAt = now + payload.RetentionSeconds,
                    HitEnergySpawn = payload.Spawn
                });

                return entries.Length - 1;
            }

            private static int FindEntryIndex(DynamicBuffer<TargetHitEnergy> entries, int accumulatorId)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].AccumulatorId == accumulatorId)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }
    }
}
