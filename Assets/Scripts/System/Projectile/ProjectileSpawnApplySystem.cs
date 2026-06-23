using System;
using System.Collections.Generic;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    public abstract partial class ProjectileSpawnApplySystemBase : SystemBase
    {
        private readonly ProfilerMarker _spawnMarker;
        private readonly ProfilerMarker _reuseJobMarker;
        private readonly ProfilerCounterValue<int> _spawnColdCreateCounter;
        private readonly ProfilerCounterValue<int> _spawnReuseCounter;

        private EntityArchetype _archetype;

        private readonly Dictionary<ProjectileSpawnKey, ProjectileSpawnBucket> _byKey = new();
        private readonly Dictionary<ProjectileSpawnKey, EntityQuery> _deadSlotQueriesByKey = new();
        private readonly List<ProjectileSpawnBucket> _bucketPool = new();
        private readonly List<ProjectileSpawnWork> _spawnWork = new();

        protected ProjectileSpawnApplySystemBase(
            string spawnMarkerName,
            string reuseMarkerName,
            string coldCounterName,
            string reuseCounterName)
        {
            _spawnMarker = new ProfilerMarker(spawnMarkerName);
            _reuseJobMarker = new ProfilerMarker(reuseMarkerName);
            _spawnColdCreateCounter =
                new ProfilerCounterValue<int>(ProfilerCategory.Scripts, coldCounterName, ProfilerMarkerDataUnit.Count);
            _spawnReuseCounter =
                new ProfilerCounterValue<int>(ProfilerCategory.Scripts, reuseCounterName, ProfilerMarkerDataUnit.Count);
        }

        protected override void OnCreate()
        {
            _archetype = CreateArchetype();
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

            var expansionSys = World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            int totalRequests = 0;
            if (expansionSys != null)
            {
                expansionSys.PendingHandle.Complete();
                NativeQueue<ProjectileSpawnCommand> commandContainer = CommandContainer(expansionSys);
                if (commandContainer.IsCreated && commandContainer.Count > 0)
                {
                    // Bulk-drain in one memcpy instead of per-element TryDequeue: the managed
                    // NativeQueue.TryDequeue path crosses the safety boundary per command and
                    // copies the (large) command struct out individually, which dominated the
                    // main-thread cost of this system. ToArray copies the whole queue once.
                    NativeArray<ProjectileSpawnCommand> commands = commandContainer.ToArray(Allocator.Temp);
                    commandContainer.Clear();

                    for (int i = 0; i < commands.Length; i++)
                    {
                        ProjectileSpawnCommand cmd = commands[i];
                        var key = new ProjectileSpawnKey((int)cmd.Faction, cmd.TypeId);
                        if (!_byKey.TryGetValue(key, out ProjectileSpawnBucket bucket))
                        {
                            bucket = GetBucket();
                            _byKey[key] = bucket;
                        }
                        bucket.Requests.Add(cmd);
                        totalRequests++;
                    }

                    commands.Dispose();
                }
            }

            if (totalRequests == 0) return;

            using (_spawnMarker.Auto())
            {
                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                int reuseCount = 0;
                _spawnWork.Clear();

                using (_reuseJobMarker.Auto())
                {
                    var jobHandles = new NativeList<JobHandle>(_byKey.Count, Allocator.Temp);
                    foreach (var (key, bucket) in _byKey)
                    {
                        CombatFaction faction = (CombatFaction)key.FactionValue;
                        EntityQuery query = DeadSlotQueryFor(key, faction);
                        NativeArray<ProjectileSpawnCommand> configs = bucket.Requests.AsArray();
                        var claimedReference = new NativeReference<int>(Allocator.TempJob);
                        claimedReference.Value = 0;

                        JobHandle spawnHandle = ScheduleReuseJob(faction, configs, claimedReference, query);
                        jobHandles.Add(spawnHandle);
                        _spawnWork.Add(new ProjectileSpawnWork(faction, configs, claimedReference));
                    }

                    JobHandle.CombineDependencies(jobHandles.AsArray()).Complete();
                    jobHandles.Dispose();
                }

                int coldCreateCount = 0;
                foreach (ProjectileSpawnWork work in _spawnWork)
                {
                    int claimed = work.ClaimedCount.Value;
                    work.ClaimedCount.Dispose();
                    reuseCount += claimed;
                    for (int i = claimed; i < work.Configs.Length; i++)
                    {
                        CreateProjectileEntity(work.Faction, work.Configs[i], _archetype, createEcb);
                        coldCreateCount++;
                    }
                }
                _spawnWork.Clear();

                if (coldCreateCount > 0)
                    createEcb.Playback(EntityManager);
                _spawnReuseCounter.Value = reuseCount;
                _spawnColdCreateCounter.Value = totalRequests - reuseCount;
            }
        }

        protected abstract EntityArchetype CreateArchetype();

        protected abstract NativeQueue<ProjectileSpawnCommand> CommandContainer(
            ProjectileSpawnExpansionSystem expansionSys);

        protected abstract EntityQuery BuildDeadSlotQuery();

        protected abstract JobHandle ScheduleReuseJob(
            CombatFaction faction,
            NativeArray<ProjectileSpawnCommand> configs,
            NativeReference<int> claimedReference,
            EntityQuery query);

        protected abstract void CreateProjectileEntity(
            CombatFaction faction,
            ProjectileSpawnCommand cmd,
            EntityArchetype archetype,
            EntityCommandBuffer ecb);

        private void ReturnBuckets()
        {
            foreach (var bucket in _byKey.Values)
            {
                bucket.Requests.Clear();
                _bucketPool.Add(bucket);
            }
            _byKey.Clear();
        }

        private ProjectileSpawnBucket GetBucket()
        {
            if (_bucketPool.Count > 0)
            {
                int last = _bucketPool.Count - 1;
                var bucket = _bucketPool[last];
                _bucketPool.RemoveAt(last);
                return bucket;
            }
            return new ProjectileSpawnBucket();
        }

        private static void DisposeBuckets(IEnumerable<ProjectileSpawnBucket> buckets)
        {
            foreach (ProjectileSpawnBucket bucket in buckets)
                bucket.Dispose();
        }

        private EntityQuery DeadSlotQueryFor(ProjectileSpawnKey key, CombatFaction faction)
        {
            if (!_deadSlotQueriesByKey.TryGetValue(key, out EntityQuery query))
            {
                query = BuildDeadSlotQuery();
                _deadSlotQueriesByKey[key] = query;
            }

            query.SetSharedComponentFilter(
                new CombatRenderFaction { Faction = faction },
                new CombatRenderTypeId { TypeId = key.TypeId });
            return query;
        }

        private void DisposeSpawnWorkReferences()
        {
            foreach (ProjectileSpawnWork work in _spawnWork)
            {
                if (work.ClaimedCount.IsCreated)
                    work.ClaimedCount.Dispose();
            }
            _spawnWork.Clear();
        }

        protected static void RecordCommonProjectileReset(
            EntityCommandBuffer ecb,
            Entity entity,
            CombatFaction faction,
            ProjectileSpawnCommand cmd)
        {
            ecb.SetComponent(entity, new ProjectileIdentityComponent
            {
                Faction = faction,
                ProjectileId = cmd.ProjectileId,
                TypeId = cmd.TypeId
            });
            ecb.SetComponent(entity, new CombatKinematicsComponent
            {
                Position = cmd.Position,
                Velocity = cmd.Velocity
            });
            ecb.SetComponent(entity, new CombatCollisionComponent
            {
                ShapeType = cmd.ShapeType,
                Radius = cmd.Radius,
                HalfExtents = cmd.HalfExtents,
                RotationRadians = cmd.RotationRadians,
                BoundsMin = cmd.BoundsMin,
                BoundsMax = cmd.BoundsMax
            });
            ecb.SetComponent(entity, new CombatLifetimeComponent { Remaining = cmd.Lifetime });
            ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);
            ecb.SetComponent(entity, new ProjectileHitComponent
            {
                PierceRemaining = cmd.PierceRemaining,
                RepeatHitCooldownSeconds = cmd.RepeatHitCooldownSeconds,
                HitPayload = cmd.HitPayload
            });
            ecb.SetComponent(entity, cmd.Tracking);
            ecb.SetComponentEnabled<ProjectileTrackingComponent>(entity, cmd.Tracking.TrackingEnabled);
            ecb.SetComponent(entity, cmd.Render);
            ecb.SetComponent(entity, new CombatRenderElement());

            if (cmd.SeedContactGateTargetId > 0)
            {
                ecb.AppendToBuffer(entity, new ProjectileContactGateElement
                {
                    TargetId = cmd.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, cmd.RepeatHitCooldownSeconds)
                });
            }

            ecb.SetComponentEnabled<Active>(entity, true);
            ecb.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, NeedsCollision(cmd.HitPayload));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        protected static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled
            || payload.StackEffect.Enabled
            || payload.ImpactAoe.Enabled
            || payload.ImpactProjectile.Enabled;

        private readonly struct ProjectileSpawnKey : IEquatable<ProjectileSpawnKey>
        {
            private readonly int _factionValue;
            private readonly int _typeId;

            public int FactionValue => _factionValue;
            public int TypeId => _typeId;

            public ProjectileSpawnKey(int factionValue, int typeId)
            {
                _factionValue = factionValue;
                _typeId = typeId;
            }

            public bool Equals(ProjectileSpawnKey other) =>
                _factionValue == other._factionValue &&
                _typeId == other._typeId;

            public override bool Equals(object obj) => obj is ProjectileSpawnKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    return _factionValue * 397 ^ _typeId;
                }
            }
        }

        private sealed class ProjectileSpawnBucket : IDisposable
        {
            public readonly NativeList<ProjectileSpawnCommand> Requests =
                new(Allocator.Persistent);

            public void Dispose()
            {
                if (Requests.IsCreated)
                    Requests.Dispose();
            }
        }

        private readonly struct ProjectileSpawnWork
        {
            public readonly CombatFaction Faction;
            public readonly NativeArray<ProjectileSpawnCommand> Configs;
            public readonly NativeReference<int> ClaimedCount;

            public ProjectileSpawnWork(
                CombatFaction faction,
                NativeArray<ProjectileSpawnCommand> configs,
                NativeReference<int> claimedCount)
            {
                Faction = faction;
                Configs = configs;
                ClaimedCount = claimedCount;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public sealed partial class BasicProjectileSpawnApplySystem : ProjectileSpawnApplySystemBase
    {
        public BasicProjectileSpawnApplySystem()
            : base(
                "BasicProjectileSpawnApplySystem",
                "BasicProjectileSpawnApplySystem.ReuseJob",
                "BasicProjectileSpawnApplySystem.Cold",
                "BasicProjectileSpawnApplySystem.Reuse")
        {
        }

        protected override EntityArchetype CreateArchetype() =>
            EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement));

        protected override NativeQueue<ProjectileSpawnCommand> CommandContainer(
            ProjectileSpawnExpansionSystem expansionSys) =>
            expansionSys.BasicProjectileCommandContainer;

        protected override EntityQuery BuildDeadSlotQuery() =>
            new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<CombatRenderFaction>()
                .WithAll<CombatRenderTypeId>()
                .WithDisabled<Active>()
                .WithNone<ProjectileChildSpawnerTag>()
                .Build(this);

        protected override JobHandle ScheduleReuseJob(
            CombatFaction faction,
            NativeArray<ProjectileSpawnCommand> configs,
            NativeReference<int> claimedReference,
            EntityQuery query) =>
            new BasicProjectileSpawnJob
            {
                Faction = faction,
                Configs = configs,
                ClaimedCount = claimedReference,
                ActiveHandle = GetComponentTypeHandle<Active>(false),
                CollisionActiveHandle = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                TrackingHandle = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false),
                ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
            }.Schedule(query, default);

        protected override void CreateProjectileEntity(
            CombatFaction faction,
            ProjectileSpawnCommand cmd,
            EntityArchetype archetype,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderFaction { Faction = faction });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = cmd.TypeId });
            RecordCommonProjectileReset(ecb, entity, faction, cmd);
        }

        [BurstCompile]
        private struct BasicProjectileSpawnJob : IJobChunk
        {
            public CombatFaction Faction;
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Configs;
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<Active> ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileCollisionActiveTag> CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileTrackingComponent> TrackingHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement> RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Configs.Length) return;

                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);
                EnabledMask trackingMask = chunk.GetEnabledMask(ref TrackingHandle);

                NativeArray<ProjectileIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent> collisions = chunk.GetNativeArray(ref CollisionHandle);
                EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<ProjectileHitComponent> hits = chunk.GetNativeArray(ref HitHandle);
                NativeArray<ProjectileTrackingComponent> tracking = chunk.GetNativeArray(ref TrackingHandle);
                NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement> renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<ProjectileContactGateElement> gates = chunk.GetBufferAccessor(ref ContactGateHandle);

                for (int i = 0; i < chunk.Count && cfgIdx < Configs.Length; i++)
                {
                    if (activeMask[i]) continue;

                    ProjectileSpawnCommand cfg = Configs[cfgIdx++];

                    identities[i] = new ProjectileIdentityComponent
                    {
                        Faction = Faction,
                        ProjectileId = cfg.ProjectileId,
                        TypeId = cfg.TypeId
                    };
                    kinematics[i] = new CombatKinematicsComponent
                    {
                        Position = cfg.Position,
                        Velocity = cfg.Velocity
                    };
                    collisions[i] = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType,
                        Radius = cfg.Radius,
                        HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians,
                        BoundsMin = cfg.BoundsMin,
                        BoundsMax = cfg.BoundsMax
                    };
                    lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                    lifetimeMask[i] = true;
                    hits[i] = new ProjectileHitComponent
                    {
                        PierceRemaining = cfg.PierceRemaining,
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                        HitPayload = cfg.HitPayload
                    };
                    tracking[i] = cfg.Tracking;
                    trackingMask[i] = cfg.Tracking.TrackingEnabled;
                    renders[i] = cfg.Render;
                    renderElems[i] = new CombatRenderElement();

                    DynamicBuffer<ProjectileContactGateElement> gate = gates[i];
                    gate.Clear();
                    if (cfg.SeedContactGateTargetId > 0)
                    {
                        gate.Add(new ProjectileContactGateElement
                        {
                            TargetId = cfg.SeedContactGateTargetId,
                            CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                        });
                    }

                    activeMask[i] = true;
                    collisionActiveMask[i] = NeedsCollision(cfg.HitPayload);
                    renderActiveMask[i] = true;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public sealed partial class ChildSpawnerProjectileSpawnApplySystem : ProjectileSpawnApplySystemBase
    {
        public ChildSpawnerProjectileSpawnApplySystem()
            : base(
                "ChildSpawnerProjectileSpawnApplySystem",
                "ChildSpawnerProjectileSpawnApplySystem.ReuseJob",
                "ChildSpawnerProjectileSpawnApplySystem.Cold",
                "ChildSpawnerProjectileSpawnApplySystem.Reuse")
        {
        }

        protected override EntityArchetype CreateArchetype() =>
            EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement),
                typeof(ProjectileChildSpawnerTag),
                typeof(ProjectileChildSpawnerComponent),
                typeof(ProjectileChildSpawnStateComponent));

        protected override NativeQueue<ProjectileSpawnCommand> CommandContainer(
            ProjectileSpawnExpansionSystem expansionSys) =>
            expansionSys.ChildSpawnerProjectileCommandContainer;

        protected override EntityQuery BuildDeadSlotQuery() =>
            new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<CombatRenderFaction>()
                .WithAll<CombatRenderTypeId>()
                .WithAll<ProjectileChildSpawnerTag>()
                .WithDisabled<Active>()
                .Build(this);

        protected override JobHandle ScheduleReuseJob(
            CombatFaction faction,
            NativeArray<ProjectileSpawnCommand> configs,
            NativeReference<int> claimedReference,
            EntityQuery query) =>
            new ChildSpawnerProjectileSpawnJob
            {
                Faction = faction,
                Configs = configs,
                ClaimedCount = claimedReference,
                ActiveHandle = GetComponentTypeHandle<Active>(false),
                CollisionActiveHandle = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                TrackingHandle = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false),
                ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
                ChildSpawnerHandle = GetComponentTypeHandle<ProjectileChildSpawnerComponent>(false),
                ChildSpawnStateHandle = GetComponentTypeHandle<ProjectileChildSpawnStateComponent>(false),
            }.Schedule(query, default);

        protected override void CreateProjectileEntity(
            CombatFaction faction,
            ProjectileSpawnCommand cmd,
            EntityArchetype archetype,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderFaction { Faction = faction });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = cmd.TypeId });
            RecordCommonProjectileReset(ecb, entity, faction, cmd);
            ecb.SetComponent(entity, cmd.ChildSpawner);
            ecb.SetComponent(entity, cmd.ChildSpawnState);
        }

        [BurstCompile]
        private struct ChildSpawnerProjectileSpawnJob : IJobChunk
        {
            public CombatFaction Faction;
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Configs;
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<Active> ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileCollisionActiveTag> CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileTrackingComponent> TrackingHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement> RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileChildSpawnerComponent> ChildSpawnerHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileChildSpawnStateComponent> ChildSpawnStateHandle;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Configs.Length) return;

                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);
                EnabledMask trackingMask = chunk.GetEnabledMask(ref TrackingHandle);

                NativeArray<ProjectileIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent> collisions = chunk.GetNativeArray(ref CollisionHandle);
                EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<ProjectileHitComponent> hits = chunk.GetNativeArray(ref HitHandle);
                NativeArray<ProjectileTrackingComponent> tracking = chunk.GetNativeArray(ref TrackingHandle);
                NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement> renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<ProjectileContactGateElement> gates = chunk.GetBufferAccessor(ref ContactGateHandle);
                NativeArray<ProjectileChildSpawnerComponent> childSpawners =
                    chunk.GetNativeArray(ref ChildSpawnerHandle);
                NativeArray<ProjectileChildSpawnStateComponent> childStates =
                    chunk.GetNativeArray(ref ChildSpawnStateHandle);

                for (int i = 0; i < chunk.Count && cfgIdx < Configs.Length; i++)
                {
                    if (activeMask[i]) continue;

                    ProjectileSpawnCommand cfg = Configs[cfgIdx++];

                    identities[i] = new ProjectileIdentityComponent
                    {
                        Faction = Faction,
                        ProjectileId = cfg.ProjectileId,
                        TypeId = cfg.TypeId
                    };
                    kinematics[i] = new CombatKinematicsComponent
                    {
                        Position = cfg.Position,
                        Velocity = cfg.Velocity
                    };
                    collisions[i] = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType,
                        Radius = cfg.Radius,
                        HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians,
                        BoundsMin = cfg.BoundsMin,
                        BoundsMax = cfg.BoundsMax
                    };
                    lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                    lifetimeMask[i] = true;
                    hits[i] = new ProjectileHitComponent
                    {
                        PierceRemaining = cfg.PierceRemaining,
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                        HitPayload = cfg.HitPayload
                    };
                    tracking[i] = cfg.Tracking;
                    trackingMask[i] = cfg.Tracking.TrackingEnabled;
                    renders[i] = cfg.Render;
                    renderElems[i] = new CombatRenderElement();

                    DynamicBuffer<ProjectileContactGateElement> gate = gates[i];
                    gate.Clear();
                    if (cfg.SeedContactGateTargetId > 0)
                    {
                        gate.Add(new ProjectileContactGateElement
                        {
                            TargetId = cfg.SeedContactGateTargetId,
                            CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                        });
                    }

                    childSpawners[i] = cfg.ChildSpawner;
                    childStates[i] = cfg.ChildSpawnState;

                    activeMask[i] = true;
                    collisionActiveMask[i] = NeedsCollision(cfg.HitPayload);
                    renderActiveMask[i] = true;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }
    }
}
