using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Projectiles
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileContactGateSystem))]
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        private EntityQuery activeProjectileQuery;

        public void OnCreate(ref SystemState state)
        {
            activeProjectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<CombatCollisionActiveTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                ComponentType.ReadOnly<CombatRenderAuthoring>(),
                ComponentType.ReadWrite<CombatLifetimeComponent>(),
                ComponentType.ReadWrite<ProjectileHitComponent>(),
                ComponentType.ReadWrite<ProjectileContactGateElement>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (activeProjectileQuery.IsEmpty)
            {
                return;
            }

            TargetSpatialHashSingleton hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            bool hasProjectileEvents = SystemAPI.TryGetSingletonRW<ProjectileSpawnEventSingleton>(
                out RefRW<ProjectileSpawnEventSingleton> projectileLane);
            NativeQueue<ProjectileSpawnEvent> projectileEventQueue =
                hasProjectileEvents ? projectileLane.ValueRO.EventQueue : default;
            hasProjectileEvents = hasProjectileEvents && projectileEventQueue.IsCreated;

            bool hasImpactAoeEvents = SystemAPI.TryGetSingletonRW<ImpactAoeSpawnEventSingleton>(
                out RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane);
            NativeQueue<ImpactAoeSpawnEvent> impactAoeEventQueue =
                hasImpactAoeEvents ? impactAoeLane.ValueRO.EventQueue : default;
            hasImpactAoeEvents = hasImpactAoeEvents && impactAoeEventQueue.IsCreated;

            bool hasLingeringAoeEvents = SystemAPI.TryGetSingletonRW<LingeringAoeSpawnEventSingleton>(
                out RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane);
            NativeQueue<LingeringAoeSpawnEvent> lingeringAoeEventQueue =
                hasLingeringAoeEvents ? lingeringAoeLane.ValueRO.EventQueue : default;
            hasLingeringAoeEvents = hasLingeringAoeEvents && lingeringAoeEventQueue.IsCreated;

            bool hasHit = SystemAPI.TryGetSingletonRW<CombatHitDispatchSingleton>(
                out RefRW<CombatHitDispatchSingleton> hitDispatch);
            NativeQueue<CombatHitEvent> hitQueue = hasHit ? hitDispatch.ValueRO.HitQueue : default;
            hasHit = hasHit && hitQueue.IsCreated;

            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatVfxDispatchSingleton>(
                out RefRW<CombatVfxDispatchSingleton> vfx);
            NativeQueue<VfxPendingSpawn> vfxQueue = hasVfx ? vfx.ValueRO.PendingSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;
            var job = new ProjectileCollisionJob
            {
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                TargetCells = hash.ProjectileCollisionCells,
                TotalTargetCount = hash.TargetCount,
                MaxTargetRadius = hash.MaxTargetRadius,
                HitWriter = hasHit
                    ? hitQueue.AsParallelWriter()
                    : default,
                HasHitWriter = hasHit,
                VfxPending = hasVfx
                    ? vfxQueue.AsParallelWriter()
                    : default,
                HasVfxWriter = hasVfx,
                ProjectileEventWriter = hasProjectileEvents
                    ? projectileEventQueue.AsParallelWriter()
                    : default,
                HasProjectileEventWriter = hasProjectileEvents,
                ImpactAoeEventWriter = hasImpactAoeEvents
                    ? impactAoeEventQueue.AsParallelWriter()
                    : default,
                LingeringAoeEventWriter = hasLingeringAoeEvents
                    ? lingeringAoeEventQueue.AsParallelWriter()
                    : default,
                HasImpactAoeEventWriter = hasImpactAoeEvents,
                HasLingeringAoeEventWriter = hasLingeringAoeEvents
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);

            // The collision job writes both expansion EventQueues via ParallelWriter. Those
            // queues are read on the main thread by the expansion systems, which only complete
            // their own component-derived dependency. Forward this write job so they wait on it.
            if (hasProjectileEvents)
                projectileLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, collisionHandle);
            if (hasImpactAoeEvents)
                impactAoeLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, collisionHandle);
            if (hasLingeringAoeEvents)
                lingeringAoeLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, collisionHandle);
            if (hasHit)
                hitDispatch.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, collisionHandle);
            if (hasVfx)
                vfx.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, collisionHandle);

            RefRW<TargetSpatialHashSingleton> hashRw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            hashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(
                hashRw.ValueRW.ConsumerHandle,
                collisionHandle);

            state.Dependency = collisionHandle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(CombatCollisionActiveTag))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            private const int ImpactAoeIdSalt = 0x5F1A0E;
            private const int ImpactProjectileIdSalt = 0x2C1297;

            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;
            public int TotalTargetCount;
            [ReadOnly] public NativeReference<float> MaxTargetRadius;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public bool HasHitWriter;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public bool HasProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public bool HasImpactAoeEventWriter;
            public bool HasLingeringAoeEventWriter;

            private void Execute(
                Entity entity,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in CombatRenderAuthoring authoring,
                ref CombatLifetimeComponent lifetime,
                ref ProjectileHitComponent projectileHit,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                float areaSize = math.max(authoring.VisualScale.x, authoring.VisualScale.y);
                if (identity.Faction == CombatFaction.None)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, arming, VfxPending, HasVfxWriter);
                    return;
                }

                if (lifetime.Remaining <= 0f)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, arming, VfxPending, HasVfxWriter);
                    return;
                }

                // Pierce is the projectile's hit cap. It may still hit at 0; below zero is exhausted.
                if (projectileHit.PierceRemaining < 0)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, arming, VfxPending, HasVfxWriter);
                    return;
                }

                if (TotalTargetCount == 0)
                {
                    return;
                }

                // Expand the projectile's AABB by MaxTargetRadius before converting to cell
                // coordinates. Any target whose center falls within this expanded region is
                // guaranteed to have its center cell included in the query, so no miss is
                // possible at cell boundaries regardless of how large a target is.
                float radiusExpansion = MaxTargetRadius.Value;
                float2 queryMin = collision.BoundsMin - new float2(radiusExpansion, radiusExpansion);
                float2 queryMax = collision.BoundsMax + new float2(radiusExpansion, radiusExpansion);
                int2 cellMin = CombatSpatialHash.FloorCell(
                    queryMin,
                    CombatSpatialHash.ProjectileCollisionCellSize);
                int2 cellMax = CombatSpatialHash.FloorCell(
                    queryMax,
                    CombatSpatialHash.ProjectileCollisionCellSize);

                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    for (int cx = cellMin.x; cx <= cellMax.x; cx++)
                    {
                        long key = CombatSpatialHash.CellKey(cx, cy);
                        if (!TargetCells.TryGetFirstValue(key, out int targetIdx,
                            out NativeParallelMultiHashMapIterator<long> iterator))
                        {
                            continue;
                        }

                        do
                        {
                            if (TargetFactions[targetIdx].Value == identity.Faction)
                            {
                                continue;
                            }

                            Entity targetEntity = TargetEntities[targetIdx];
                            TargetPosition targetPosition = TargetPositions[targetIdx];
                            TargetCollisionShape target = TargetShapes[targetIdx];
                            int targetKey = TargetKey(targetEntity);

                            if (IsGated(contactGates, targetKey))
                            {
                                continue;
                            }

                            if (!ProjectileCollisionMath.BoundsIntersect(
                                collision.BoundsMin,
                                collision.BoundsMax,
                                target.BoundsMin,
                                target.BoundsMax))
                            {
                                continue;
                            }

                            if (!CombatCollisionMath.Hit(
                                    kinematics.Position,
                                    collision.Radius,
                                    collision.HalfExtents,
                                    collision.RotationRadians,
                                    collision.ShapeType,
                                    targetPosition.Value,
                                    target.Radius,
                                    target.HalfExtents,
                                    target.RotationRadians,
                                    target.ShapeType))
                            {
                                continue;
                            }

                            if (HasHitWriter && HasHitEvent(projectileHit.HitPayload))
                            {
                                HitWriter.Enqueue(new CombatHitEvent
                                {
                                    TargetProxy = targetEntity,
                                    HitPosition = kinematics.Position,
                                    Kind = CombatHitKind.Projectile,
                                    DamageAmount = projectileHit.HitPayload.DamageAmount,
                                    CritChance = projectileHit.HitPayload.CritChance,
                                    CritMultiplier = projectileHit.HitPayload.CritMultiplier,
                                    DirectDamageEnabled = projectileHit.HitPayload.DirectDamageEnabled,
                                    SourceNodeId = projectileHit.HitPayload.SourceNodeId,
                                    SourceId = identity.ProjectileId,
                                    TypeId = identity.TypeId,
                                    StackEffect = projectileHit.HitPayload.StackEffect
                                });
                            }

                            if (HasProjectileEventWriter
                                && projectileHit.HitPayload.OnHitSpawn.Enabled
                                && projectileHit.HitPayload.OnHitSpawn.Kind == IntervalChildKind.Projectile)
                            {
                                int baseId = HashId(
                                    identity.ProjectileId,
                                    identity.TypeId,
                                    targetKey,
                                    ImpactProjectileIdSalt);
                                ProjectileEventWriter.Enqueue(new ProjectileSpawnEvent
                                {
                                    Kind = projectileHit.HitPayload.OnHitSpawn.Kind,
                                    TemplateKey = projectileHit.HitPayload.OnHitSpawn.TemplateKey,
                                    Faction = identity.Faction,
                                    Position = kinematics.Position,
                                    AimDirection = DirectionFromTo(
                                        kinematics.Position,
                                        targetPosition.Value,
                                        invert: true),
                                    SourceId = baseId,
                                    JitterSeed = (uint)baseId * 2654435761u,
                                    ContactGateSeedTargetId = targetKey
                                });
                            }

                            if (projectileHit.HitPayload.OnHitSpawn.Enabled
                                && (projectileHit.HitPayload.OnHitSpawn.Kind == IntervalChildKind.ImpactAoe
                                    || projectileHit.HitPayload.OnHitSpawn.Kind == IntervalChildKind.LingeringAoe))
                            {
                                int aoeId = HashId(
                                    identity.ProjectileId,
                                    identity.TypeId,
                                    targetKey,
                                    ImpactAoeIdSalt);
                                if (projectileHit.HitPayload.OnHitSpawn.Kind == IntervalChildKind.LingeringAoe)
                                {
                                    if (HasLingeringAoeEventWriter)
                                    {
                                        LingeringAoeEventWriter.Enqueue(new LingeringAoeSpawnEvent
                                        {
                                            Kind = projectileHit.HitPayload.OnHitSpawn.Kind,
                                            TemplateKey = projectileHit.HitPayload.OnHitSpawn.TemplateKey,
                                            Faction = identity.Faction,
                                            Position = kinematics.Position,
                                            SourceId = aoeId,
                                            JitterSeed = (uint)aoeId * 2654435761u,
                                            ContactGateSeedTargetId = targetKey
                                        });
                                    }
                                }
                                else if (HasImpactAoeEventWriter)
                                {
                                    ImpactAoeEventWriter.Enqueue(new ImpactAoeSpawnEvent
                                    {
                                        Kind = projectileHit.HitPayload.OnHitSpawn.Kind,
                                        TemplateKey = projectileHit.HitPayload.OnHitSpawn.TemplateKey,
                                        Faction = identity.Faction,
                                        Position = kinematics.Position,
                                        SourceId = aoeId,
                                        JitterSeed = (uint)aoeId * 2654435761u,
                                        ContactGateSeedTargetId = targetKey
                                    });
                                }
                            }

                            if (HasVfxWriter)
                            {
                                VfxPending.Enqueue(new VfxPendingSpawn
                                {
                                    TypeId = identity.TypeId,
                                    Trigger = 1,
                                    Position = kinematics.Position,
                                    AreaSize = areaSize
                                });
                            }

                            AddOrRefreshGate(contactGates, targetKey,
                                projectileHit.RepeatHitCooldownSeconds);

                            projectileHit.PierceRemaining--;
                            if (projectileHit.PierceRemaining < 0)
                            {
                                Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, arming, VfxPending, HasVfxWriter);
                                return;
                            }
                        }
                        while (TargetCells.TryGetNextValue(out targetIdx, ref iterator));
                    }
                }
            }

            private void Deactivate(
                ProjectileIdentityComponent identity,
                float2 position,
                float areaSize,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming,
                NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
                bool hasVfxWriter)
            {
                lifetime.Remaining = 0f;
                CombatDeathUtility.Kill(
                    active,
                    arming,
                    vfxPending,
                    hasVfxWriter,
                    identity.TypeId,
                    position,
                    areaSize);
            }

            private static bool IsGated(DynamicBuffer<ProjectileContactGateElement> contactGates, int targetId)
            {
                for (int i = 0; i < contactGates.Length; i++)
                {
                    if (contactGates[i].TargetId == targetId)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static void AddOrRefreshGate(
                DynamicBuffer<ProjectileContactGateElement> contactGates,
                int targetId,
                float cooldownSeconds)
            {
                if (cooldownSeconds <= 0f)
                {
                    cooldownSeconds = float.MaxValue;
                }

                for (int i = 0; i < contactGates.Length; i++)
                {
                    ProjectileContactGateElement gate = contactGates[i];
                    if (gate.TargetId != targetId)
                    {
                        continue;
                    }

                    gate.CooldownRemaining = cooldownSeconds;
                    contactGates[i] = gate;
                    return;
                }

                contactGates.Add(new ProjectileContactGateElement
                {
                    TargetId = targetId,
                    CooldownRemaining = cooldownSeconds
                });
            }

            private static bool HasHitEvent(in ProjectileHitPayload payload) =>
                payload.DirectDamageEnabled || payload.StackEffect.Enabled;

            private static int TargetKey(Entity entity)
            {
                unchecked
                {
                    int key = ((entity.Index + 1) * 397) ^ entity.Version;
                    key &= 0x7fffffff;
                    return key == 0 ? 1 : key;
                }
            }

            private static float2 DirectionFromTo(float2 from, float2 to, bool invert)
            {
                float2 toTarget = to - from;
                if (math.lengthsq(toTarget) <= 0.0001f)
                {
                    return new float2(1f, 0f);
                }

                float2 dir = math.normalize(toTarget);
                return invert ? -dir : dir;
            }

            private static int HashId(int a, int b, int c, int salt)
            {
                unchecked
                {
                    int hash = salt;
                    hash = (hash * 397) ^ a;
                    hash = (hash * 397) ^ b;
                    hash = (hash * 397) ^ c;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }
        }

    }
}
