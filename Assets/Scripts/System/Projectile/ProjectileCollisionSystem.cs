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
                ref ProjectileComponent projectile,
                EnabledRefRW<ProjectileActiveTag> active,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                if (projectile.Scope == Entity.Null || !Targets.HasBuffer(projectile.Scope))
                {
                    Deactivate(ref projectile, active);
                    return;
                }

                if (projectile.RemainingLifetime <= 0f)
                {
                    Deactivate(ref projectile, active);
                    return;
                }

                if (!SpatialHashIntersects(projectile))
                {
                    return;
                }

                DynamicBuffer<ProjectileTargetElement> targets = Targets[projectile.Scope];
                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    if ((projectile.TargetMask & target.TargetMask) == 0 || IsGated(contactGates, target.TargetId))
                    {
                        continue;
                    }

                    if (!ProjectileCollisionMath.BoundsIntersect(
                        projectile.BoundsMin,
                        projectile.BoundsMax,
                        target.BoundsMin,
                        target.BoundsMax))
                    {
                        continue;
                    }

                    if (!ProjectileCollisionMath.Hit(projectile, target))
                    {
                        continue;
                    }

                    uint order = ProjectileEventOrder.ForProjectileTarget(projectile.ProjectileId, target.TargetId);
                    PendingHits.Enqueue(new ProjectilePendingHit
                    {
                        Scope = projectile.Scope,
                        ProjectileId = projectile.ProjectileId,
                        ProjectileTypeId = projectile.TypeId,
                        TargetId = target.TargetId,
                        Position = projectile.Position,
                        HitPayload = projectile.HitPayload,
                        Order = order
                    });

                    AddOrRefreshGate(contactGates, target.TargetId, projectile.RepeatHitCooldownSeconds);
                    if (projectile.PierceRemaining <= 0)
                    {
                        Deactivate(ref projectile, active);
                        return;
                    }

                    projectile.PierceRemaining--;
                }
            }

            private void Deactivate(
                ref ProjectileComponent projectile,
                EnabledRefRW<ProjectileActiveTag> active)
            {
                projectile.RemainingLifetime = 0f;
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

            private bool SpatialHashIntersects(ProjectileComponent projectile)
            {
                if (OccupiedTargetCellCount == 0)
                {
                    return false;
                }

                int2 min = MinCell(projectile.BoundsMin);
                int2 max = MaxCell(projectile.BoundsMax);
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (OccupiedTargetCells.Contains(CellKey(projectile.Scope, x, y)))
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
