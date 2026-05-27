using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileContactGateSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var pendingHits = new NativeQueue<ProjectilePendingHit>(Allocator.TempJob);
            var job = new ProjectileCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<ProjectileTargetElement>(true),
                PendingHits = pendingHits.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(state.Dependency);
            var flushHandle = new ProjectileHitFlushJob
            {
                PendingHits = pendingHits,
                Hits = SystemAPI.GetBufferLookup<ProjectileHitElement>()
            }.Schedule(collisionHandle);

            state.Dependency = pendingHits.Dispose(flushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<ProjectileTargetElement> Targets;
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

                DynamicBuffer<ProjectileTargetElement> targets = Targets[projectile.Scope];
                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    if ((projectile.TargetMask & target.TargetMask) == 0 || IsGated(contactGates, target.TargetId))
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
                        DamageAmount = projectile.DamageAmount,
                        DirectDamageEnabled = projectile.DirectDamageEnabled,
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
                    return;
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
                        DamageAmount = pending.DamageAmount,
                        DirectDamageEnabled = pending.DirectDamageEnabled,
                        Order = pending.Order
                    });
                }
            }
        }
    }
}
