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
        private const float SpatialHashCellSize = 64f;

        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();
            int targetCellCapacity = 0;
            foreach (DynamicBuffer<ProjectileTargetElement> targets in SystemAPI.Query<DynamicBuffer<ProjectileTargetElement>>())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    int2 min = MinCell(target.BoundsMin);
                    int2 max = MaxCell(target.BoundsMax);
                    targetCellCapacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
                }
            }

            var occupiedTargetCells = new NativeParallelHashSet<long>(math.max(1, targetCellCapacity), Allocator.TempJob);
            foreach ((DynamicBuffer<ProjectileTargetElement> targets, Entity scope) in SystemAPI.Query<DynamicBuffer<ProjectileTargetElement>>().WithEntityAccess())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    int2 min = MinCell(target.BoundsMin);
                    int2 max = MaxCell(target.BoundsMax);
                    for (int y = min.y; y <= max.y; y++)
                    {
                        for (int x = min.x; x <= max.x; x++)
                        {
                            occupiedTargetCells.Add(CellKey(scope, x, y));
                        }
                    }
                }
            }

            var pendingHits = new NativeQueue<ProjectilePendingHit>(Allocator.TempJob);
            var job = new ProjectileCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<ProjectileTargetElement>(true),
                OccupiedTargetCells = occupiedTargetCells,
                OccupiedTargetCellCount = occupiedTargetCells.Count(),
                PendingHits = pendingHits.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);
            var flushHandle = new ProjectileHitFlushJob
            {
                PendingHits = pendingHits,
                Hits = SystemAPI.GetBufferLookup<ProjectileHitElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeHitsHandle = pendingHits.Dispose(flushHandle);
            state.Dependency = occupiedTargetCells.Dispose(disposeHitsHandle);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<ProjectileTargetElement> Targets;
            [ReadOnly] public NativeParallelHashSet<long> OccupiedTargetCells;
            public int OccupiedTargetCellCount;
            public NativeQueue<ProjectilePendingHit>.ParallelWriter PendingHits;

            private void Execute(
                in ProjectileIdentityComponent identity,
                in ProjectileKinematicsComponent kinematics,
                in ProjectileCollisionComponent collision,
                ref ProjectileLifetimeComponent lifetime,
                ref ProjectileHitComponent hit,
                EnabledRefRW<ProjectileActiveTag> active,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                if (identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    Deactivate(ref lifetime, active);
                    return;
                }

                if (lifetime.RemainingLifetime <= 0f)
                {
                    Deactivate(ref lifetime, active);
                    return;
                }

                if (!SpatialHashIntersects(identity, collision))
                {
                    return;
                }

                DynamicBuffer<ProjectileTargetElement> targets = Targets[identity.Scope];
                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    if ((hit.TargetMask & target.TargetMask) == 0 || IsGated(contactGates, target.TargetId))
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

                    uint order = ProjectileEventOrder.ForProjectileTarget(identity.ProjectileId, target.TargetId);
                    PendingHits.Enqueue(new ProjectilePendingHit
                    {
                        Scope = identity.Scope,
                        ProjectileId = identity.ProjectileId,
                        ProjectileTypeId = identity.TypeId,
                        TargetId = target.TargetId,
                        Position = kinematics.Position,
                        HitPayload = hit.HitPayload,
                        Order = order
                    });

                    AddOrRefreshGate(contactGates, target.TargetId, hit.RepeatHitCooldownSeconds);
                    if (hit.PierceRemaining <= 0)
                    {
                        Deactivate(ref lifetime, active);
                        return;
                    }

                    hit.PierceRemaining--;
                }
            }

            private void Deactivate(
                ref ProjectileLifetimeComponent lifetime,
                EnabledRefRW<ProjectileActiveTag> active)
            {
                lifetime.RemainingLifetime = 0f;
                active.ValueRW = false;
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

            private bool SpatialHashIntersects(
                ProjectileIdentityComponent identity,
                ProjectileCollisionComponent collision)
            {
                if (OccupiedTargetCellCount == 0)
                {
                    return false;
                }

                int2 min = MinCell(collision.BoundsMin);
                int2 max = MaxCell(collision.BoundsMax);
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (OccupiedTargetCells.Contains(CellKey(identity.Scope, x, y)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        [BurstCompile]
        private struct ProjectileHitFlushJob : IJob
        {
            public NativeQueue<ProjectilePendingHit> PendingHits;
            public BufferLookup<ProjectileHitElement> Hits;

            public void Execute()
            {
                while (PendingHits.TryDequeue(out ProjectilePendingHit pending))
                {
                    if (pending.Scope == Entity.Null || !Hits.HasBuffer(pending.Scope))
                    {
                        continue;
                    }

                    Hits[pending.Scope].Add(new ProjectileHitElement
                    {
                        ProjectileId = pending.ProjectileId,
                        ProjectileTypeId = pending.ProjectileTypeId,
                        TargetId = pending.TargetId,
                        Position = pending.Position,
                        HitPayload = pending.HitPayload,
                        Order = pending.Order
                    });
                }
            }
        }

        private static int2 MinCell(float2 min)
        {
            return new int2(
                (int)math.floor(min.x / SpatialHashCellSize),
                (int)math.floor(min.y / SpatialHashCellSize));
        }

        private static int2 MaxCell(float2 max)
        {
            return new int2(
                (int)math.floor(max.x / SpatialHashCellSize),
                (int)math.floor(max.y / SpatialHashCellSize));
        }

        private static long CellKey(Entity scope, int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)scope.Index) * 1099511628211UL;
                hash = (hash ^ (uint)scope.Version) * 1099511628211UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
