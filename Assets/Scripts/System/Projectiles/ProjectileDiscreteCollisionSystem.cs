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
    public partial struct ProjectileDiscreteCollisionSystem : ISystem
    {
        private EntityQuery activeProjectileQuery;

        public void OnCreate(ref SystemState state)
        {
            activeProjectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.Exclude<ProjectileContinuousTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<CombatCollisionActiveTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatHitPayload>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
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

            // The hit-dispatch lane is created unconditionally by its owning system's OnCreate.
            // Read it directly: a missing lane is a broken world and must throw here, not be
            // silently skipped.
            RefRW<CombatHitDispatchSingleton> hitDispatch =
                SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();

            var job = new ProjectileCollisionJob
            {
                SpawnTemplateDeltas = registryState.Deltas.AsParallelWriter(),
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                TargetCells = hash.ProjectileCollisionCells,
                TotalTargetCount = hash.TargetCount,
                MaxTargetRadius = hash.MaxTargetRadius,
                HitWriter = hitDispatch.ValueRO.HitQueue.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);

            hitDispatch.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, collisionHandle);
            RefRW<TargetSpatialHashSingleton> hashRw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            hashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(
                hashRw.ValueRW.ConsumerHandle,
                collisionHandle);

            state.Dependency = collisionHandle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(CombatCollisionActiveTag))]
        [WithNone(typeof(ProjectileContinuousTag))]
        [WithDisabled(typeof(ArmingTag))]
        // TimedSpawnComponent is read only to release its template key on death. It is
        // enableable and disabled on non-timed projectiles, so it must be Present rather
        // than All or the query would drop every non-timed projectile from collision.
        [WithPresent(typeof(TimedSpawnComponent))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;
            public int TotalTargetCount;
            [ReadOnly] public NativeReference<float> MaxTargetRadius;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter SpawnTemplateDeltas;

            private void Execute(
                Entity entity,
                in ProjectileIdentityComponent identity,
                in CombatHitPayload payload,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
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
                        in timedSpawn,
                        in payload,
                        SpawnTemplateDeltas);
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
                int2 cellMin = CombatSpatialHash.FloorCell(queryMin, CombatSpatialHash.ProjectileCollisionCellSize);
                int2 cellMax = CombatSpatialHash.FloorCell(queryMax, CombatSpatialHash.ProjectileCollisionCellSize);

                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    for (int cx = cellMin.x; cx <= cellMax.x; cx++)
                    {
                        long key = CombatSpatialHash.CellKey(cx, cy);
                        if (!TargetCells.TryGetFirstValue(key, out int targetIdx, out NativeParallelMultiHashMapIterator<long> iterator))
                        {
                            continue;
                        }

                        do
                        {
                            TargetFaction candidateFaction = TargetFactions[targetIdx];
                            if (!TargetFaction.CanHit(identity.Faction, in candidateFaction))
                            {
                                continue;
                            }

                            Entity targetEntity = TargetEntities[targetIdx];
                            TargetPosition targetPosition = TargetPositions[targetIdx];
                            TargetCollisionShape target = TargetShapes[targetIdx];
                            int targetKey = ProjectileHitEmission.TargetKey(targetEntity);

                            if (ProjectileHitEmission.IsGated(contactGates, targetKey))
                            {
                                continue;
                            }

                            if (!CombatCollisionMath.BoundsIntersect(
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

                            ProjectileHitEmission.EnqueueHitEvent(HitWriter, entity, targetEntity, payload);

                            ProjectileHitEmission.AddOrRefreshGate(contactGates, targetKey,
                                projectileHit.RepeatHitCooldownSeconds);

                            projectileHit.PierceRemaining--;
                            if (projectileHit.PierceRemaining < 0)
                            {
                                ProjectileHitEmission.Deactivate(
                                    ref lifetime,
                                    active,
                                    arming,
                                    in timedSpawn,
                                    in payload,
                                    SpawnTemplateDeltas);
                                return;
                            }
                        }
                        while (TargetCells.TryGetNextValue(out targetIdx, ref iterator));
                    }
                }
            }

        }

    }

    internal static class ProjectileHitEmission
    {
        private const int ImpactAoeIdSalt = 0x5F1A0E;
        private const int ImpactProjectileIdSalt = 0x2C1297;

        internal static void EnqueueHitEvent(
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            Entity source,
            Entity target,
            in CombatHitPayload payload)
        {
            if (!HasHitEvent(payload))
            {
                return;
            }

            hitWriter.Enqueue(new CombatHitEvent
            {
                Source = source,
                Target = target
            });
        }

        // Single projectile death funnel for both collision lanes. Despawn emits a release
        // event for every template key the entity carries; it never touches a reference count.
        internal static void Deactivate(
            ref CombatLifetimeComponent lifetime,
            EnabledRefRW<Active> active,
            EnabledRefRW<ArmingTag> arming,
            in TimedSpawnComponent timedSpawn,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            lifetime.Remaining = 0f;
            CombatDeathUtility.Kill(active, arming);
            SpawnTemplateRefEmit.ReleaseProjectile(in timedSpawn, in payload, deltas);
        }

        internal static bool IsGated(DynamicBuffer<ProjectileContactGateElement> contactGates, int targetId)
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

        internal static void AddOrRefreshGate(
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

        internal static bool HasHitEvent(in CombatHitPayload payload) =>
            payload.DirectDamageEnabled || payload.StackEffect.Enabled;

        internal static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        internal static float2 DirectionFromTo(float2 from, float2 to, bool invert)
        {
            float2 toTarget = to - from;
            if (math.lengthsq(toTarget) <= 0.0001f)
            {
                return new float2(1f, 0f);
            }

            float2 direction = math.normalize(toTarget);
            return invert ? -direction : direction;
        }

        internal static int HashId(int a, int b, int c, int salt)
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
