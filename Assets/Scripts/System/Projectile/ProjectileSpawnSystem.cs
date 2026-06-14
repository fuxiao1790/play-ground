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
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    public partial class ProjectileSpawnSystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker =
            new("Projectile.Spawn");
        private static readonly ProfilerMarker SliceAssignMarker =
            new("Projectile.Spawn.SliceAssign.MainThread");                  // 2A
        // private static readonly ProfilerMarker CountJobMarker =
        //     new("Projectile.Spawn.CountJob");                              // 2B
        // private static readonly ProfilerMarker PrefixSumMarker =
        //     new("Projectile.Spawn.PrefixSum");                             // 2B
        private static readonly ProfilerMarker ResetJobMarker =
            new("Projectile.Spawn.ResetJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "Projectile.Spawn.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "Projectile.Spawn.Reuse", ProfilerMarkerDataUnit.Count);

        private EntityArchetype archetypeNoChildSpawner;
        private EntityArchetype archetypeWithChildSpawner;
        private EntityQuery scopeQuery;
        private EntityQuery deadSlotsNoChildSpawner;
        private EntityQuery deadSlotsWithChildSpawner;

        private readonly Dictionary<ProjectileSpawnKey, List<ProjectileSpawnRequestElement>> _byKey = new();
        private readonly Dictionary<int, Entity> _scopeByIndex = new();
        private readonly List<List<ProjectileSpawnRequestElement>> _listPool = new();

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileScope>(),
                ComponentType.ReadWrite<ProjectileSpawnRequestElement>());

            archetypeNoChildSpawner = EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(ProjectileLifetimeComponent),
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
                typeof(ProjectileLifetimeComponent),
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

            deadSlotsNoChildSpawner = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<CombatRenderScope>()
                .WithAll<CombatRenderTypeId>()
                .WithDisabled<ProjectileActiveTag>()
                .WithNone<ProjectileChildSpawnerTag>()
                .Build(this);

            deadSlotsWithChildSpawner = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<CombatRenderScope>()
                .WithAll<CombatRenderTypeId>()
                .WithDisabled<ProjectileActiveTag>()
                .WithAll<ProjectileChildSpawnerTag>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            ReturnLists();
            using var scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            int totalRequests = 0;
            for (int i = 0; i < scopes.Length; i++)
            {
                Entity scope = scopes[i];
                DynamicBuffer<ProjectileSpawnRequestElement> requests =
                    EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scope);
                int count = requests.Length;
                if (count == 0) continue;
                _scopeByIndex[scope.Index] = scope;
                for (int j = 0; j < count; j++)
                {
                    ProjectileSpawnRequestElement req = requests[j];
                    var key = new ProjectileSpawnKey(scope.Index, req.TypeId, req.HasChildSpawner != 0);
                    if (!_byKey.TryGetValue(key, out List<ProjectileSpawnRequestElement> list))
                    {
                        list = GetList();
                        _byKey[key] = list;
                    }
                    list.Add(req);
                }
                requests.Clear();
                totalRequests += count;
            }

            if (totalRequests == 0) return;

            using (SpawnMarker.Auto())
            {
                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                int reuseCount = 0;
                int coldCreateCount = 0;

                ComponentTypeHandle<ProjectileActiveTag> activeHandle =
                    GetComponentTypeHandle<ProjectileActiveTag>(false);

                var jobHandles = new NativeList<JobHandle>(_byKey.Count * 4, Allocator.Temp);

                foreach (var (key, requests) in _byKey)
                {
                    Entity scope = _scopeByIndex[key.ScopeIndex];
                    bool hasChildSpawner = key.HasChildSpawner;
                    EntityQuery query = hasChildSpawner ? deadSlotsWithChildSpawner : deadSlotsNoChildSpawner;
                    query.SetSharedComponentFilter(
                        new CombatRenderScope { Scope = scope },
                        new CombatRenderTypeId { TypeId = key.TypeId });

                    int reqCount = requests.Count;
                    var configs = new NativeList<ProjectileSpawnRequestElement>(reqCount, Allocator.TempJob);
                    for (int i = 0; i < reqCount; i++) configs.Add(requests[i]);

                    // ---- Option 2A: main-thread slice assignment (active) ----
                    NativeList<int2> slices;
                    int claimed;
                    using (SliceAssignMarker.Auto())
                        slices = AssignSlices2A(query, reqCount, ref activeHandle, out claimed);

                    // ---- Option 2B: counting job + prefix sum (swap with 2A to compare) ----
                    // NativeList<int2> slices;
                    // int claimed;
                    // using (CountJobMarker.Auto())
                    // {
                    //     JobHandle countHandle = ScheduleCountJob2B(query, ref activeHandle, out NativeList<int> deadCounts);
                    //     countHandle.Complete();
                    //     using (PrefixSumMarker.Auto())
                    //         (slices, claimed) = PrefixSumSlices2B(deadCounts, reqCount);
                    //     deadCounts.Dispose();
                    // }

                    reuseCount += claimed;
                    for (int i = claimed; i < reqCount; i++)
                    {
                        CreateProjectileEntity(scope, configs[i], hasChildSpawner, createEcb);
                        coldCreateCount++;
                    }

                    if (claimed > 0)
                    {
                        JobHandle spawnHandle;
                        using (ResetJobMarker.Auto())
                        {
                            spawnHandle = new ProjectileSpawnJob
                            {
                                Scope                 = scope,
                                Configs               = configs.AsArray(),
                                Slices                = slices.AsArray(),
                                ActiveHandle          = activeHandle,
                                CollisionActiveHandle  = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                                RenderActiveHandle    = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                                IdentityHandle        = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                                KinematicsHandle      = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                                CollisionHandle       = GetComponentTypeHandle<CombatCollisionComponent>(false),
                                LifetimeHandle        = GetComponentTypeHandle<ProjectileLifetimeComponent>(false),
                                HitHandle             = GetComponentTypeHandle<ProjectileHitComponent>(false),
                                TrackingHandle        = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                                RenderHandle          = GetComponentTypeHandle<CombatRenderComponent>(false),
                                RenderElementHandle   = GetComponentTypeHandle<CombatRenderElement>(false),
                                ContactGateHandle     = GetBufferTypeHandle<ProjectileContactGateElement>(false),
                                ChildSpawnerHandle    = GetComponentTypeHandle<ProjectileChildSpawnerComponent>(false),
                                ChildSpawnStateHandle = GetComponentTypeHandle<ProjectileChildSpawnStateComponent>(false),
                            }.ScheduleParallel(query, default);
                        }
                        jobHandles.Add(configs.Dispose(spawnHandle));
                        jobHandles.Add(slices.Dispose(spawnHandle));
                    }
                    else
                    {
                        configs.Dispose();
                        slices.Dispose();
                    }
                }

                Dependency = JobHandle.CombineDependencies(jobHandles.AsArray());
                if (coldCreateCount > 0)
                    createEcb.Playback(EntityManager);
                SpawnReuseCounter.Value = reuseCount;
                SpawnColdCreateCounter.Value = totalRequests - reuseCount;
                jobHandles.Dispose();
            }
        }

        private void ReturnLists()
        {
            foreach (var list in _byKey.Values)
            {
                list.Clear();
                _listPool.Add(list);
            }
            _byKey.Clear();
            _scopeByIndex.Clear();
        }

        private List<ProjectileSpawnRequestElement> GetList()
        {
            if (_listPool.Count > 0)
            {
                int last = _listPool.Count - 1;
                var l = _listPool[last];
                _listPool.RemoveAt(last);
                return l;
            }
            return new List<ProjectileSpawnRequestElement>();
        }

        // ---- Option 2A ----
        private NativeList<int2> AssignSlices2A(
            EntityQuery query,
            int configCount,
            ref ComponentTypeHandle<ProjectileActiveTag> activeHandle,
            out int claimed)
        {
            using NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            using NativeArray<int> filteredChunkIndexes = query.CalculateFilteredChunkIndexArray(Allocator.Temp);
            var slices = new NativeList<int2>(filteredChunkIndexes.Length, Allocator.TempJob);
            for (int i = 0; i < filteredChunkIndexes.Length; i++) slices.Add(default);

            int cursor = 0;
            for (int unfilteredChunkIndex = 0;
                 unfilteredChunkIndex < filteredChunkIndexes.Length && cursor < configCount;
                 unfilteredChunkIndex++)
            {
                int filteredChunkIndex = filteredChunkIndexes[unfilteredChunkIndex];
                if (filteredChunkIndex < 0) continue;

                int dead = CountDisabled(chunks[filteredChunkIndex], ref activeHandle);
                int take = math.min(dead, configCount - cursor);
                slices[unfilteredChunkIndex] = new int2(cursor, take);
                cursor += take;
            }

            claimed = cursor;
            return slices;
        }

        private static int CountDisabled(ArchetypeChunk chunk, ref ComponentTypeHandle<ProjectileActiveTag> handle)
        {
            EnabledMask mask = chunk.GetEnabledMask(ref handle);
            int count = 0;
            for (int i = 0; i < chunk.Count; i++)
                if (!mask[i]) count++;
            return count;
        }

        /* ---- Option 2B: counting job + prefix sum (commented out — swap with 2A to compare) ----

        private JobHandle ScheduleCountJob2B(
            EntityQuery query,
            ref ComponentTypeHandle<ProjectileActiveTag> activeHandle,
            out NativeList<int> deadCounts)
        {
            int chunkCount = query.CalculateChunkCount();
            deadCounts = new NativeList<int>(chunkCount, Allocator.TempJob);
            for (int i = 0; i < chunkCount; i++) deadCounts.Add(0);
            return new CountDeadJob
            {
                ActiveHandle = activeHandle,
                DeadCounts   = deadCounts.AsArray()
            }.ScheduleParallel(query, default);
        }

        private static (NativeList<int2> slices, int claimed) PrefixSumSlices2B(
            NativeList<int> deadCounts,
            int configCount)
        {
            var slices = new NativeList<int2>(deadCounts.Length, Allocator.TempJob);
            int cursor = 0;
            for (int c = 0; c < deadCounts.Length; c++)
            {
                int take = math.min(deadCounts[c], configCount - cursor);
                slices.Add(new int2(cursor, take));
                cursor += take;
            }
            return (slices, cursor);
        }

        [BurstCompile]
        private struct CountDeadJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ProjectileActiveTag> ActiveHandle;
            [NativeDisableContainerSafetyRestriction][WriteOnly]
            public NativeArray<int> DeadCounts;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                EnabledMask mask = chunk.GetEnabledMask(ref ActiveHandle);
                int dead = 0;
                for (int i = 0; i < chunk.Count; i++)
                    if (!mask[i]) dead++;
                DeadCounts[unfilteredChunkIndex] = dead;
            }
        }

        ---- end Option 2B ---- */

        private void CreateProjectileEntity(
            Entity scope,
            ProjectileSpawnRequestElement request,
            bool hasChildSpawner,
            EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(hasChildSpawner ? archetypeWithChildSpawner : archetypeNoChildSpawner);
            ecb.AddSharedComponent(entity, new CombatRenderScope { Scope = scope });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = request.TypeId });
            RecordProjectileReset(ecb, entity, scope, request, hasChildSpawner);
        }

        private static void RecordProjectileReset(
            EntityCommandBuffer ecb,
            Entity entity,
            Entity scope,
            ProjectileSpawnRequestElement request,
            bool hasChildSpawner)
        {
            ecb.SetComponent(entity, new ProjectileIdentityComponent
            {
                Scope = scope,
                ProjectileId = request.ProjectileId,
                TypeId = request.TypeId
            });
            ecb.SetComponent(entity, new CombatKinematicsComponent
            {
                Position = request.Position,
                Velocity = request.Velocity
            });
            ecb.SetComponent(entity, new CombatCollisionComponent
            {
                ShapeType = request.ShapeType,
                Radius = request.Radius,
                HalfExtents = request.HalfExtents,
                RotationRadians = request.RotationRadians,
                BoundsMin = request.BoundsMin,
                BoundsMax = request.BoundsMax
            });
            ecb.SetComponent(entity, new ProjectileLifetimeComponent
            {
                RemainingLifetime = request.Lifetime
            });
            ecb.SetComponent(entity, new ProjectileHitComponent
            {
                PierceRemaining = request.PierceRemaining,
                RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds,
                HitPayload = request.HitPayload
            });
            ecb.SetComponent(entity, request.Tracking);
            ecb.SetComponentEnabled<ProjectileTrackingComponent>(entity, request.Tracking.TrackingEnabled);
            ecb.SetComponent(entity, request.Render);
            ecb.SetComponent(entity, new CombatRenderElement());

            if (hasChildSpawner)
            {
                ecb.SetComponent(entity, request.ChildSpawner);
                ecb.SetComponent(entity, request.ChildSpawnState);
            }

            if (request.SeedContactGateTargetId > 0)
            {
                ecb.AppendToBuffer(entity, new ProjectileContactGateElement
                {
                    TargetId = request.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, request.RepeatHitCooldownSeconds)
                });
            }

            ecb.SetComponentEnabled<ProjectileActiveTag>(entity, true);
            ecb.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, NeedsCollision(request.HitPayload));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled || payload.StackEffect.Enabled;

        [BurstCompile]
        private struct ProjectileSpawnJob : IJobChunk
        {
            public Entity Scope;
            [ReadOnly] public NativeArray<ProjectileSpawnRequestElement> Configs;
            [ReadOnly] public NativeArray<int2> Slices;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileActiveTag>            ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileCollisionActiveTag>   CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag>          RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileIdentityComponent>    IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent>      KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent>       CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ProjectileLifetimeComponent>    LifetimeHandle;
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
                int2 slice = Slices[unfilteredChunkIndex];
                if (slice.y == 0) return;

                EnabledMask activeMask         = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask   = chunk.GetEnabledMask(ref RenderActiveHandle);
                EnabledMask trackingMask       = chunk.GetEnabledMask(ref TrackingHandle);

                NativeArray<ProjectileIdentityComponent>  identities  = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent>    kinematics  = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent>     collisions  = chunk.GetNativeArray(ref CollisionHandle);
                NativeArray<ProjectileLifetimeComponent>  lifetimes   = chunk.GetNativeArray(ref LifetimeHandle);
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

                int cfgIdx = slice.x;
                int cfgEnd = slice.x + slice.y;

                for (int i = 0; i < chunk.Count && cfgIdx < cfgEnd; i++)
                {
                    if (activeMask[i]) continue;

                    ProjectileSpawnRequestElement cfg = Configs[cfgIdx++];

                    identities[i]  = new ProjectileIdentityComponent
                    {
                        Scope = Scope, ProjectileId = cfg.ProjectileId, TypeId = cfg.TypeId
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
                    lifetimes[i]   = new ProjectileLifetimeComponent { RemainingLifetime = cfg.Lifetime };
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
            }
        }

        private readonly struct ProjectileSpawnKey : IEquatable<ProjectileSpawnKey>
        {
            private readonly int  _scopeIndex;
            private readonly int  _typeId;
            private readonly byte _hasChildSpawner;

            public int  ScopeIndex      => _scopeIndex;
            public int  TypeId          => _typeId;
            public bool HasChildSpawner => _hasChildSpawner != 0;

            public ProjectileSpawnKey(int scopeIndex, int typeId, bool hasChildSpawner)
            {
                _scopeIndex      = scopeIndex;
                _typeId          = typeId;
                _hasChildSpawner = hasChildSpawner ? (byte)1 : (byte)0;
            }

            public bool Equals(ProjectileSpawnKey other) =>
                _scopeIndex      == other._scopeIndex &&
                _typeId          == other._typeId     &&
                _hasChildSpawner == other._hasChildSpawner;

            public override bool Equals(object obj) => obj is ProjectileSpawnKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _scopeIndex;
                    h = h * 397 ^ _typeId;
                    h = h * 397 ^ _hasChildSpawner;
                    return h;
                }
            }
        }
    }
}
