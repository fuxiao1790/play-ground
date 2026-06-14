using System;
using System.Collections.Generic;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
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
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeSimulationSystem))]
    [UpdateBefore(typeof(ProjectileSpawnSystem))]
    [UpdateBefore(typeof(AoeCollisionSystem))]
    [UpdateBefore(typeof(PlayGround.System.Common.CombatRenderPrepareSystem))]
    public partial class AoeSpawnSystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker =
            new("Aoe.Spawn");
        private static readonly ProfilerMarker SliceAssignMarker =
            new("Aoe.Spawn.SliceAssign.MainThread");                     // 2A
        // private static readonly ProfilerMarker CountJobMarker =
        //     new("Aoe.Spawn.CountJob");                                 // 2B
        // private static readonly ProfilerMarker PrefixSumMarker =
        //     new("Aoe.Spawn.PrefixSum");                                // 2B
        private static readonly ProfilerMarker ResetJobMarker =
            new("Aoe.Spawn.ResetJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "Aoe.Spawn.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "Aoe.Spawn.Reuse", ProfilerMarkerDataUnit.Count);

        private EntityArchetype archetype;
        private EntityQuery scopeQuery;
        private EntityQuery deadSlots;

        private readonly Dictionary<AoeSpawnKey, List<AoeSpawnRequestElement>> _byKey = new();
        private readonly Dictionary<int, Entity> _scopeByIndex = new();
        private readonly List<List<AoeSpawnRequestElement>> _listPool = new();

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeScope>(),
                ComponentType.ReadWrite<AoeSpawnRequestElement>());

            archetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(AoeLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeAreaComponent),
                typeof(AoePulseVfxComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(AoeActiveTag),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(AoeContactGateElement));

            deadSlots = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<CombatRenderScope>()
                .WithAll<CombatRenderTypeId>()
                .WithDisabled<AoeActiveTag>()
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
                DynamicBuffer<AoeSpawnRequestElement> requests =
                    EntityManager.GetBuffer<AoeSpawnRequestElement>(scope);
                int count = requests.Length;
                if (count == 0) continue;

                _scopeByIndex[scope.Index] = scope;
                DynamicBuffer<VfxSpawnRequestElement> vfxBuffer =
                    EntityManager.GetBuffer<VfxSpawnRequestElement>(scope);
                vfxBuffer.EnsureCapacity(vfxBuffer.Length + count);

                for (int j = 0; j < count; j++)
                {
                    AoeSpawnRequestElement req = requests[j];
                    var key = new AoeSpawnKey(scope.Index, req.TypeId);
                    if (!_byKey.TryGetValue(key, out List<AoeSpawnRequestElement> list))
                    {
                        list = GetList();
                        _byKey[key] = list;
                    }
                    list.Add(req);
                    vfxBuffer.Add(new VfxSpawnRequestElement
                    {
                        TypeId   = req.TypeId,
                        Trigger  = 0,
                        Position = req.Position,
                        AreaSize = req.AreaSize
                    });
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

                ComponentTypeHandle<AoeActiveTag> activeHandle =
                    GetComponentTypeHandle<AoeActiveTag>(false);

                var jobHandles = new NativeList<JobHandle>(_byKey.Count * 4, Allocator.Temp);

                foreach (var (key, requests) in _byKey)
                {
                    Entity scope = _scopeByIndex[key.ScopeIndex];
                    deadSlots.SetSharedComponentFilter(
                        new CombatRenderScope { Scope = scope },
                        new CombatRenderTypeId { TypeId = key.TypeId });

                    int reqCount = requests.Count;
                    var configs = new NativeList<AoeSpawnRequestElement>(reqCount, Allocator.TempJob);
                    for (int i = 0; i < reqCount; i++) configs.Add(requests[i]);

                    // ---- Option 2A: main-thread slice assignment (active) ----
                    NativeList<int2> slices;
                    int claimed;
                    using (SliceAssignMarker.Auto())
                        slices = AssignSlices2A(reqCount, ref activeHandle, out claimed);

                    // ---- Option 2B: counting job + prefix sum (swap with 2A to compare) ----
                    // NativeList<int2> slices;
                    // int claimed;
                    // using (CountJobMarker.Auto())
                    // {
                    //     JobHandle countHandle = ScheduleCountJob2B(ref activeHandle, out NativeList<int> deadCounts);
                    //     countHandle.Complete();
                    //     using (PrefixSumMarker.Auto())
                    //         (slices, claimed) = PrefixSumSlices2B(deadCounts, reqCount);
                    //     deadCounts.Dispose();
                    // }

                    reuseCount += claimed;
                    for (int i = claimed; i < reqCount; i++)
                    {
                        CreateAoeEntity(scope, configs[i], createEcb);
                        coldCreateCount++;
                    }

                    if (claimed > 0)
                    {
                        JobHandle spawnHandle;
                        using (ResetJobMarker.Auto())
                        {
                            spawnHandle = new AoeSpawnJob
                            {
                                Scope               = scope,
                                Configs             = configs.AsArray(),
                                Slices              = slices.AsArray(),
                                ActiveHandle        = activeHandle,
                                CollisionActiveHandle = GetComponentTypeHandle<AoeCollisionActiveTag>(false),
                                RenderActiveHandle  = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                                IdentityHandle      = GetComponentTypeHandle<AoeIdentityComponent>(false),
                                KinematicsHandle    = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                                CollisionHandle     = GetComponentTypeHandle<CombatCollisionComponent>(false),
                                LifetimeHandle      = GetComponentTypeHandle<AoeLifetimeComponent>(false),
                                HitGateHandle       = GetComponentTypeHandle<AoeHitGateComponent>(false),
                                HitSpawnHandle      = GetComponentTypeHandle<AoeHitSpawnComponent>(false),
                                AreaHandle          = GetComponentTypeHandle<AoeAreaComponent>(false),
                                PulseVfxHandle      = GetComponentTypeHandle<AoePulseVfxComponent>(false),
                                RenderHandle        = GetComponentTypeHandle<CombatRenderComponent>(false),
                                RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false),
                                ContactGateHandle   = GetBufferTypeHandle<AoeContactGateElement>(false),
                            }.ScheduleParallel(deadSlots, default);
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

        private List<AoeSpawnRequestElement> GetList()
        {
            if (_listPool.Count > 0)
            {
                int last = _listPool.Count - 1;
                var l = _listPool[last];
                _listPool.RemoveAt(last);
                return l;
            }
            return new List<AoeSpawnRequestElement>();
        }

        // ---- Option 2A ----
        private NativeList<int2> AssignSlices2A(
            int configCount,
            ref ComponentTypeHandle<AoeActiveTag> activeHandle,
            out int claimed)
        {
            using NativeArray<ArchetypeChunk> chunks = deadSlots.ToArchetypeChunkArray(Allocator.Temp);
            using NativeArray<int> filteredChunkIndexes = deadSlots.CalculateFilteredChunkIndexArray(Allocator.Temp);
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

        private static int CountDisabled(ArchetypeChunk chunk, ref ComponentTypeHandle<AoeActiveTag> handle)
        {
            EnabledMask mask = chunk.GetEnabledMask(ref handle);
            int count = 0;
            for (int i = 0; i < chunk.Count; i++)
                if (!mask[i]) count++;
            return count;
        }

        /* ---- Option 2B: counting job + prefix sum (commented out — swap with 2A to compare) ----

        private JobHandle ScheduleCountJob2B(
            ref ComponentTypeHandle<AoeActiveTag> activeHandle,
            out NativeList<int> deadCounts)
        {
            int chunkCount = deadSlots.CalculateChunkCount();
            deadCounts = new NativeList<int>(chunkCount, Allocator.TempJob);
            for (int i = 0; i < chunkCount; i++) deadCounts.Add(0);
            return new CountDeadJob
            {
                ActiveHandle = activeHandle,
                DeadCounts   = deadCounts.AsArray()
            }.ScheduleParallel(deadSlots, default);
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
            [ReadOnly] public ComponentTypeHandle<AoeActiveTag> ActiveHandle;
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

        private void CreateAoeEntity(Entity scope, AoeSpawnRequestElement request, EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(archetype);
            ecb.AddSharedComponent(entity, new CombatRenderScope { Scope = scope });
            ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = request.TypeId });
            RecordAoeReset(ecb, entity, scope, request);
        }

        private static void RecordAoeReset(EntityCommandBuffer ecb, Entity entity, Entity scope, AoeSpawnRequestElement request)
        {
            CombatKinematicsComponent kinematics = KinematicsFor(request);
            CombatRenderComponent render = request.Render;
            ecb.SetComponent(entity, IdentityFor(scope, request));
            ecb.SetComponent(entity, kinematics);
            ecb.SetComponent(entity, CollisionFor(request));
            ecb.SetComponent(entity, LifetimeFor(request));
            ecb.SetComponent(entity, HitGateFor(request));
            ecb.SetComponent(entity, HitSpawnFor(request));
            ecb.SetComponent(entity, AreaFor(request));
            ecb.SetComponent(entity, PulseVfxFor(request));
            ecb.SetComponent(entity, render);
            ecb.SetComponent(entity, CombatRenderMatrixUtility.ElementFor(kinematics, render));
            ecb.SetComponentEnabled<AoeActiveTag>(entity, true);
            ecb.SetComponentEnabled<AoeCollisionActiveTag>(entity, NeedsCollision(request));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static bool NeedsCollision(AoeSpawnRequestElement request) =>
            request.HitPayload.DirectDamageEnabled || request.HitPayload.StackEffect.Enabled;

        private static AoeIdentityComponent IdentityFor(Entity scope, AoeSpawnRequestElement request) =>
            new AoeIdentityComponent { Scope = scope, AoeId = request.AoeId, TypeId = request.TypeId };

        private static CombatKinematicsComponent KinematicsFor(AoeSpawnRequestElement request) =>
            new CombatKinematicsComponent { Position = request.Position, Velocity = default };

        private static CombatCollisionComponent CollisionFor(AoeSpawnRequestElement request) =>
            new CombatCollisionComponent
            {
                ShapeType       = request.ShapeType,
                Radius          = request.Radius,
                HalfExtents     = request.HalfExtents,
                RotationRadians = request.RotationRadians,
                BoundsMin       = request.BoundsMin,
                BoundsMax       = request.BoundsMax
            };

        private static AoeLifetimeComponent LifetimeFor(AoeSpawnRequestElement request) =>
            new AoeLifetimeComponent
            {
                RemainingLifetime = request.Lifetime,
                IsPulse           = request.Lifetime <= 0f ? 1 : 0
            };

        private static AoeHitGateComponent HitGateFor(AoeSpawnRequestElement request) =>
            new AoeHitGateComponent { RepeatHitCooldownSeconds = request.RepeatHitCooldownSeconds };

        private static AoeHitSpawnComponent HitSpawnFor(AoeSpawnRequestElement request) =>
            new AoeHitSpawnComponent { HitPayload = request.HitPayload, ProjectileBurst = request.ProjectileBurst };

        private static AoeAreaComponent AreaFor(AoeSpawnRequestElement request) =>
            new AoeAreaComponent { Size = request.AreaSize > 0f ? request.AreaSize : 1f };

        private static AoePulseVfxComponent PulseVfxFor(AoeSpawnRequestElement request)
        {
            float interval = request.RepeatHitCooldownSeconds > 0f ? request.RepeatHitCooldownSeconds : 0f;
            return new AoePulseVfxComponent { Interval = interval, RemainingInterval = interval };
        }

        [BurstCompile]
        private struct AoeSpawnJob : IJobChunk
        {
            public Entity Scope;
            [ReadOnly] public NativeArray<AoeSpawnRequestElement> Configs;
            [ReadOnly] public NativeArray<int2> Slices;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeActiveTag>           ActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeCollisionActiveTag>  CollisionActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderActiveTag>  RenderActiveHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeIdentityComponent>   IdentityHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatCollisionComponent>  CollisionHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeLifetimeComponent>   LifetimeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeHitGateComponent>    HitGateHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeHitSpawnComponent>   HitSpawnHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoeAreaComponent>       AreaHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<AoePulseVfxComponent>   PulseVfxHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderComponent>  RenderHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderElement>    RenderElementHandle;
            [NativeDisableContainerSafetyRestriction] public BufferTypeHandle<AoeContactGateElement>     ContactGateHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                int2 slice = Slices[unfilteredChunkIndex];
                if (slice.y == 0) return;

                EnabledMask activeMask         = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask renderActiveMask   = chunk.GetEnabledMask(ref RenderActiveHandle);

                NativeArray<AoeIdentityComponent>   identities  = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent>  collisions = chunk.GetNativeArray(ref CollisionHandle);
                NativeArray<AoeLifetimeComponent>   lifetimes   = chunk.GetNativeArray(ref LifetimeHandle);
                NativeArray<AoeHitGateComponent>    hitGates    = chunk.GetNativeArray(ref HitGateHandle);
                NativeArray<AoeHitSpawnComponent>   hitSpawns   = chunk.GetNativeArray(ref HitSpawnHandle);
                NativeArray<AoeAreaComponent>       areas       = chunk.GetNativeArray(ref AreaHandle);
                NativeArray<AoePulseVfxComponent>   pulseVfxs   = chunk.GetNativeArray(ref PulseVfxHandle);
                NativeArray<CombatRenderComponent>  renders     = chunk.GetNativeArray(ref RenderHandle);
                NativeArray<CombatRenderElement>    renderElems = chunk.GetNativeArray(ref RenderElementHandle);
                BufferAccessor<AoeContactGateElement> gates     = chunk.GetBufferAccessor(ref ContactGateHandle);

                int cfgIdx = slice.x;
                int cfgEnd = slice.x + slice.y;

                for (int i = 0; i < chunk.Count && cfgIdx < cfgEnd; i++)
                {
                    if (activeMask[i]) continue;

                    AoeSpawnRequestElement cfg = Configs[cfgIdx++];

                    identities[i]  = new AoeIdentityComponent
                    {
                        Scope = Scope, AoeId = cfg.AoeId, TypeId = cfg.TypeId
                    };
                    CombatKinematicsComponent kin = new CombatKinematicsComponent
                    {
                        Position = cfg.Position, Velocity = default
                    };
                    kinematics[i]  = kin;
                    collisions[i]  = new CombatCollisionComponent
                    {
                        ShapeType = cfg.ShapeType, Radius = cfg.Radius, HalfExtents = cfg.HalfExtents,
                        RotationRadians = cfg.RotationRadians, BoundsMin = cfg.BoundsMin, BoundsMax = cfg.BoundsMax
                    };
                    float isPulse  = cfg.Lifetime <= 0f ? 1f : 0f;
                    lifetimes[i]   = new AoeLifetimeComponent
                    {
                        RemainingLifetime = cfg.Lifetime, IsPulse = (int)isPulse
                    };
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
                    collisionActiveMask[i]  = NeedsCollision(cfg);
                    renderActiveMask[i]    = true;
                }
            }
        }

        private readonly struct AoeSpawnKey : IEquatable<AoeSpawnKey>
        {
            private readonly int _scopeIndex;
            private readonly int _typeId;

            public int ScopeIndex => _scopeIndex;
            public int TypeId     => _typeId;

            public AoeSpawnKey(int scopeIndex, int typeId)
            {
                _scopeIndex = scopeIndex;
                _typeId     = typeId;
            }

            public bool Equals(AoeSpawnKey other) =>
                _scopeIndex == other._scopeIndex && _typeId == other._typeId;

            public override bool Equals(object obj) => obj is AoeSpawnKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    return _scopeIndex * 397 ^ _typeId;
                }
            }
        }
    }
}
