using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileContactGateSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        // this depends on the arena size and mob count
        // cellSize = sqrt(arenaWidth * arenaHeight / mobCount) * ~1.5
        private const float SpatialHashCellSize = 1f;

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.CompleteDependencyBeforeRO<CombatTargetElement>();

            // Pass 1: count targets and find the largest bounding radius.
            // Used to size the multimap and to expand the per-projectile query range
            // so targets near cell boundaries are never missed.
            int totalTargetCount = 0;
            float maxTargetRadius = 0f;
            foreach (DynamicBuffer<CombatTargetElement> targets in
                SystemAPI.Query<DynamicBuffer<CombatTargetElement>>().WithAll<ProjectileScope>())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    CombatTargetElement t = targets[i];
                    totalTargetCount++;
                    float r = CombatCollisionMath.BoundingRadius(t.Radius, t.HalfExtents, t.ShapeType);
                    if (r > maxTargetRadius)
                    {
                        maxTargetRadius = r;
                    }
                }
            }

            // Pass 2: register each target at its center cell (one entry per target,
            // no duplicates). The query expansion handles boundary coverage.
            var targetCells = new NativeParallelMultiHashMap<long, int>(
                math.max(1, totalTargetCount), Allocator.TempJob);
            foreach ((DynamicBuffer<CombatTargetElement> targets, Entity scope) in
                SystemAPI.Query<DynamicBuffer<CombatTargetElement>>()
                    .WithAll<ProjectileScope>()
                    .WithEntityAccess())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    int2 cell = FloorCell(targets[i].Position);
                    targetCells.Add(CellKey(scope, cell.x, cell.y), i);
                }
            }

            var pendingDamage = new NativeQueue<CombatPendingDamage>(Allocator.TempJob);
            var pendingSpawns = new NativeQueue<CombatPendingSpawn>(Allocator.TempJob);
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            var job = new ProjectileCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<CombatTargetElement>(true),
                TargetCells = targetCells,
                TotalTargetCount = totalTargetCount,
                MaxTargetRadius = maxTargetRadius,
                PendingDamage = pendingDamage.AsParallelWriter(),
                PendingSpawns = pendingSpawns.AsParallelWriter(),
                VfxPending = vfxPending.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);
            var flushHandle = new CombatHitFlushJob
            {
                PendingDamage = pendingDamage,
                PendingSpawns = pendingSpawns,
                Damage = SystemAPI.GetBufferLookup<CombatDamageElement>(),
                Spawns = SystemAPI.GetBufferLookup<CombatSpawnElement>()
            }.Schedule(collisionHandle);
            var vfxFlushHandle = new VfxFlushJob
            {
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeDamageHandle = pendingDamage.Dispose(flushHandle);
            JobHandle disposeSpawnsHandle = pendingSpawns.Dispose(flushHandle);
            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            state.Dependency = targetCells.Dispose(
                JobHandle.CombineDependencies(disposeDamageHandle, disposeSpawnsHandle, disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag), typeof(ProjectileCollisionActiveTag))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;
            public int TotalTargetCount;
            public float MaxTargetRadius;
            public NativeQueue<CombatPendingDamage>.ParallelWriter PendingDamage;
            public NativeQueue<CombatPendingSpawn>.ParallelWriter PendingSpawns;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                Entity entity,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in CombatRenderComponent render,
                ref ProjectileLifetimeComponent lifetime,
                ref ProjectileHitComponent projectileHit,
                EnabledRefRW<ProjectileActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<ProjectileContactGateElement> contactGates)
            {
                float areaSize = math.max(render.VisualScale.x, render.VisualScale.y);
                if (identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive);
                    return;
                }

                if (lifetime.RemainingLifetime <= 0f)
                {
                    Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive);
                    return;
                }

                if (TotalTargetCount == 0)
                {
                    return;
                }

                DynamicBuffer<CombatTargetElement> targets = Targets[identity.Scope];

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
                        long key = CellKey(identity.Scope, cx, cy);
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
                                PendingDamage.Enqueue(new CombatPendingDamage
                                {
                                    Scope = identity.Scope,
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

                            if (HasSpawnEvent(projectileHit.HitPayload))
                            {
                                PendingSpawns.Enqueue(new CombatPendingSpawn
                                {
                                    Scope = identity.Scope,
                                    SourceId = identity.ProjectileId,
                                    TypeId = identity.TypeId,
                                    TargetId = target.TargetId,
                                    Position = kinematics.Position,
                                    TargetPosition = target.Position,
                                    Kind = CombatHitKind.Projectile,
                                    SourceNodeId = projectileHit.HitPayload.SourceNodeId,
                                    ImpactAoe = projectileHit.HitPayload.ImpactAoe,
                                    ImpactProjectile = projectileHit.HitPayload.ImpactProjectile
                                });
                            }

                            VfxPending.Enqueue(new VfxPendingSpawn
                            {
                                Scope = identity.Scope,
                                TypeId = identity.TypeId,
                                Trigger = 1,
                                Position = kinematics.Position,
                                AreaSize = areaSize
                            });

                            AddOrRefreshGate(contactGates, target.TargetId,
                                projectileHit.RepeatHitCooldownSeconds);

                            if (projectileHit.PierceRemaining <= 0)
                            {
                                Deactivate(identity, kinematics.Position, areaSize, ref lifetime, active, renderActive);
                                return;
                            }

                            projectileHit.PierceRemaining--;
                        }
                        while (TargetCells.TryGetNextValue(out targetIdx, ref iterator));
                    }
                }
            }

            private void Deactivate(
                ProjectileIdentityComponent identity,
                float2 position,
                float areaSize,
                ref ProjectileLifetimeComponent lifetime,
                EnabledRefRW<ProjectileActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                lifetime.RemainingLifetime = 0f;
                active.ValueRW = false;
                renderActive.ValueRW = false;
                VfxPending.Enqueue(new VfxPendingSpawn
                {
                    Scope = identity.Scope,
                    TypeId = identity.TypeId,
                    Trigger = 2,
                    Position = position,
                    AreaSize = areaSize
                });
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

            private static bool HasSpawnEvent(in ProjectileHitPayload payload) =>
                payload.ImpactAoe.Enabled || payload.ImpactProjectile.Enabled;
        }

        private static int2 FloorCell(float2 pos)
        {
            return new int2(
                (int)math.floor(pos.x / SpatialHashCellSize),
                (int)math.floor(pos.y / SpatialHashCellSize));
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
