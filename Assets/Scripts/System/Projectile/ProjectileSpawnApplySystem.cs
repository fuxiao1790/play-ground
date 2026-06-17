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

namespace PlayGround.System.Projectile
{
    // Port of ProjectileSpawnSystem with input changed from ProjectileSpawnRequestElement
    // to ProjectileSpawnCommandData. Reads PendingCommands from ProjectileSpawnExpansionSystem.
    // Built beside the old ProjectileSpawnSystem; inert until Task 004 wires producers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    public partial class ProjectileSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker =
            new("Projectile.SpawnApply");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("Projectile.SpawnApply.ReuseJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "Projectile.SpawnApply.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "Projectile.SpawnApply.Reuse", ProfilerMarkerDataUnit.Count);

        private EntityArchetype archetypeNoChildSpawner;
        private EntityArchetype archetypeWithChildSpawner;

        private readonly Dictionary<ProjectileSpawnKey, ProjectileSpawnBucket> _byKey = new();
        private readonly Dictionary<ProjectileSpawnKey, EntityQuery> _deadSlotQueriesByKey = new();
        private readonly List<ProjectileSpawnBucket> _bucketPool = new();
        private readonly List<ProjectileSpawnWork> _spawnWork = new();

        protected override void OnCreate()
        {
            archetypeNoChildSpawner = EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(ProjectileActiveTag),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement));

            archetypeWithChildSpawner = EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(ProjectileActiveTag),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement),
                typeof(ProjectileChildSpawnerTag),
                typeof(ProjectileChildSpawnerComponent),
                typeof(ProjectileChildSpawnStateComponent));
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

            var expansionSys = World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
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
                        ProjectileSpawnCommandData cmd = reader.Read<ProjectileSpawnCommandData>();
                        var key = new ProjectileSpawnKey((int)cmd.Faction, cmd.TypeId, cmd.HasChildSpawner != 0);
                        if (!_byKey.TryGetValue(key, out ProjectileSpawnBucket bucket))
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
                        NativeArray<ProjectileSpawnCommandData> configs = bucket.Requests.AsArray();
                        var claimedReference = new NativeReference<int>(Allocator.TempJob);
                        claimedReference.Value = 0;

                        JobHandle spawnHandle = new ProjectileSpawnJob
                        {
                            Faction               = faction,
                            Configs               = configs,
                            ClaimedCount          = claimedReference,
                            ActiveHandle          = GetComponentTypeHandle<ProjectileActiveTag>(false),
                            CollisionActiveHandle  = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                            RenderActiveHandle    = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                            IdentityHandle        = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                            KinematicsHandle      = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                            CollisionHandle       = GetComponentTypeHandle<CombatCollisionComponent>(false),
                            LifetimeHandle        = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                            HitHandle             = GetComponentTypeHandle<ProjectileHitComponent>(false),
                            TrackingHandle        = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                            RenderHandle          = GetComponentTypeHandle<CombatRenderComponent>(false),
                            RenderElementHandle   = GetComponentTypeHandle<CombatRenderElement>(false),
                            ContactGateHandle     = GetBufferTypeHandle<ProjectileContactGateElement>(false),
                            ChildSpawnerHandle    = GetComponentTypeHandle<ProjectileChildSpawnerComponent>(false),
                            ChildSpawnStateHandle = GetComponentTypeHandle<ProjectileChildSpawnStateComponent>(false),
                        }.Schedule(query, default);
                        jobHandles.Add(spawnHandle);
                        _spawnWork.Add(new ProjectileSpawnWork(
                            faction,
                            key.HasChildSpawner,
                            configs,
                            claimedReference));
                    }

                    JobHandle.CombineDependencies(jobHandles.AsArray()).Complete();
                    jobHandles.Dispose();
                }

                foreach (ProjectileSpawnWork work in _spawnWork)
                {
                    int claimed = work.ClaimedCount.Value;
                    work.ClaimedCount.Dispose();
                    reuseCount += claimed;
                    for (int i = claimed; i < work.Configs.Length; i++)
                    {
                        CreateProjectileEntity(work.Faction, work.Configs[i], work.HasChildSpawner, createEcb);
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
                EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<ProjectileTag>()
                    .WithAll<CombatRenderFaction>()
                    .WithAll<CombatRenderTypeId>()
                    .WithDisabled<ProjectileActiveTag>();

                query = key.HasChildSpawner
                    ? builder.WithAll<ProjectileChildSpawnerTag>().Build(this)
                    : builder.WithNone<ProjectileChildSpawnerTag>().Build(this);
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

        private void CreateProjectileEntity(
            CombatFaction faction,
            ProjectileSpawnCommandData cmd,
            bool hasChildSpawner,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(hasChildSpawner ? archetypeWithChildSpawner : archetypeNoChildSpawner);
            ecb.AddSharedComponent(entity, new CombatRenderFaction { Faction = faction });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = cmd.TypeId });
            RecordProjectileReset(ecb, entity, faction, cmd, hasChildSpawner);
        }

        private static void RecordProjectileReset(
            EntityCommandBuffer ecb,
            Entity entity,
            CombatFaction faction,
            ProjectileSpawnCommandData cmd,
            bool hasChildSpawner)
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

            if (hasChildSpawner)
            {
                ecb.SetComponent(entity, cmd.ChildSpawner);
                ecb.SetComponent(entity, cmd.ChildSpawnState);
            }

            if (cmd.SeedContactGateTargetId > 0)
            {
                ecb.AppendToBuffer(entity, new ProjectileContactGateElement
                {
                    TargetId = cmd.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, cmd.RepeatHitCooldownSeconds)
                });
            }

            ecb.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            ecb.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, NeedsCollision(cmd.HitPayload));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled
            || payload.StackEffect.Enabled
            || payload.ImpactAoe.Enabled
            || payload.ImpactProjectile.Enabled;

        [BurstCompile]
        private struct ProjectileSpawnJob : IJobChunk
        {
            public CombatFaction Faction;
            [ReadOnly] public NativeArray<ProjectileSpawnCommandData> Configs;
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileActiveTag>            ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileCollisionActiveTag>   CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag>          RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileIdentityComponent>    IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent>      KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent>       CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatLifetimeComponent>        LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileHitComponent>         HitHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileTrackingComponent>    TrackingHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent>          RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement>            RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<ProjectileContactGateElement>      ContactGateHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileChildSpawnerComponent>    ChildSpawnerHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileChildSpawnStateComponent> ChildSpawnStateHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Configs.Length) return;

                EnabledMask activeMask         = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask   = chunk.GetEnabledMask(ref RenderActiveHandle);
                EnabledMask trackingMask       = chunk.GetEnabledMask(ref TrackingHandle);

                NativeArray<ProjectileIdentityComponent>  identities  = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent>    kinematics  = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent>     collisions  = chunk.GetNativeArray(ref CollisionHandle);
                EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                NativeArray<CombatLifetimeComponent>      lifetimes   = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<ProjectileHitComponent>       hits        = chunk.GetNativeArray(ref HitHandle);
                NativeArray<ProjectileTrackingComponent>  tracking    = chunk.GetNativeArray(ref TrackingHandle);
                NativeArray<CombatRenderComponent>        renders     = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement>          renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<ProjectileContactGateElement> gates    = chunk.GetBufferAccessor(ref ContactGateHandle);

                bool hasChildSpawner = chunk.Has(ref ChildSpawnerHandle);
                NativeArray<ProjectileChildSpawnerComponent>    childSpawners =
                    hasChildSpawner ? chunk.GetNativeArray(ref ChildSpawnerHandle)   : default;
                NativeArray<ProjectileChildSpawnStateComponent> childStates   =
                    hasChildSpawner ? chunk.GetNativeArray(ref ChildSpawnStateHandle) : default;

                for (int i = 0; i < chunk.Count && cfgIdx < Configs.Length; i++)
                {
                    if (activeMask[i]) continue;

                    ProjectileSpawnCommandData cfg = Configs[cfgIdx++];

                    identities[i]  = new ProjectileIdentityComponent
                    {
                        Faction = Faction, ProjectileId = cfg.ProjectileId, TypeId = cfg.TypeId
                    };
                    kinematics[i]  = new CombatKinematicsComponent
                    {
                        Position = cfg.Position, Velocity = cfg.Velocity
                    };
                    collisions[i]  = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType, Radius = cfg.Radius, HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians, BoundsMin = cfg.BoundsMin, BoundsMax = cfg.BoundsMax
                    };
                    lifetimes[i]   = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                    lifetimeMask[i] = true;
                    hits[i]        = new ProjectileHitComponent
                    {
                        PierceRemaining          = cfg.PierceRemaining,
                        RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                        HitPayload               = cfg.HitPayload
                    };
                    tracking[i]    = cfg.Tracking;
                    trackingMask[i] = cfg.Tracking.TrackingEnabled;
                    renders[i]     = cfg.Render;
                    renderElems[i] = new CombatRenderElement();

                    DynamicBuffer<ProjectileContactGateElement> gate = gates[i];
                    gate.Clear();
                    if (cfg.SeedContactGateTargetId > 0)
                    {
                        gate.Add(new ProjectileContactGateElement
                        {
                            TargetId          = cfg.SeedContactGateTargetId,
                            CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                        });
                    }

                    if (hasChildSpawner)
                    {
                        childSpawners[i] = cfg.ChildSpawner;
                        childStates[i]   = cfg.ChildSpawnState;
                    }

                    activeMask[i]          = true;
                    collisionActiveMask[i]  = NeedsCollision(cfg.HitPayload);
                    renderActiveMask[i]    = true;
                }

                ClaimedCount.Value = cfgIdx;
            }
        }

        private readonly struct ProjectileSpawnKey : IEquatable<ProjectileSpawnKey>
        {
            private readonly int  _factionValue;
            private readonly int  _typeId;
            private readonly byte _hasChildSpawner;

            public int  FactionValue    => _factionValue;
            public int  TypeId          => _typeId;
            public bool HasChildSpawner => _hasChildSpawner != 0;

            public ProjectileSpawnKey(int factionValue, int typeId, bool hasChildSpawner)
            {
                _factionValue    = factionValue;
                _typeId          = typeId;
                _hasChildSpawner = hasChildSpawner ? (byte)1 : (byte)0;
            }

            public bool Equals(ProjectileSpawnKey other) =>
                _factionValue    == other._factionValue &&
                _typeId          == other._typeId     &&
                _hasChildSpawner == other._hasChildSpawner;

            public override bool Equals(object obj) => obj is ProjectileSpawnKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _factionValue;
                    h = h * 397 ^ _typeId;
                    h = h * 397 ^ _hasChildSpawner;
                    return h;
                }
            }
        }

        private sealed class ProjectileSpawnBucket : IDisposable
        {
            public readonly NativeList<ProjectileSpawnCommandData> Requests =
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
            public readonly bool HasChildSpawner;
            public readonly NativeArray<ProjectileSpawnCommandData> Configs;
            public readonly NativeReference<int> ClaimedCount;

            public ProjectileSpawnWork(
                CombatFaction faction,
                bool hasChildSpawner,
                NativeArray<ProjectileSpawnCommandData> configs,
                NativeReference<int> claimedCount)
            {
                Faction = faction;
                HasChildSpawner = hasChildSpawner;
                Configs = configs;
                ClaimedCount = claimedCount;
            }
        }
    }
}
