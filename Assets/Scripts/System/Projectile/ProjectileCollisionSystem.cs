using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileContactGateSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton =
                SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var job = new ProjectileCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<ProjectileTargetElement>(true),
                CommandBuffer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<ProjectileTargetElement> Targets;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            private void Execute(
                [ChunkIndexInQuery] int chunkIndex,
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
                    CommandBuffer.AppendToBuffer((int)order, projectile.Scope, new ProjectileHitElement
                    {
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
    }
}
