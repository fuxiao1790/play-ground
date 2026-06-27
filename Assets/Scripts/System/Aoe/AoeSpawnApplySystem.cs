using System;
using System.Collections.Generic;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Aoe
{
    // Port of AoeSpawnSystem with input changed from AoeSpawnRequestElement to AoeSpawnCommand.
    // Reads PendingCommands from AoeSpawnExpansionSystem. VFX emission moved to expansion.
    // Built beside the old AoeSpawnSystem; inert until Task 006 wires producers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeSpawnExpansionSystem))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public partial class AoeSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker =
            new("AoeSpawnApplySystem");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("AoeSpawnApplySystem.ReuseJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "AoeSpawnApplySystem.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "AoeSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

        private EntityArchetype lingeringArchetype;
        private EntityArchetype timedSpawnerLingeringArchetype;
        private EntityArchetype impactArchetype;

        private readonly Dictionary<AoeSpawnKey, AoeSpawnBucket> _byKey = new();
        private readonly Dictionary<AoeSpawnKey, EntityQuery> _deadSlotQueriesByKey = new();
        private readonly List<AoeSpawnBucket> _bucketPool = new();
        private readonly List<AoeSpawnWork> _spawnWork = new();

        protected override void OnCreate()
        {
            lingeringArchetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(CombatLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeAreaComponent),
                typeof(AoePulseVfxComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(AoeContactGateElement));
            timedSpawnerLingeringArchetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(CombatLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeAreaComponent),
                typeof(AoePulseVfxComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(AoeContactGateElement),
                typeof(TimedSpawnTag),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));
            impactArchetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeAreaComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag));
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            DisposeSpawnWorkReferences();
            DisposeBuckets(_byKey.Values);
            DisposeBuckets(_bucketPool);
            _byKey.Clear();
            _deadSlotQueriesByKey.Clear();
            _bucketPool.Clear();
            _spawnWork.Clear();
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            ReturnBuckets();

            var expansionSys = World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            int totalRequests = 0;
            if (expansionSys != null && expansionSys.PendingCommands.IsCreated)
            {
                expansionSys.PendingHandle.Complete();
                NativeStream.Reader reader = expansionSys.PendingCommands.AsReader();
                for (int i = 0; i < reader.ForEachCount; i++)
                {
                    int n = reader.BeginForEachIndex(i);
                    for (int j = 0; j < n; j++)
                    {
                        AoeSpawnCommand cmd = reader.Read<AoeSpawnCommand>();
                        bool hasTimedSpawner = HasTimedSpawner(cmd);
                        var key = new AoeSpawnKey(((int)cmd.Faction << 16) | cmd.TypeId, cmd.Lifetime > 0f, hasTimedSpawner);
                        if (!_byKey.TryGetValue(key, out AoeSpawnBucket bucket))
                        {
                            bucket = GetBucket();
                            _byKey[key] = bucket;
                        }
                        bucket.Requests.Add(cmd);
                        totalRequests++;
                    }
                    reader.EndForEachIndex();
                }
                expansionSys.PendingCommands.Dispose();
            }

            if (totalRequests == 0) return;

            using (SpawnMarker.Auto())
            {
                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                int reuseCount = 0;
                int coldCreateCount = 0;
                _spawnWork.Clear();

                using (ReuseJobMarker.Auto())
                {
                    var jobHandles = new NativeList<JobHandle>(_byKey.Count, Allocator.Temp);
                    foreach (var (key, bucket) in _byKey)
                    {
                        CombatFaction faction = (CombatFaction)(key.BatchId >> 16);
                        EntityQuery query = DeadSlotQueryFor(key);
                        NativeArray<AoeSpawnCommand> configs = bucket.Requests.AsArray();
                        var claimedReference = new NativeReference<int>(Allocator.TempJob);
                        claimedReference.Value = 0;

                        JobHandle spawnHandle = new AoeSpawnJob
                        {
                            Faction               = faction,
                            Configs               = configs,
                            ClaimedCount          = claimedReference,
                            ActiveHandle          = GetComponentTypeHandle<Active>(false),
                            CollisionActiveHandle = GetComponentTypeHandle<AoeCollisionActiveTag>(false),
                            RenderActiveHandle    = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                            IdentityHandle        = GetComponentTypeHandle<AoeIdentityComponent>(false),
                            KinematicsHandle      = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                            CollisionHandle       = GetComponentTypeHandle<CombatCollisionComponent>(false),
                            LifetimeHandle        = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                            HitGateHandle         = GetComponentTypeHandle<AoeHitGateComponent>(false),
                            HitSpawnHandle        = GetComponentTypeHandle<AoeHitSpawnComponent>(false),
                            AreaHandle            = GetComponentTypeHandle<AoeAreaComponent>(false),
                            PulseVfxHandle        = GetComponentTypeHandle<AoePulseVfxComponent>(false),
                            RenderHandle          = GetComponentTypeHandle<CombatRenderComponent>(false),
                            RenderElementHandle   = GetComponentTypeHandle<CombatRenderElement>(false),
                            ContactGateHandle     = GetBufferTypeHandle<AoeContactGateElement>(false),
                            TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                            TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false),
                            HasLingeringComponents = key.Lingering,
                            HasTimedSpawner = key.HasTimedSpawner,
                        }.Schedule(query, default);

                        jobHandles.Add(spawnHandle);
                        _spawnWork.Add(new AoeSpawnWork(faction, configs, claimedReference));
                    }

                    JobHandle.CombineDependencies(jobHandles.AsArray()).Complete();
                    jobHandles.Dispose();
                }

                foreach (AoeSpawnWork work in _spawnWork)
                {
                    int claimed = work.ClaimedCount.Value;
                    work.ClaimedCount.Dispose();
                    reuseCount += claimed;
                    for (int i = claimed; i < work.Configs.Length; i++)
                    {
                        CreateAoeEntity(work.Faction, work.Configs[i], createEcb);
                        coldCreateCount++;
                    }
                }
                _spawnWork.Clear();

                if (coldCreateCount > 0)
                    createEcb.Playback(EntityManager);
                SpawnReuseCounter.Value = reuseCount;
                SpawnColdCreateCounter.Value = totalRequests - reuseCount;
            }
        }

        private void ReturnBuckets()
        {
            foreach (var bucket in _byKey.Values)
            {
                bucket.Requests.Clear();
                _bucketPool.Add(bucket);
            }
            _byKey.Clear();
        }

        private AoeSpawnBucket GetBucket()
        {
            if (_bucketPool.Count > 0)
            {
                int last = _bucketPool.Count - 1;
                var bucket = _bucketPool[last];
                _bucketPool.RemoveAt(last);
                return bucket;
            }
            return new AoeSpawnBucket();
        }

        private static void DisposeBuckets(IEnumerable<AoeSpawnBucket> buckets)
        {
            foreach (AoeSpawnBucket bucket in buckets)
                bucket.Dispose();
        }

        private EntityQuery DeadSlotQueryFor(AoeSpawnKey key)
        {
            if (!_deadSlotQueriesByKey.TryGetValue(key, out EntityQuery query))
            {
                if (key.Lingering)
                {
                    if (key.HasTimedSpawner)
                    {
                        query = new EntityQueryBuilder(Allocator.Temp)
                            .WithAll<AoeTag>()
                            .WithAll<CombatRenderBatchId>()
                            .WithAll<CombatLifetimeComponent>()
                            .WithAll<TimedSpawnTag>()
                            .WithDisabled<Active>()
                            .Build(this);
                    }
                    else
                    {
                        query = new EntityQueryBuilder(Allocator.Temp)
                            .WithAll<AoeTag>()
                            .WithAll<CombatRenderBatchId>()
                            .WithAll<CombatLifetimeComponent>()
                            .WithDisabled<Active>()
                            .WithNone<TimedSpawnTag>()
                            .Build(this);
                    }
                }
                else
                {
                    query = new EntityQueryBuilder(Allocator.Temp)
                        .WithAll<AoeTag>()
                        .WithAll<CombatRenderBatchId>()
                        .WithDisabled<Active>()
                        .WithNone<CombatLifetimeComponent>()
                        .WithNone<TimedSpawnTag>()
                        .Build(this);
                }
                _deadSlotQueriesByKey[key] = query;
            }

            query.SetSharedComponentFilter(new CombatRenderBatchId { Value = key.BatchId });
            return query;
        }

        private void DisposeSpawnWorkReferences()
        {
            foreach (AoeSpawnWork work in _spawnWork)
            {
                if (work.ClaimedCount.IsCreated)
                    work.ClaimedCount.Dispose();
            }
            _spawnWork.Clear();
        }

        private void CreateAoeEntity(CombatFaction faction, AoeSpawnCommand cmd, EntityCommandBuffer ecb)
        {
            bool lingering = cmd.Lifetime > 0f;
            bool hasTimedSpawner = HasTimedSpawner(cmd);
            EntityArchetype archetype = lingering
                ? hasTimedSpawner ? timedSpawnerLingeringArchetype : lingeringArchetype
                : impactArchetype;
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = ((int)faction << 16) | cmd.TypeId });
            RecordAoeReset(ecb, entity, faction, cmd, lingering, hasTimedSpawner);
        }

        private static void RecordAoeReset(
            EntityCommandBuffer ecb,
            Entity entity,
            CombatFaction faction,
            AoeSpawnCommand cmd,
            bool lingering,
            bool hasTimedSpawner)
        {
            CombatKinematicsComponent kinematics = KinematicsFor(cmd);
            CombatRenderComponent render = cmd.Render;
            ecb.SetComponent(entity, IdentityFor(faction, cmd));
            ecb.SetComponent(entity, kinematics);
            ecb.SetComponent(entity, CollisionFor(cmd));
            ecb.SetComponent(entity, HitGateFor(cmd));
            ecb.SetComponent(entity, HitSpawnFor(cmd, faction));
            ecb.SetComponent(entity, AreaFor(cmd));
            if (lingering)
            {
                ecb.SetComponent(entity, new CombatLifetimeComponent { Remaining = cmd.Lifetime });
                ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);
                ecb.SetComponent(entity, PulseVfxFor(cmd));
            }
            if (hasTimedSpawner)
            {
                ecb.SetComponent(entity, cmd.TimedSpawn);
                ecb.SetComponent(entity, InitialTimedSpawnStateFor(cmd));
            }
            ecb.SetComponent(entity, render);
            ecb.SetComponent(entity, CombatRenderMatrixUtility.ElementFor(kinematics, render));
            bool collisionEnabled = NeedsCollision(cmd);
            bool active = lingering || collisionEnabled;
            ecb.SetComponentEnabled<Active>(entity, active);
            ecb.SetComponentEnabled<AoeCollisionActiveTag>(entity, collisionEnabled);
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, active);
        }

        private static bool NeedsCollision(in AoeSpawnCommand cmd) =>
            cmd.HitPayload.DirectDamageEnabled
            || cmd.HitPayload.StackEffect.Enabled
            || cmd.OnHitSpawn.Enabled;

        private static bool HasTimedSpawner(in AoeSpawnCommand cmd) =>
            cmd.Lifetime > 0f && !cmd.TimedSpawn.TemplateKey.Equals(default(Hash128));

        private static TimedSpawnStateComponent InitialTimedSpawnStateFor(in AoeSpawnCommand cmd) =>
            new TimedSpawnStateComponent
            {
                CooldownRemaining = cmd.TimedSpawn.IntervalSeconds
                    + DeterministicJitter(
                        cmd.AoeId,
                        cmd.TimedSpawn.JitterSeed,
                        cmd.TimedSpawn.IntervalJitterSeconds),
                TickIndex = 0
            };

        private static AoeIdentityComponent IdentityFor(CombatFaction faction, in AoeSpawnCommand cmd) =>
            new AoeIdentityComponent { Faction = faction, AoeId = cmd.AoeId, TypeId = cmd.TypeId };

        private static CombatKinematicsComponent KinematicsFor(in AoeSpawnCommand cmd) =>
            new CombatKinematicsComponent { Position = cmd.Position, Velocity = default };

        private static CombatCollisionComponent CollisionFor(in AoeSpawnCommand cmd) =>
            new CombatCollisionComponent
            {
                ShapeType       = cmd.ShapeType,
                Radius          = cmd.Radius,
                HalfExtents     = cmd.HalfExtents,
                RotationRadians = cmd.RotationRadians,
                BoundsMin       = cmd.BoundsMin,
                BoundsMax       = cmd.BoundsMax
            };

        private static AoeHitGateComponent HitGateFor(in AoeSpawnCommand cmd) =>
            new AoeHitGateComponent { RepeatHitCooldownSeconds = cmd.RepeatHitCooldownSeconds };

        private static AoeHitSpawnComponent HitSpawnFor(in AoeSpawnCommand cmd, CombatFaction faction) =>
            new AoeHitSpawnComponent
            {
                HitPayload = HitPayloadFor(cmd.HitPayload, faction),
                OnHitSpawn = cmd.OnHitSpawn
            };

        private static CombatHitPayload HitPayloadFor(CombatHitPayload hitPayload, CombatFaction faction)
        {
            StackEffectSnapshot stack = hitPayload.StackEffect;
            stack.Faction = faction;
            hitPayload.StackEffect = stack;
            return hitPayload;
        }

        private static AoeAreaComponent AreaFor(in AoeSpawnCommand cmd) =>
            new AoeAreaComponent { Size = cmd.AreaSize > 0f ? cmd.AreaSize : 1f };

        private static AoePulseVfxComponent PulseVfxFor(in AoeSpawnCommand cmd)
        {
            float interval = cmd.RepeatHitCooldownSeconds > 0f ? cmd.RepeatHitCooldownSeconds : 0f;
            return new AoePulseVfxComponent { Interval = interval, RemainingInterval = interval };
        }

        [BurstCompile]
        private struct AoeSpawnJob : IJobChunk
        {
            public CombatFaction Faction;
            [ReadOnly] public NativeArray<AoeSpawnCommand> Configs;
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<Active>                   ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeCollisionActiveTag>    CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag>    RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeIdentityComponent>     IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent>  CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatLifetimeComponent>  LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeHitGateComponent>      HitGateHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeHitSpawnComponent>     HitSpawnHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeAreaComponent>         AreaHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoePulseVfxComponent>     PulseVfxHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent>    RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement>      RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<AoeContactGateElement>       ContactGateHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TimedSpawnStateComponent> TimedSpawnStateHandle;
            public bool HasLingeringComponents;
            public bool HasTimedSpawner;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Configs.Length) return;

                EnabledMask activeMask          = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask    = chunk.GetEnabledMask(ref RenderActiveHandle);

                NativeArray<AoeIdentityComponent>     identities  = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent> kinematics  = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent>  collisions  = chunk.GetNativeArray(ref CollisionHandle);
                NativeArray<AoeHitGateComponent>      hitGates    = chunk.GetNativeArray(ref HitGateHandle);
                NativeArray<AoeHitSpawnComponent>     hitSpawns   = chunk.GetNativeArray(ref HitSpawnHandle);
                NativeArray<AoeAreaComponent>         areas       = chunk.GetNativeArray(ref AreaHandle);
                NativeArray<CombatRenderComponent>    renders     = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement>      renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                EnabledMask lifetimeMask = default;
                NativeArray<CombatLifetimeComponent> lifetimes = default;
                NativeArray<AoePulseVfxComponent> pulseVfxs = default;
                BufferAccessor<AoeContactGateElement> gates = default;
                NativeArray<TimedSpawnComponent> timedSpawns = default;
                NativeArray<TimedSpawnStateComponent> timedSpawnStates = default;
                if (HasLingeringComponents)
                {
                    lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                    lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                    pulseVfxs = chunk.GetNativeArray(ref PulseVfxHandle);
                    gates = chunk.GetBufferAccessor(ref ContactGateHandle);
                    if (HasTimedSpawner)
                    {
                        timedSpawns = chunk.GetNativeArray(ref TimedSpawnHandle);
                        timedSpawnStates = chunk.GetNativeArray(ref TimedSpawnStateHandle);
                    }
                }

                for (int i = 0; i < chunk.Count && cfgIdx < Configs.Length; i++)
                {
                    if (activeMask[i]) continue;

                    AoeSpawnCommand cfg = Configs[cfgIdx++];

                    identities[i] = new AoeIdentityComponent
                    {
                        Faction = Faction, AoeId = cfg.AoeId, TypeId = cfg.TypeId
                    };
                    CombatKinematicsComponent kin = new CombatKinematicsComponent
                    {
                        Position = cfg.Position, Velocity = default
                    };
                    kinematics[i] = kin;
                    collisions[i] = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType, Radius = cfg.Radius, HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians, BoundsMin = cfg.BoundsMin, BoundsMax = cfg.BoundsMax
                    };
                    hitGates[i]    = new AoeHitGateComponent
                    {
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds
                    };
                    hitSpawns[i]   = new AoeHitSpawnComponent
                    {
                        HitPayload = HitPayloadFor(cfg.HitPayload, Faction),
                        OnHitSpawn = cfg.OnHitSpawn
                    };
                    areas[i]       = new AoeAreaComponent
                    {
                        Size = cfg.AreaSize > 0f ? cfg.AreaSize : 1f
                    };
                    if (HasLingeringComponents)
                    {
                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                        lifetimeMask[i] = true;
                        float interval = cfg.RepeatHitCooldownSeconds > 0f ? cfg.RepeatHitCooldownSeconds : 0f;
                        pulseVfxs[i] = new AoePulseVfxComponent
                        {
                            Interval = interval, RemainingInterval = interval
                        };
                        gates[i].Clear();
                    }
                    if (HasTimedSpawner)
                    {
                        timedSpawns[i] = cfg.TimedSpawn;
                        timedSpawnStates[i] = InitialTimedSpawnStateFor(cfg);
                    }
                    CombatRenderComponent render = cfg.Render;
                    renders[i]     = render;
                    renderElems[i] = CombatRenderMatrixUtility.ElementFor(kin, render);

                    bool collisionEnabled = NeedsCollision(cfg);
                    bool active = HasLingeringComponents || collisionEnabled;
                    activeMask[i]          = active;
                    collisionActiveMask[i] = collisionEnabled;
                    renderActiveMask[i]    = active;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }

        private readonly struct AoeSpawnKey : IEquatable<AoeSpawnKey>
        {
            private readonly int _batchId;
            private readonly bool _lingering;
            private readonly bool _hasTimedSpawner;

            public int BatchId          => _batchId;
            public bool Lingering       => _lingering;
            public bool HasTimedSpawner => _hasTimedSpawner;

            public AoeSpawnKey(int batchId, bool lingering, bool hasTimedSpawner)
            {
                _batchId         = batchId;
                _lingering       = lingering;
                _hasTimedSpawner = hasTimedSpawner;
            }

            public bool Equals(AoeSpawnKey other) =>
                _batchId == other._batchId
                && _lingering == other._lingering
                && _hasTimedSpawner == other._hasTimedSpawner;

            public override bool Equals(object obj) => obj is AoeSpawnKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _batchId;
                    hash = hash * 397 ^ (_lingering ? 1 : 0);
                    hash = hash * 397 ^ (_hasTimedSpawner ? 1 : 0);
                    return hash;
                }
            }
        }

        private sealed class AoeSpawnBucket : IDisposable
        {
            public readonly NativeList<AoeSpawnCommand> Requests =
                new(Allocator.Persistent);

            public void Dispose()
            {
                if (Requests.IsCreated)
                    Requests.Dispose();
            }
        }

        private static float DeterministicJitter(int aoeId, int jitterSeed, float maxOffsetSeconds)
        {
            if (maxOffsetSeconds <= 0f)
            {
                return 0f;
            }

            unchecked
            {
                uint hash = (uint)aoeId;
                hash = (hash * 397u) ^ (uint)jitterSeed;
                hash *= 0x9E3779B9u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return ((hash & 0x00FFFFFFu) + 1u) / 16777217f * maxOffsetSeconds;
            }
        }

        private readonly struct AoeSpawnWork
        {
            public readonly CombatFaction Faction;
            public readonly NativeArray<AoeSpawnCommand> Configs;
            public readonly NativeReference<int> ClaimedCount;

            public AoeSpawnWork(
                CombatFaction faction,
                NativeArray<AoeSpawnCommand> configs,
                NativeReference<int> claimedCount)
            {
                Faction = faction;
                Configs = configs;
                ClaimedCount = claimedCount;
            }
        }
    }
}
