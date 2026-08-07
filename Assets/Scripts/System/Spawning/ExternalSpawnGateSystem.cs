using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
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

    // ECS Lifecycle: singleton rejection lane; created by ExternalSpawnGateSystem, appended during simulation, drained by SpawnRejectionBridge, disposed by the gate.
    public struct SpawnRejectedSingleton : IComponentData
    {
        public NativeList<SpawnRejectedEvent> Events;
        public JobHandle ProducerHandle;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(TargetedSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringTargetedSpawnExpansionSystem))]
    public partial class ExternalSpawnGateSystem : SystemBase
    {
        private Entity rejectionEntity;
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            rejectionEntity = EntityManager.CreateEntity(typeof(SpawnRejectedSingleton));
            EntityManager.SetComponentData(rejectionEntity, new SpawnRejectedSingleton
            {
                Events = new NativeList<SpawnRejectedEvent>(Allocator.Persistent)
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
        }

        protected override void OnUpdate()
        {
            SpawnRejectedSingleton lane = EntityManager.GetComponentData<SpawnRejectedSingleton>(rejectionEntity);
            lane.ProducerHandle.Complete();
            lane.ProducerHandle = default;

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<ExternalSpawnRequest> requests = EntityManager.GetBuffer<ExternalSpawnRequest>(scope);
                for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                {
                    ExternalSpawnRequest request = requests[requestIndex];
                    if (TrySpendMana(request.Caster, request.ManaCost))
                    {
                        AppendInternalSpawn(scope, request);
                    }
                    else
                    {
                        lane.Events.Add(new SpawnRejectedEvent
                        {
                            Caster = request.Caster,
                            CastToken = request.CastToken,
                            Reason = SpawnRejectionReason.InsufficientMana
                        });
                    }
                }

                requests.Clear();
            }

            EntityManager.SetComponentData(rejectionEntity, lane);
        }

        private bool TrySpendMana(Entity caster, float requestedCost)
        {
            if (caster == Entity.Null
                || !EntityManager.Exists(caster)
                || !EntityManager.HasComponent<Mana>(caster))
            {
                return true;
            }

            Mana mana = EntityManager.GetComponentData<Mana>(caster);
            float cost = math.max(0f, requestedCost);
            if (mana.Current < cost)
            {
                return false;
            }

            mana.Current -= cost;
            EntityManager.SetComponentData(caster, mana);
            return true;
        }

        private void AppendInternalSpawn(Entity scope, in ExternalSpawnRequest request)
        {
            float2 acquireAnchor = math.all(request.AcquireAnchor == default)
                ? request.Position
                : request.AcquireAnchor;

            if (request.Kind == IntervalChildKind.Targeted)
            {
                EntityManager.GetBuffer<TargetedSpawnEvent>(scope).Add(new TargetedSpawnEvent
                {
                    Kind = IntervalChildKind.Targeted,
                    TemplateKey = request.TemplateKey,
                    Position = request.Position,
                    AcquireAnchor = acquireAnchor,
                    AimDirection = request.AimDirection,
                    Faction = request.Faction,
                    SourceId = request.SourceId,
                    JitterSeed = request.JitterSeed,
                    ContactGateSeedTargetId = request.ContactGateSeedTargetId
                });
                return;
            }

            if (request.Kind == IntervalChildKind.LingeringTargeted)
            {
                EntityManager.GetBuffer<LingeringTargetedSpawnEvent>(scope).Add(new LingeringTargetedSpawnEvent
                {
                    Kind = IntervalChildKind.LingeringTargeted,
                    TemplateKey = request.TemplateKey,
                    Position = request.Position,
                    AcquireAnchor = acquireAnchor,
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
                EntityManager.GetBuffer<ProjectileSpawnEvent>(scope).Add(new ProjectileSpawnEvent
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
                EntityManager.GetBuffer<LingeringAoeSpawnEvent>(scope).Add(new LingeringAoeSpawnEvent
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
                EntityManager.GetBuffer<ImpactAoeSpawnEvent>(scope).Add(new ImpactAoeSpawnEvent
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

            throw new global::System.InvalidOperationException(
                $"Unhandled interval child kind {request.Kind}.");
        }
    }
}
