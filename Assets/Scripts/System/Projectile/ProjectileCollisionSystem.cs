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
    public partial struct ProjectileCollisionSystem : ISystem
    {
        // this depends on the arena size and mob count
        // cellSize = sqrt(arenaWidth * arenaHeight / mobCount) * ~1.5
        private const float SpatialHashCellSize = 1f;
        private EntityQuery activeProjectileQuery;
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            activeProjectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileActiveTag>(),
                ComponentType.ReadOnly<ProjectileCollisionActiveTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                ComponentType.ReadOnly<CombatRenderComponent>(),
                ComponentType.ReadWrite<CombatLifetimeComponent>(),
                ComponentType.ReadWrite<ProjectileHitComponent>(),
                ComponentType.ReadWrite<CombatRenderActiveTag>(),
                ComponentType.ReadWrite<ProjectileContactGateElement>());
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        public void OnUpdate(ref SystemState state)
        {
            int activeProjectileCount = activeProjectileQuery.CalculateEntityCount();
            if (activeProjectileCount == 0)
            {
                return;
            }

            state.EntityManager.CompleteDependencyBeforeRO<CombatTargetElement>();

            Entity scope = scopeQuery.GetSingletonEntity();
            DynamicBuffer<CombatTargetElement> targets = state.EntityManager.GetBuffer<CombatTargetElement>(scope);

            // Pass 1: count targets and find the largest bounding radius.
            // Used to size the multimap and to expand the per-projectile query range
            // so targets near cell boundaries are never missed.
            int totalTargetCount = targets.Length;
            float maxTargetRadius = 0f;
            for (int i = 0; i < targets.Length; i++)
            {
                CombatTargetElement t = targets[i];
                float r = CombatCollisionMath.BoundingRadius(t.Radius, t.HalfExtents, t.ShapeType);
                if (r > maxTargetRadius)
                {
                    maxTargetRadius = r;
                }
            }

            // Pass 2: register each target at its center cell (one entry per target,
            // no duplicates), keyed by Faction so a projectile's cell lookup only
            // matches targets belonging to its own faction's target set.
            var targetCells = new NativeParallelMultiHashMap<long, int>(
                math.max(1, totalTargetCount), Allocator.TempJob);
            for (int i = 0; i < targets.Length; i++)
            {
                int2 cell = FloorCell(targets[i].Position);
                targetCells.Add(CellKey(targets[i].Faction, cell.x, cell.y), i);
            }

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            var pendingDamage = new NativeStream(activeProjectileCount, Allocator.TempJob);
            var vfxPending = new NativeStream(activeProjectileCount, Allocator.TempJob);
            var job = new ProjectileCollisionJob
            {
                Targets = targets,
                TargetCells = targetCells,
                TotalTargetCount = totalTargetCount,
                MaxTargetRadius = maxTargetRadius,
                PendingDamage = pendingDamage.AsWriter(),
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

            var flushHandle = new CombatHitFlushJob
            {
                Scope = scope,
                PendingDamage = pendingDamage,
                Damage = SystemAPI.GetBufferLookup<CombatDamageElement>()
            }.Schedule(collisionHandle);
            var vfxFlushHandle = new VfxStreamFlushJob
            {
                Scope = scope,
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeDamageHandle = pendingDamage.Dispose(flushHandle);
            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            state.Dependency = targetCells.Dispose(
                JobHandle.CombineDependencies(disposeDamageHandle, disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag), typeof(ProjectileCollisionActiveTag))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            [ReadOnly] public DynamicBuffer<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;
            public int TotalTargetCount;
            public float MaxTargetRadius;
            public NativeStream.Writer PendingDamage;
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
                EnabledRefRW<ProjectileActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                NativeStream.Writer pendingDamage = PendingDamage;
                NativeStream.Writer vfxPending = VfxPending;
                pendingDamage.BeginForEachIndex(entityIndexInQuery);
                vfxPending.BeginForEachIndex(entityIndexInQuery);

                float areaSize = math.max(render.VisualScale.x, render.VisualScale.y);
                if (identity.Faction == CombatFaction.None)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                    EndStreams(ref pendingDamage, ref vfxPending);
                    return;
                }

                if (lifetime.Remaining <= 0f)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                    EndStreams(ref pendingDamage, ref vfxPending);
                    return;
                }

                if (TotalTargetCount == 0)
                {
                    EndStreams(ref pendingDamage, ref vfxPending);
                    return;
                }

                DynamicBuffer<CombatTargetElement> targets = Targets;

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
                        long key = CellKey(identity.Faction, cx, cy);
                        if (!TargetCells.TryGetFirstValue(key, out int targetIdx,
                            out NativeParallelMultiHashMapIterator<long> iterator))
                        {
                            continue;
                        }

                        do
                        {
                            CombatTargetElement target = targets[targetIdx];

                            if (IsGated(contactGates, target.TargetId))
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

                            if (!ProjectileCollisionMath.Hit(kinematics, collision, target))
                            {
                                continue;
                            }

                            if (HasDamageEvent(projectileHit.HitPayload))
                            {
                                pendingDamage.Write(new DamageReplayEvent
                                {
                                    Faction = identity.Faction,
                                    SourceId = identity.ProjectileId,
                                    TypeId = identity.TypeId,
                                    TargetId = target.TargetId,
                                    Position = kinematics.Position,
                                    Kind = CombatHitKind.Projectile,
                                    DamageAmount = projectileHit.HitPayload.DamageAmount,
                                    CritChance = projectileHit.HitPayload.CritChance,
                                    CritMultiplier = projectileHit.HitPayload.CritMultiplier,
                                    DirectDamageEnabled = projectileHit.HitPayload.DirectDamageEnabled,
                                    SourceNodeId = projectileHit.HitPayload.SourceNodeId,
                                    StackEffect = projectileHit.HitPayload.StackEffect
                                });
                            }

                            if (projectileHit.HitPayload.ImpactProjectile.Enabled)
                            {
                                ProjectileEventWriter.Enqueue(ProjectileSpawnPipeline.BuildImpactProjectileEvent(
                                    identity.Faction, identity.ProjectileId, identity.TypeId, target.TargetId,
                                    kinematics.Position, target.Position,
                                    projectileHit.HitPayload.ImpactProjectile));
                            }

                            if (projectileHit.HitPayload.ImpactAoe.Enabled)
                            {
                                AoeEventWriter.Enqueue(AoeSpawnPipeline.BuildImpactAoeEvent(
                                    identity.Faction, identity.ProjectileId, identity.TypeId, target.TargetId,
                                    kinematics.Position, projectileHit.HitPayload.SourceNodeId,
                                    projectileHit.HitPayload.ImpactAoe));
                            }

                            vfxPending.Write(new VfxPendingSpawn
                            {
                                Faction = identity.Faction,
                                TypeId = identity.TypeId,
                                Trigger = 1,
                                Position = kinematics.Position,
                                AreaSize = areaSize
                            });

                            AddOrRefreshGate(contactGates, target.TargetId,
                                projectileHit.RepeatHitCooldownSeconds);

                            if (projectileHit.PierceRemaining <= 0)
                            {
                                Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive, ref vfxPending);
                                EndStreams(ref pendingDamage, ref vfxPending);
                                return;
                            }

                            projectileHit.PierceRemaining--;
                        }
                        while (TargetCells.TryGetNextValue(out targetIdx, ref iterator));
                    }
                }

                EndStreams(ref pendingDamage, ref vfxPending);
            }

            private void Deactivate(
                ProjectileIdentityComponent identity,
                float2 position,
                float areaSize,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<ProjectileActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                ref NativeStream.Writer vfxPending)
            {
                lifetime.Remaining = 0f;
                active.ValueRW = false;
                renderActive.ValueRW = false;
                vfxPending.Write(new VfxPendingSpawn
                {
                    Faction = identity.Faction,
                    TypeId = identity.TypeId,
                    Trigger = 2,
                    Position = position,
                    AreaSize = areaSize
                });
            }

            private static void EndStreams(
                ref NativeStream.Writer pendingDamage,
                ref NativeStream.Writer vfxPending)
            {
                pendingDamage.EndForEachIndex();
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

            private static bool HasDamageEvent(in ProjectileHitPayload payload) =>
                payload.DirectDamageEnabled || payload.StackEffect.Enabled;
        }

        private static int2 FloorCell(float2 pos)
        {
            return new int2(
                (int)math.floor(pos.x / SpatialHashCellSize),
                (int)math.floor(pos.y / SpatialHashCellSize));
        }

        private static long CellKey(CombatFaction faction, int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (byte)faction) * 1099511628211UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
