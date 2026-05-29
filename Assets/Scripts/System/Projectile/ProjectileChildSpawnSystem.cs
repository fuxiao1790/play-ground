using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileLifetimeSystem))]
    public partial struct ProjectileChildSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var pendingRequests = new NativeQueue<ProjectilePendingChildSpawn>(Allocator.TempJob);
            var job = new ProjectileChildSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                PendingRequests = pendingRequests.AsParallelWriter()
            };

            var spawnHandle = job.ScheduleParallel(state.Dependency);
            var flushHandle = new ProjectileChildSpawnFlushJob
            {
                PendingRequests = pendingRequests,
                Requests = SystemAPI.GetBufferLookup<ProjectileChildSpawnRequestElement>()
            }.Schedule(spawnHandle);

            state.Dependency = pendingRequests.Dispose(flushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileChildSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectilePendingChildSpawn>.ParallelWriter PendingRequests;

            private void Execute(ref ProjectileComponent projectile)
            {
                
                if (projectile.RemainingLifetime <= 0f                  // todo: this system should be scheduled after the lifetime system so this can be optimized away by using entity query
                    || projectile.Scope == Entity.Null                  // todo: why is this even possible?
                    || projectile.ChildSpawnerId <= 0                   // todo: should use entity query here instead of if statement on id
                    || projectile.ChildSpawnIntervalSeconds <= 0f)      // todo: this is basically the same condition, optimize it away by using the same query, reject tiny spawn interval before entities enter the ecs
                {
                    return;
                }

                float cooldown = projectile.ChildSpawnCooldownRemaining - DeltaTime;
                int tickIndex = projectile.ChildSpawnTickIndex;
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    PendingRequests.Enqueue(new ProjectilePendingChildSpawn
                    {
                        Scope = projectile.Scope,
                        ProjectileId = projectile.ProjectileId,
                        ProjectileTypeId = projectile.TypeId,
                        ChildSpawnerId = projectile.ChildSpawnerId,
                        TickIndex = tickIndex,
                        Position = projectile.Position,
                        Velocity = projectile.Velocity,
                        DamageAmount = projectile.DamageAmount,
                        Order = ProjectileEventOrder.ForProjectileTarget(projectile.ProjectileId, tickIndex)
                    });
                    cooldown += projectile.ChildSpawnIntervalSeconds;
                }

                projectile.ChildSpawnCooldownRemaining = cooldown;
                projectile.ChildSpawnTickIndex = tickIndex;
            }
        }


        // todo: why can't the reader of the Requests just read from PendingRequests? The ecb play back is executed on the main thread because it modifies the entity list
        // todo: is there any benefit to create a specific child spawn event instead of calculating the child spawn data here and use a generic spawn event instead?
        [BurstCompile]
        private struct ProjectileChildSpawnFlushJob : IJob
        {
            public NativeQueue<ProjectilePendingChildSpawn> PendingRequests;
            public BufferLookup<ProjectileChildSpawnRequestElement> Requests;

            public void Execute()
            {
                while (PendingRequests.TryDequeue(out ProjectilePendingChildSpawn pending))
                {
                    if (pending.Scope == Entity.Null || !Requests.HasBuffer(pending.Scope))
                    {
                        continue;
                    }

                    Requests[pending.Scope].Add(new ProjectileChildSpawnRequestElement
                    {
                        ProjectileId = pending.ProjectileId,
                        ProjectileTypeId = pending.ProjectileTypeId,
                        ChildSpawnerId = pending.ChildSpawnerId,
                        TickIndex = pending.TickIndex,
                        Position = pending.Position,
                        Velocity = pending.Velocity,
                        DamageAmount = pending.DamageAmount,
                        DirectDamageEnabled = false,
                        Order = pending.Order
                    });
                }
            }
        }
    }
}
