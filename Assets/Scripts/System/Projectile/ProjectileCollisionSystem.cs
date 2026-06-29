using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileContactGateSystem))]
    [UpdateBefore(typeof(PlayGround.System.Aoe.AoeContactGateSystem))]
    [UpdateBefore(typeof(CombatApplyFinalizeSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        // this depends on the arena size and mob count
        // cellSize = sqrt(arenaWidth * arenaHeight / mobCount) * ~1.5
        private const float SpatialHashCellSize = 1f;
        private EntityQuery activeProjectileQuery;
        private EntityQuery targetQuery;

        public void OnCreate(ref SystemState state)
        {
            activeProjectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<ProjectileCollisionActiveTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                ComponentType.ReadOnly<CombatRenderComponent>(),
                ComponentType.ReadWrite<CombatLifetimeComponent>(),
                ComponentType.ReadWrite<ProjectileHitComponent>(),
                ComponentType.ReadWrite<CombatRenderActiveTag>(),
                ComponentType.ReadWrite<ProjectileContactGateElement>());
            targetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TargetProxyTag>(),
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadOnly<TargetCollisionShape>(),
                ComponentType.ReadOnly<TargetFaction>());
        }

        public void OnUpdate(ref SystemState state)
        {
            int activeProjectileCount = activeProjectileQuery.CalculateEntityCount();
            if (activeProjectileCount == 0)
            {
                return;
            }

            state.EntityManager.CompleteDependencyBeforeRO<TargetPosition>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetCollisionShape>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetFaction>();

            NativeArray<Entity> targetEntities = targetQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<TargetPosition> targetPositions = targetQuery.ToComponentDataArray<TargetPosition>(Allocator.TempJob);
            NativeArray<TargetCollisionShape> targetShapes = targetQuery.ToComponentDataArray<TargetCollisionShape>(Allocator.TempJob);
            NativeArray<TargetFaction> targetFactions = targetQuery.ToComponentDataArray<TargetFaction>(Allocator.TempJob);

            // Pass 1: count targets and find the largest bounding radius.
            // Used to size the multimap and to expand the per-projectile query range
            // so targets near cell boundaries are never missed.
            int totalTargetCount = targetEntities.Length;
            float maxTargetRadius = 0f;
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape t = targetShapes[i];
                float r = CombatCollisionMath.BoundingRadius(t.Radius, t.HalfExtents, t.ShapeType);
                if (r > maxTargetRadius)
                {
                    maxTargetRadius = r;
                }
            }

            // Pass 2: register each target at its center cell (one entry per target,
            // no duplicates). Faction filtering is done per-candidate in the job.
            var targetCells = new NativeParallelMultiHashMap<long, int>(
                math.max(1, totalTargetCount), Allocator.TempJob);
            for (int i = 0; i < targetPositions.Length; i++)
            {
                int2 cell = FloorCell(targetPositions[i].Value);
                targetCells.Add(CellKey(cell.x, cell.y), i);
            }

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            var hitApply = state.World.GetExistingSystemManaged<CombatApplyFinalizeSystem>();
            var vfxPending = new NativeStream(activeProjectileCount, Allocator.TempJob);
            var job = new ProjectileCollisionJob
            {
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetShapes = targetShapes,
                TargetFactions = targetFactions,
                TargetCells = targetCells,
                TotalTargetCount = totalTargetCount,
                MaxTargetRadius = maxTargetRadius,
                HitWriter = hitApply != null
                    ? hitApply.AsParallelWriter()
                    : default,
                HasHitWriter = hitApply != null && hitApply.HitQueue.IsCreated,
                VfxPending = vfxPending.AsWriter(),
                ProjectileEventWriter = expansion != null
                    ? expansion.EventQueue.AsParallelWriter()
                    : default,
                AoeEventWriter = aoeExpansion != null
                    ? aoeExpansion.EventQueue.AsParallelWriter()
                    : default
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);

            // The collision job writes both expansion EventQueues via ParallelWriter. Those
            // queues are read on the main thread by the expansion systems, which only complete
            // their own component-derived dependency. Forward this write job so they wait on it.
            if (expansion != null)
                expansion.ProducerHandle =
                    JobHandle.CombineDependencies(expansion.ProducerHandle, collisionHandle);
            if (aoeExpansion != null)
                aoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, collisionHandle);
            if (hitApply != null)
                hitApply.ProducerHandle =
                    JobHandle.CombineDependencies(hitApply.ProducerHandle, collisionHandle);

            var vfxFlushHandle = new VfxStreamFlushJob
            {
                Scope = SystemAPI.GetSingletonEntity<VfxSingleton>(),
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            JobHandle targetDisposeHandle = JobHandle.CombineDependencies(
                targetEntities.Dispose(collisionHandle),
                JobHandle.CombineDependencies(
                    targetPositions.Dispose(collisionHandle),
                    JobHandle.CombineDependencies(
                        targetShapes.Dispose(collisionHandle),
                        targetFactions.Dispose(collisionHandle))));
            state.Dependency = targetCells.Dispose(
                JobHandle.CombineDependencies(
                    targetDisposeHandle,
                    disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(ProjectileCollisionActiveTag))]
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
            public float MaxTargetRadius;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public bool HasHitWriter;
            public NativeStream.Writer VfxPending;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventWriter;

            private void Execute(
                [EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in CombatRenderComponent render,
                ref CombatLifetimeComponent lifetime,
                ref ProjectileHitComponent projectileHit,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                NativeStream.Writer vfxPending = VfxPending;
                vfxPending.BeginForEachIndex(entityIndexInQuery);

                float areaSize = math.max(render.VisualScale.x, render.VisualScale.y);
                if (identity.Faction == CombatFaction.None)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                    EndVfxStream(ref vfxPending);
                    return;
                }

                if (lifetime.Remaining <= 0f)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                    EndVfxStream(ref vfxPending);
                    return;
                }

                // Pierce is the projectile's hit cap. It may still hit at 0; below zero is exhausted.
                if (projectileHit.PierceRemaining < 0)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                    EndVfxStream(ref vfxPending);
                    return;
                }

                if (TotalTargetCount == 0)
                {
                    EndVfxStream(ref vfxPending);
                    return;
                }

                // Expand the projectile's AABB by MaxTargetRadius before converting to cell
                // coordinates. Any target whose center falls within this expanded region is
                // guaranteed to have its center cell included in the query, so no miss is
                // possible at cell boundaries regardless of how large a target is.
                float2 queryMin = collision.BoundsMin - new float2(MaxTargetRadius, MaxTargetRadius);
                float2 queryMax = collision.BoundsMax + new float2(MaxTargetRadius, MaxTargetRadius);
                int2 cellMin = FloorCell(queryMin);
                int2 cellMax = FloorCell(queryMax);

                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    for (int cx = cellMin.x; cx <= cellMax.x; cx++)
                    {
                        long key = CellKey(cx, cy);
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

                            if (projectileHit.HitPayload.OnHitSpawn.Enabled
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
                                && projectileHit.HitPayload.OnHitSpawn.Kind == IntervalChildKind.Aoe)
                            {
                                int aoeId = HashId(
                                    identity.ProjectileId,
                                    identity.TypeId,
                                    targetKey,
                                    ImpactAoeIdSalt);
                                AoeEventWriter.Enqueue(new AoeSpawnEvent
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

                            vfxPending.Write(new VfxPendingSpawn
                            {
                                TypeId = identity.TypeId,
                                Trigger = 1,
                                Position = kinematics.Position,
                                AreaSize = areaSize
                            });

                            AddOrRefreshGate(contactGates, targetKey,
                                projectileHit.RepeatHitCooldownSeconds);

                            projectileHit.PierceRemaining--;
                            if (projectileHit.PierceRemaining < 0)
                            {
                                Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                                EndVfxStream(ref vfxPending);
                                return;
                            }
                        }
                        while (TargetCells.TryGetNextValue(out targetIdx, ref iterator));
                    }
                }

                EndVfxStream(ref vfxPending);
            }

            private void Deactivate(
                ProjectileIdentityComponent identity,
                float2 position,
                float areaSize,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                ref NativeStream.Writer vfxPending)
            {
                lifetime.Remaining = 0f;
                active.ValueRW = false;
                renderActive.ValueRW = false;
                vfxPending.Write(new VfxPendingSpawn
                {
                    TypeId = identity.TypeId,
                    Trigger = 2,
                    Position = position,
                    AreaSize = areaSize
                });
            }

            private static void EndVfxStream(ref NativeStream.Writer vfxPending)
            {
                vfxPending.EndForEachIndex();
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

        private static int2 FloorCell(float2 pos)
        {
            return new int2(
                (int)math.floor(pos.x / SpatialHashCellSize),
                (int)math.floor(pos.y / SpatialHashCellSize));
        }

        private static long CellKey(int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
