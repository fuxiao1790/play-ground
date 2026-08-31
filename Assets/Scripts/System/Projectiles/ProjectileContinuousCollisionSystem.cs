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
using PlayGround.System.Combat.Targeted;
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
    public partial struct ProjectileContinuousCollisionSystem : ISystem
    {
        private EntityQuery activeProjectileQuery;

        public void OnCreate(ref SystemState state)
        {
            activeProjectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileContinuousTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<CombatCollisionActiveTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatHitPayload>(),
                ComponentType.ReadWrite<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                ComponentType.ReadOnly<ProjectileContinuousStepComponent>(),
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

            // The spawn lanes and hit-dispatch lane are created unconditionally by their owning
            // systems' OnCreate. Read them directly: a missing lane is a broken world and must
            // throw here, not be silently skipped.
            RefRW<ProjectileSpawnEventSingleton> projectileLane =
                SystemAPI.GetSingletonRW<ProjectileSpawnEventSingleton>();
            RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane =
                SystemAPI.GetSingletonRW<ImpactAoeSpawnEventSingleton>();
            RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane =
                SystemAPI.GetSingletonRW<LingeringAoeSpawnEventSingleton>();
            RefRW<TargetedSpawnEventSingleton> targetedLane =
                SystemAPI.GetSingletonRW<TargetedSpawnEventSingleton>();
            RefRW<CombatHitDispatchSingleton> hitDispatch =
                SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();

            var job = new ProjectileContinuousCollisionJob
            {
                SpawnTemplateDeltas = registryState.Deltas.AsParallelWriter(),
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                TargetCells = hash.ProjectileCollisionCells,
                TotalTargetCount = hash.TargetCount,
                MaxTargetRadius = hash.MaxTargetRadius,
                HitWriter = hitDispatch.ValueRO.HitQueue.AsParallelWriter(),
                ProjectileEventWriter = projectileLane.ValueRO.EventQueue.AsParallelWriter(),
                ImpactAoeEventWriter = impactAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                LingeringAoeEventWriter = lingeringAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                TargetedEventWriter = targetedLane.ValueRO.EventQueue.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);

            // The collision job writes both expansion EventQueues via ParallelWriter. Those
            // queues are read on the main thread by the expansion systems, which only complete
            // their own component-derived dependency. Forward this write job so they wait on it.
            projectileLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, collisionHandle);
            impactAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, collisionHandle);
            lingeringAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, collisionHandle);
            targetedLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(targetedLane.ValueRW.ProducerHandle, collisionHandle);
            hitDispatch.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, collisionHandle);
            RefRW<TargetSpatialHashSingleton> hashRw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            hashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(
                hashRw.ValueRW.ConsumerHandle,
                collisionHandle);

            state.Dependency = collisionHandle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileContinuousTag), typeof(Active),
            typeof(CombatCollisionActiveTag))]
        [WithDisabled(typeof(ArmingTag))]
        // TimedSpawnComponent is read only to release its template key on death. It is
        // enableable and disabled on non-timed projectiles, so it must be Present rather
        // than All or the query would drop every non-timed projectile from collision.
        [WithPresent(typeof(TimedSpawnComponent))]
        private partial struct ProjectileContinuousCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;
            public int TotalTargetCount;
            [ReadOnly] public NativeReference<float> MaxTargetRadius;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public NativeQueue<TargetedSpawnEvent>.ParallelWriter TargetedEventWriter;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter SpawnTemplateDeltas;

            private struct HitCandidate
            {
                public float T;
                public int TargetIndex;
            }

            private void Execute(
                Entity entity,
                in ProjectileIdentityComponent identity,
                in CombatHitPayload payload,
                ref CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in ProjectileContinuousStepComponent step,
                in TimedSpawnComponent timedSpawn,
                ref CombatLifetimeComponent lifetime,
                ref ProjectileHitComponent projectileHit,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                if (identity.Faction == CombatFaction.None)
                {
                    ProjectileHitEmission.Deactivate(
                        ref lifetime,
                        active,
                        arming,
                        in projectileHit,
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
                    return;
                }

                if (lifetime.Remaining <= 0f)
                {
                    ProjectileHitEmission.Deactivate(
                        ref lifetime,
                        active,
                        arming,
                        in projectileHit,
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
                    return;
                }

                // Pierce is the projectile's hit cap. It may still hit at 0; below zero is exhausted.
                if (projectileHit.PierceRemaining < 0)
                {
                    ProjectileHitEmission.Deactivate(
                        ref lifetime,
                        active,
                        arming,
                        in projectileHit,
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
                    return;
                }

                if (TotalTargetCount == 0)
                {
                    return;
                }

                float2 segmentStart = step.Origin;
                float2 segmentEnd = kinematics.Position;
                bool hasCorridor = CombatSweepMath.TryBuildTravelCorridor(
                    segmentStart,
                    segmentEnd,
                    collision.Radius,
                    collision.HalfExtents,
                    collision.RotationRadians,
                    collision.ShapeType,
                    out float2 boxCenter,
                    out float2 boxHalfExtents,
                    out float boxRotation);

                float2 queryMin = collision.BoundsMin;
                float2 queryMax = collision.BoundsMax;
                if (hasCorridor)
                {
                    CombatCollisionMath.ComputeWorldBounds(
                        boxCenter,
                        0f,
                        boxHalfExtents,
                        boxRotation,
                        CombatShapeType.Rectangle,
                        out float2 corridorMin,
                        out float2 corridorMax);
                    queryMin = math.min(queryMin, corridorMin);
                    queryMax = math.max(queryMax, corridorMax);
                }

                // Expand the discrete-plus-corridor query region by MaxTargetRadius before
                // converting to cell coordinates. Target insertion is center-cell only, so
                // this includes every target whose bounds can overlap either tested shape.
                float radiusExpansion = MaxTargetRadius.Value;
                float2 expandedQueryMin = queryMin - new float2(radiusExpansion, radiusExpansion);
                float2 expandedQueryMax = queryMax + new float2(radiusExpansion, radiusExpansion);
                int2 cellMin = CombatSpatialHash.FloorCell(expandedQueryMin, CombatSpatialHash.ProjectileCollisionCellSize);
                int2 cellMax = CombatSpatialHash.FloorCell(expandedQueryMax, CombatSpatialHash.ProjectileCollisionCellSize);

                FixedList512Bytes<HitCandidate> candidates = default;
                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    for (int cx = cellMin.x; cx <= cellMax.x; cx++)
                    {
                        long key = CombatSpatialHash.CellKey(cx, cy);
                        if (!TargetCells.TryGetFirstValue(key, out int targetIndex,
                                out NativeParallelMultiHashMapIterator<long> iterator))
                        {
                            continue;
                        }

                        do
                        {
                            if (TargetFactions[targetIndex].Value == identity.Faction)
                            {
                                continue;
                            }

                            Entity targetEntity = TargetEntities[targetIndex];
                            TargetPosition targetPosition = TargetPositions[targetIndex];
                            TargetCollisionShape target = TargetShapes[targetIndex];
                            int targetKey = ProjectileHitEmission.TargetKey(targetEntity);

                            if (ProjectileHitEmission.IsGated(contactGates, targetKey))
                            {
                                continue;
                            }

                            if (!CombatCollisionMath.BoundsIntersect(
                                    queryMin,
                                    queryMax,
                                    target.BoundsMin,
                                    target.BoundsMax))
                            {
                                continue;
                            }

                            bool hit = CombatCollisionMath.Hit(
                                kinematics.Position,
                                collision.Radius,
                                collision.HalfExtents,
                                collision.RotationRadians,
                                collision.ShapeType,
                                targetPosition.Value,
                                target.Radius,
                                target.HalfExtents,
                                target.RotationRadians,
                                target.ShapeType)
                                || (hasCorridor && CombatCollisionMath.Hit(
                                    boxCenter,
                                    0f,
                                    boxHalfExtents,
                                    boxRotation,
                                    CombatShapeType.Rectangle,
                                    targetPosition.Value,
                                    target.Radius,
                                    target.HalfExtents,
                                    target.RotationRadians,
                                    target.ShapeType));
                            if (!hit)
                            {
                                continue;
                            }

                            AddCandidate(
                                ref candidates,
                                new HitCandidate
                                {
                                    T = CombatSweepMath.ClosestApproachParam(
                                        segmentStart,
                                        segmentEnd,
                                        targetPosition.Value),
                                    TargetIndex = targetIndex
                                });
                        }
                        while (TargetCells.TryGetNextValue(out targetIndex, ref iterator));
                    }
                }

                SortCandidates(ref candidates);
                for (int i = 0; i < candidates.Length; i++)
                {
                    HitCandidate candidate = candidates[i];
                    Entity targetEntity = TargetEntities[candidate.TargetIndex];
                    TargetPosition targetPosition = TargetPositions[candidate.TargetIndex];
                    int targetKey = ProjectileHitEmission.TargetKey(targetEntity);
                    float2 impactPoint = math.lerp(segmentStart, segmentEnd, candidate.T);

                    ProjectileHitEmission.EnqueueHitEvent(HitWriter, entity, targetEntity, payload);
                    ProjectileHitEmission.EnqueueOnHitSpawn(
                        identity,
                        projectileHit,
                        impactPoint,
                        targetPosition.Value,
                        targetKey,
                        ProjectileEventWriter,
                        ImpactAoeEventWriter,
                        LingeringAoeEventWriter,
                        TargetedEventWriter);
                    ProjectileHitEmission.AddOrRefreshGate(
                        contactGates,
                        targetKey,
                        projectileHit.RepeatHitCooldownSeconds);

                    projectileHit.PierceRemaining--;
                    if (projectileHit.PierceRemaining < 0)
                    {
                        kinematics.Position = impactPoint;
                        ProjectileHitEmission.Deactivate(
                            ref lifetime,
                            active,
                            arming,
                            in projectileHit,
                            in timedSpawn,
                            in payload,
                            SpawnTemplateDeltas);
                        return;
                    }
                }
            }

            private static void AddCandidate(
                ref FixedList512Bytes<HitCandidate> candidates,
                in HitCandidate candidate)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i].TargetIndex == candidate.TargetIndex)
                    {
                        return;
                    }
                }

                if (candidates.Length < CollisionConstants.MaxContinuousHitsPerFrame)
                {
                    candidates.Add(candidate);
                    return;
                }

                int worstIndex = 0;
                for (int i = 1; i < candidates.Length; i++)
                {
                    if (Precedes(candidates[worstIndex], candidates[i]))
                    {
                        worstIndex = i;
                    }
                }

                if (Precedes(candidate, candidates[worstIndex]))
                {
                    candidates[worstIndex] = candidate;
                }
            }

            private static void SortCandidates(ref FixedList512Bytes<HitCandidate> candidates)
            {
                for (int i = 1; i < candidates.Length; i++)
                {
                    HitCandidate candidate = candidates[i];
                    int insertIndex = i;
                    while (insertIndex > 0 && Precedes(candidate, candidates[insertIndex - 1]))
                    {
                        candidates[insertIndex] = candidates[insertIndex - 1];
                        insertIndex--;
                    }

                    candidates[insertIndex] = candidate;
                }
            }

            private static bool Precedes(in HitCandidate left, in HitCandidate right)
            {
                return left.T < right.T || (left.T == right.T && left.TargetIndex < right.TargetIndex);
            }
        }
    }
}
