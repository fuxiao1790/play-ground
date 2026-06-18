using System;
using System.Collections.Generic;
using PlayGround.System.Common;
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
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public partial class AoeSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker =
            new("Aoe.Spawn");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("Aoe.Spawn.ReuseJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "Aoe.Spawn.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "Aoe.Spawn.Reuse", ProfilerMarkerDataUnit.Count);

        private EntityArchetype archetype;

        private readonly Dictionary<AoeSpawnKey, AoeSpawnBucket> _byKey = new();
        private readonly Dictionary<AoeSpawnKey, EntityQuery> _deadSlotQueriesByKey = new();
        private readonly List<AoeSpawnBucket> _bucketPool = new();
        private readonly List<AoeSpawnWork> _spawnWork = new();

        protected override void OnCreate()
        {
            archetype = EntityManager.CreateArchetype(
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
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            DisposeSpawnWorkReferences();
            DisposeBuckets(_byKey.Values);
            DisposeBuckets(_bucketPool);
            foreach (EntityQuery query in _deadSlotQueriesByKey.Values)
                query.Dispose();
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
                        var key = new AoeSpawnKey((int)cmd.Faction, cmd.TypeId);
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
                        CombatFaction faction = (CombatFaction)key.FactionValue;
                        EntityQuery query = DeadSlotQueryFor(key, faction);
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

        private EntityQuery DeadSlotQueryFor(AoeSpawnKey key, CombatFaction faction)
        {
            if (!_deadSlotQueriesByKey.TryGetValue(key, out EntityQuery query))
            {
                query = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<AoeTag>()
                    .WithAll<CombatRenderFaction>()
                    .WithAll<CombatRenderTypeId>()
                    .WithDisabled<Active>()
                    .Build(this);
                _deadSlotQueriesByKey[key] = query;
            }

            query.SetSharedComponentFilter(
                new CombatRenderFaction { Faction = faction },
                new CombatRenderTypeId { TypeId = key.TypeId });
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
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderFaction { Faction = faction });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = cmd.TypeId });
            RecordAoeReset(ecb, entity, faction, cmd);
        }

        private static void RecordAoeReset(EntityCommandBuffer ecb, Entity entity, CombatFaction faction, AoeSpawnCommand cmd)
        {
            CombatKinematicsComponent kinematics = KinematicsFor(cmd);
            CombatRenderComponent render = cmd.Render;
            ecb.SetComponent(entity, IdentityFor(faction, cmd));
            ecb.SetComponent(entity, kinematics);
            ecb.SetComponent(entity, CollisionFor(cmd));
            ecb.SetComponent(entity, new CombatLifetimeComponent { Remaining = cmd.Lifetime });
            ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, cmd.Lifetime > 0f);
            ecb.SetComponent(entity, HitGateFor(cmd));
            ecb.SetComponent(entity, HitSpawnFor(cmd));
            ecb.SetComponent(entity, AreaFor(cmd));
            ecb.SetComponent(entity, PulseVfxFor(cmd));
            ecb.SetComponent(entity, render);
            ecb.SetComponent(entity, CombatRenderMatrixUtility.ElementFor(kinematics, render));
            ecb.SetComponentEnabled<Active>(entity, true);
            ecb.SetComponentEnabled<AoeCollisionActiveTag>(entity, NeedsCollision(cmd));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static bool NeedsCollision(in AoeSpawnCommand cmd) =>
            cmd.HitPayload.DirectDamageEnabled
            || cmd.HitPayload.StackEffect.Enabled
            || cmd.ProjectileBurst.Enabled;

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

        private static AoeHitSpawnComponent HitSpawnFor(in AoeSpawnCommand cmd) =>
            new AoeHitSpawnComponent { HitPayload = cmd.HitPayload, ProjectileBurst = cmd.ProjectileBurst };

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
                EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                NativeArray<CombatLifetimeComponent>  lifetimes   = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<AoeHitGateComponent>      hitGates    = chunk.GetNativeArray(ref HitGateHandle);
                NativeArray<AoeHitSpawnComponent>     hitSpawns   = chunk.GetNativeArray(ref HitSpawnHandle);
                NativeArray<AoeAreaComponent>         areas       = chunk.GetNativeArray(ref AreaHandle);
                NativeArray<AoePulseVfxComponent>     pulseVfxs   = chunk.GetNativeArray(ref PulseVfxHandle);
                NativeArray<CombatRenderComponent>    renders     = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement>      renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<AoeContactGateElement> gates       = chunk.GetBufferAccessor(ref ContactGateHandle);

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
                    lifetimes[i]   = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                    lifetimeMask[i] = cfg.Lifetime > 0f;
                    hitGates[i]    = new AoeHitGateComponent
                    {
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds
                    };
                    hitSpawns[i]   = new AoeHitSpawnComponent
                    {
                        HitPayload = cfg.HitPayload, ProjectileBurst = cfg.ProjectileBurst
                    };
                    areas[i]       = new AoeAreaComponent
                    {
                        Size = cfg.AreaSize > 0f ? cfg.AreaSize : 1f
                    };
                    float interval = cfg.RepeatHitCooldownSeconds > 0f ? cfg.RepeatHitCooldownSeconds : 0f;
                    pulseVfxs[i]   = new AoePulseVfxComponent
                    {
                        Interval = interval, RemainingInterval = interval
                    };
                    CombatRenderComponent render = cfg.Render;
                    renders[i]     = render;
                    renderElems[i] = CombatRenderMatrixUtility.ElementFor(kin, render);
                    gates[i].Clear();

                    activeMask[i]          = true;
                    collisionActiveMask[i] = NeedsCollision(cfg);
                    renderActiveMask[i]    = true;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }

        private readonly struct AoeSpawnKey : IEquatable<AoeSpawnKey>
        {
            private readonly int _factionValue;
            private readonly int _typeId;

            public int FactionValue => _factionValue;
            public int TypeId       => _typeId;

            public AoeSpawnKey(int factionValue, int typeId)
            {
                _factionValue = factionValue;
                _typeId       = typeId;
            }

            public bool Equals(AoeSpawnKey other) =>
                _factionValue == other._factionValue && _typeId == other._typeId;

            public override bool Equals(object obj) => obj is AoeSpawnKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    return _factionValue * 397 ^ _typeId;
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
