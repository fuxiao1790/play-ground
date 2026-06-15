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
    [UpdateAfter(typeof(AoeCollisionSystem))]
    [UpdateAfter(typeof(AoeLifetimeSystem))]
    // Projectile collisions convert impact AOEs into AoeSpawnRequestElement this
    // frame; run after so those impact AOEs spawn same-frame.
    [UpdateAfter(typeof(PlayGround.System.Projectile.ProjectileCollisionSystem))]
    [UpdateBefore(typeof(PlayGround.System.Common.CombatRenderPrepareSystem))]
    public partial class AoeSpawnSystem : SystemBase
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
        private EntityQuery scopeQuery;

        private readonly Dictionary<AoeSpawnKey, AoeSpawnBucket> _byKey = new();
        private readonly Dictionary<AoeSpawnKey, EntityQuery> _deadSlotQueriesByKey = new();
        private readonly Dictionary<int, Entity> _scopeByIndex = new();
        private readonly List<AoeSpawnBucket> _bucketPool = new();
        private readonly List<AoeSpawnWork> _spawnWork = new();

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
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

        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            DisposeSpawnWorkReferences();
            DisposeBuckets(_byKey.Values);
            DisposeBuckets(_bucketPool);
            foreach (EntityQuery query in _deadSlotQueriesByKey.Values)
            {
                query.Dispose();
            }
            scopeQuery.Dispose();
            _byKey.Clear();
            _deadSlotQueriesByKey.Clear();
            _scopeByIndex.Clear();
            _bucketPool.Clear();
            _spawnWork.Clear();
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
                    if (!_byKey.TryGetValue(key, out AoeSpawnBucket bucket))
                    {
                        bucket = GetBucket();
                        _byKey[key] = bucket;
                    }
                    bucket.Requests.Add(req);
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
                _spawnWork.Clear();

                using (ReuseJobMarker.Auto())
                {
                    var jobHandles = new NativeList<JobHandle>(_byKey.Count, Allocator.Temp);
                    foreach (var (key, bucket) in _byKey)
                    {
                        Entity scope = _scopeByIndex[key.ScopeIndex];
                        EntityQuery query = DeadSlotQueryFor(key, scope);
                        NativeArray<AoeSpawnRequestElement> configs = bucket.Requests.AsArray();
                        var claimedReference = new NativeReference<int>(Allocator.TempJob);
                        claimedReference.Value = 0;

                        JobHandle spawnHandle = new AoeSpawnJob
                        {
                            Scope                 = scope,
                            Configs               = configs,
                            ClaimedCount          = claimedReference,
                            ActiveHandle          = GetComponentTypeHandle<AoeActiveTag>(false),
                            CollisionActiveHandle = GetComponentTypeHandle<AoeCollisionActiveTag>(false),
                            RenderActiveHandle    = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                            IdentityHandle        = GetComponentTypeHandle<AoeIdentityComponent>(false),
                            KinematicsHandle      = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                            CollisionHandle       = GetComponentTypeHandle<CombatCollisionComponent>(false),
                            LifetimeHandle        = GetComponentTypeHandle<AoeLifetimeComponent>(false),
                            HitGateHandle         = GetComponentTypeHandle<AoeHitGateComponent>(false),
                            HitSpawnHandle        = GetComponentTypeHandle<AoeHitSpawnComponent>(false),
                            AreaHandle            = GetComponentTypeHandle<AoeAreaComponent>(false),
                            PulseVfxHandle        = GetComponentTypeHandle<AoePulseVfxComponent>(false),
                            RenderHandle          = GetComponentTypeHandle<CombatRenderComponent>(false),
                            RenderElementHandle   = GetComponentTypeHandle<CombatRenderElement>(false),
                            ContactGateHandle     = GetBufferTypeHandle<AoeContactGateElement>(false),
                        }.Schedule(query, default);

                        jobHandles.Add(spawnHandle);
                        _spawnWork.Add(new AoeSpawnWork(scope, configs, claimedReference));
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
                        CreateAoeEntity(work.Scope, work.Configs[i], createEcb);
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

        private void ReturnLists()
        {
            foreach (var bucket in _byKey.Values)
            {
                bucket.Requests.Clear();
                _bucketPool.Add(bucket);
            }
            _byKey.Clear();
            _scopeByIndex.Clear();
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
            {
                bucket.Dispose();
            }
        }

        private EntityQuery DeadSlotQueryFor(AoeSpawnKey key, Entity scope)
        {
            if (!_deadSlotQueriesByKey.TryGetValue(key, out EntityQuery query))
            {
                query = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<AoeTag>()
                    .WithAll<CombatRenderScope>()
                    .WithAll<CombatRenderTypeId>()
                    .WithDisabled<AoeActiveTag>()
                    .Build(this);
                _deadSlotQueriesByKey[key] = query;
            }

            query.SetSharedComponentFilter(
                new CombatRenderScope { Scope = scope },
                new CombatRenderTypeId { TypeId = key.TypeId });
            return query;
        }

        private void DisposeSpawnWorkReferences()
        {
            foreach (AoeSpawnWork work in _spawnWork)
            {
                if (work.ClaimedCount.IsCreated)
                {
                    work.ClaimedCount.Dispose();
                }
            }
            _spawnWork.Clear();
        }

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
            request.HitPayload.DirectDamageEnabled
            || request.HitPayload.StackEffect.Enabled
            || request.ProjectileBurst.Enabled;

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
            [NativeDisableContainerSafetyRestriction] public NativeReference<int> ClaimedCount;

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
                int cfgIdx = ClaimedCount.Value;
                if (cfgIdx >= Configs.Length) return;

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

                for (int i = 0; i < chunk.Count && cfgIdx < Configs.Length; i++)
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

                ClaimedCount.Value = cfgIdx;
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

        private sealed class AoeSpawnBucket : IDisposable
        {
            public readonly NativeList<AoeSpawnRequestElement> Requests =
                new(Allocator.Persistent);

            public void Dispose()
            {
                if (Requests.IsCreated)
                {
                    Requests.Dispose();
                }
            }
        }

        private readonly struct AoeSpawnWork
        {
            public readonly Entity Scope;
            public readonly NativeArray<AoeSpawnRequestElement> Configs;
            public readonly NativeReference<int> ClaimedCount;

            public AoeSpawnWork(
                Entity scope,
                NativeArray<AoeSpawnRequestElement> configs,
                NativeReference<int> claimedCount)
            {
                Scope = scope;
                Configs = configs;
                ClaimedCount = claimedCount;
            }
        }
    }
}
