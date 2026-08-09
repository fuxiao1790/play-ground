using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Spawning
{
    public enum SpawnRejectionReason : byte
    {
        InsufficientMana = 0
    }

    // ECS Lifecycle: root-cast intent; appended by managed CombatRoot submission and consumed by ExternalSpawnGateSystem in the same simulation update.
    public struct ExternalSpawnRequest : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Hash128 TemplateKey;
        public Entity Caster;
        public float ManaCost;
        public float2 Position;
        public float2 AcquireAnchor;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int ContactGateSeedTargetId;
        public int CastToken;
    }

    public struct SpawnRejectedEvent
    {
        public Entity Caster;
        public int CastToken;
        public SpawnRejectionReason Reason;
    }

    // ECS Lifecycle: singleton rejection lane; created by ExternalSpawnGateSystem, appended during simulation,
    // drained by SpawnRejectionBridge, and disposed by the gate. UnhandledKindCount is written by the gate job
    // and checked by the gate before its next update.
    public struct SpawnRejectedSingleton : IComponentData
    {
        public NativeList<SpawnRejectedEvent> Events;
        public NativeReference<int> UnhandledKindCount;
        public JobHandle ProducerHandle;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetSpatialHashSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(TargetedSpawnExpansionSystem))]
    public partial class ExternalSpawnGateSystem : SystemBase
    {
        private Entity rejectionEntity;
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            rejectionEntity = EntityManager.CreateEntity(typeof(SpawnRejectedSingleton));
            EntityManager.SetComponentData(rejectionEntity, new SpawnRejectedSingleton
            {
                Events = new NativeList<SpawnRejectedEvent>(Allocator.Persistent),
                UnhandledKindCount = new NativeReference<int>(Allocator.Persistent)
            });
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<ExternalSpawnRequest>());
        }

        protected override void OnDestroy()
        {
            if (rejectionEntity == Entity.Null
                || !EntityManager.Exists(rejectionEntity)
                || !EntityManager.HasComponent<SpawnRejectedSingleton>(rejectionEntity))
            {
                return;
            }

            SpawnRejectedSingleton lane = EntityManager.GetComponentData<SpawnRejectedSingleton>(rejectionEntity);
            lane.ProducerHandle.Complete();
            if (lane.Events.IsCreated)
            {
                lane.Events.Dispose();
            }

            if (lane.UnhandledKindCount.IsCreated)
            {
                lane.UnhandledKindCount.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            SpawnRejectedSingleton lane = EntityManager.GetComponentData<SpawnRejectedSingleton>(rejectionEntity);
            lane.ProducerHandle.Complete();
            if (lane.UnhandledKindCount.Value != 0)
            {
                throw new global::System.InvalidOperationException("Unhandled interval child kind.");
            }

            lane.ProducerHandle = default;

            bool hasTemplates = SystemAPI.TryGetSingleton(out TargetedSpawnTemplate templates);
            bool hasHash = SystemAPI.TryGetSingletonRW<TargetSpatialHashSingleton>(
                out RefRW<TargetSpatialHashSingleton> hashRw);
            TargetSpatialHashSingleton hash = hasHash ? hashRw.ValueRO : default;
            JobHandle input = hasHash
                ? JobHandle.CombineDependencies(Dependency, hash.BuildHandle)
                : Dependency;

            NativeArray<ArchetypeChunk> scopeChunks = scopeQuery.ToArchetypeChunkArray(Allocator.TempJob);
            JobHandle gateHandle = new ExternalSpawnGateJob
            {
                ScopeChunks = scopeChunks,
                RequestHandle = GetBufferTypeHandle<ExternalSpawnRequest>(false),
                ProjectileHandle = GetBufferTypeHandle<ProjectileSpawnEvent>(false),
                ImpactAoeHandle = GetBufferTypeHandle<ImpactAoeSpawnEvent>(false),
                LingeringAoeHandle = GetBufferTypeHandle<LingeringAoeSpawnEvent>(false),
                TargetedHandle = GetBufferTypeHandle<TargetedSpawnEvent>(false),
                ManaLookup = GetComponentLookup<Mana>(false),
                TargetedTemplates = hasTemplates ? templates.Map : default,
                CanAcquireTarget = hasTemplates && hasHash ? (byte)1 : (byte)0,
                TargetEntities = hasHash ? hash.TargetEntities.AsArray() : default,
                TargetPositions = hasHash ? hash.TargetPositions.AsArray() : default,
                TargetShapes = hasHash ? hash.TargetShapes.AsArray() : default,
                TargetFactions = hasHash ? hash.TargetFactions.AsArray() : default,
                AoeOccupiedCells = hasHash ? hash.AoeOccupiedCells : default,
                Rejections = lane.Events,
                UnhandledKindCount = lane.UnhandledKindCount
            }.Schedule(input);

            if (hasHash)
            {
                hashRw.ValueRW.ConsumerHandle =
                    JobHandle.CombineDependencies(hashRw.ValueRO.ConsumerHandle, gateHandle);
            }

            lane.ProducerHandle = gateHandle;
            EntityManager.SetComponentData(rejectionEntity, lane);
            Dependency = scopeChunks.Dispose(gateHandle);
        }

        [BurstCompile]
        private struct ExternalSpawnGateJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> ScopeChunks;
            public BufferTypeHandle<ExternalSpawnRequest> RequestHandle;
            public BufferTypeHandle<ProjectileSpawnEvent> ProjectileHandle;
            public BufferTypeHandle<ImpactAoeSpawnEvent> ImpactAoeHandle;
            public BufferTypeHandle<LingeringAoeSpawnEvent> LingeringAoeHandle;
            public BufferTypeHandle<TargetedSpawnEvent> TargetedHandle;
            public ComponentLookup<Mana> ManaLookup;
            [ReadOnly] public NativeHashMap<Hash128, TargetedSpawnCommand> TargetedTemplates;
            public byte CanAcquireTarget;
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> AoeOccupiedCells;
            public NativeList<SpawnRejectedEvent> Rejections;
            public NativeReference<int> UnhandledKindCount;

            public void Execute()
            {
                TargetedAcquisition.Snapshot snapshot = new(
                    TargetEntities,
                    TargetPositions,
                    TargetShapes,
                    TargetFactions,
                    AoeOccupiedCells);

                for (int chunkIndex = 0; chunkIndex < ScopeChunks.Length; chunkIndex++)
                {
                    ArchetypeChunk chunk = ScopeChunks[chunkIndex];
                    BufferAccessor<ExternalSpawnRequest> requests =
                        chunk.GetBufferAccessor(ref RequestHandle);
                    BufferAccessor<ProjectileSpawnEvent> projectiles =
                        chunk.GetBufferAccessor(ref ProjectileHandle);
                    BufferAccessor<ImpactAoeSpawnEvent> impactAoes =
                        chunk.GetBufferAccessor(ref ImpactAoeHandle);
                    BufferAccessor<LingeringAoeSpawnEvent> lingeringAoes =
                        chunk.GetBufferAccessor(ref LingeringAoeHandle);
                    BufferAccessor<TargetedSpawnEvent> targeted =
                        chunk.GetBufferAccessor(ref TargetedHandle);

                    for (int scopeIndex = 0; scopeIndex < requests.Length; scopeIndex++)
                    {
                        DynamicBuffer<ExternalSpawnRequest> scopeRequests = requests[scopeIndex];
                        DynamicBuffer<ProjectileSpawnEvent> projectileEvents = projectiles[scopeIndex];
                        DynamicBuffer<ImpactAoeSpawnEvent> impactAoeEvents = impactAoes[scopeIndex];
                        DynamicBuffer<LingeringAoeSpawnEvent> lingeringAoeEvents = lingeringAoes[scopeIndex];
                        DynamicBuffer<TargetedSpawnEvent> targetedEvents = targeted[scopeIndex];

                        for (int requestIndex = 0; requestIndex < scopeRequests.Length; requestIndex++)
                        {
                            ExternalSpawnRequest request = scopeRequests[requestIndex];
                            if (TrySpendMana(request.Caster, request.ManaCost))
                            {
                                AppendInternalSpawn(
                                    in request,
                                    in snapshot,
                                    ref projectileEvents,
                                    ref impactAoeEvents,
                                    ref lingeringAoeEvents,
                                    ref targetedEvents);
                            }
                            else
                            {
                                Rejections.Add(new SpawnRejectedEvent
                                {
                                    Caster = request.Caster,
                                    CastToken = request.CastToken,
                                    Reason = SpawnRejectionReason.InsufficientMana
                                });
                            }
                        }

                        scopeRequests.Clear();
                    }
                }
            }

            private bool TrySpendMana(Entity caster, float requestedCost)
            {
                if (caster == Entity.Null || !ManaLookup.HasComponent(caster))
                {
                    return true;
                }

                Mana mana = ManaLookup[caster];
                float cost = math.max(0f, requestedCost);
                if (mana.Current < cost)
                {
                    return false;
                }

                mana.Current -= cost;
                ManaLookup[caster] = mana;
                return true;
            }

            private void AppendInternalSpawn(
                in ExternalSpawnRequest request,
                in TargetedAcquisition.Snapshot snapshot,
                ref DynamicBuffer<ProjectileSpawnEvent> projectileEvents,
                ref DynamicBuffer<ImpactAoeSpawnEvent> impactAoeEvents,
                ref DynamicBuffer<LingeringAoeSpawnEvent> lingeringAoeEvents,
                ref DynamicBuffer<TargetedSpawnEvent> targetedEvents)
            {
                if (request.Kind == IntervalChildKind.Targeted)
                {
                    float2 acquireAnchor = math.all(request.AcquireAnchor == default)
                        ? request.Position
                        : request.AcquireAnchor;
                    byte hasAcquiredTarget = 0;
                    if (CanAcquireTarget != 0
                        && TargetedTemplates.TryGetValue(request.TemplateKey, out TargetedSpawnCommand command)
                        && TargetedAcquisition.TryNearestHostile(
                            snapshot,
                            acquireAnchor,
                            command.Resolve.ChainDistance,
                            request.Faction,
                            out _,
                            out float2 acquiredPosition))
                    {
                        acquireAnchor = acquiredPosition;
                        hasAcquiredTarget = 1;
                    }

                    targetedEvents.Add(new TargetedSpawnEvent
                    {
                        Kind = IntervalChildKind.Targeted,
                        TemplateKey = request.TemplateKey,
                        Position = request.Position,
                        AcquireAnchor = acquireAnchor,
                        HasAcquiredTarget = hasAcquiredTarget,
                        AimDirection = request.AimDirection,
                        Faction = request.Faction,
                        SourceId = request.SourceId,
                        JitterSeed = request.JitterSeed,
                        ContactGateSeedTargetId = request.ContactGateSeedTargetId
                    });
                    return;
                }

                if (request.Kind == IntervalChildKind.Projectile)
                {
                    projectileEvents.Add(new ProjectileSpawnEvent
                    {
                        Kind = IntervalChildKind.Projectile,
                        TemplateKey = request.TemplateKey,
                        Position = request.Position,
                        AimDirection = request.AimDirection,
                        Faction = request.Faction,
                        SourceId = request.SourceId,
                        JitterSeed = request.JitterSeed,
                        ContactGateSeedTargetId = request.ContactGateSeedTargetId
                    });
                    return;
                }

                if (request.Kind == IntervalChildKind.LingeringAoe)
                {
                    lingeringAoeEvents.Add(new LingeringAoeSpawnEvent
                    {
                        Kind = IntervalChildKind.LingeringAoe,
                        TemplateKey = request.TemplateKey,
                        Position = request.Position,
                        AimDirection = request.AimDirection,
                        Faction = request.Faction,
                        SourceId = request.SourceId,
                        JitterSeed = request.JitterSeed,
                        ContactGateSeedTargetId = request.ContactGateSeedTargetId
                    });
                    return;
                }

                if (request.Kind == IntervalChildKind.ImpactAoe)
                {
                    impactAoeEvents.Add(new ImpactAoeSpawnEvent
                    {
                        Kind = IntervalChildKind.ImpactAoe,
                        TemplateKey = request.TemplateKey,
                        Position = request.Position,
                        AimDirection = request.AimDirection,
                        Faction = request.Faction,
                        SourceId = request.SourceId,
                        JitterSeed = request.JitterSeed,
                        ContactGateSeedTargetId = request.ContactGateSeedTargetId
                    });
                    return;
                }

                UnhandledKindCount.Value++;
            }
        }
    }
}
