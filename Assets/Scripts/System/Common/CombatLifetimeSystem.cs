using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateBefore(typeof(TimedSpawnSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileContactGateSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    [UpdateBefore(typeof(AoeContactGateSystem))]
    [UpdateBefore(typeof(LingeringAoeCollisionSystem))]
    public partial struct CombatLifetimeSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var vfx = state.World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
            bool hasVfx = vfx != null && vfx.HasQueue;
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxWriter = hasVfx
                ? vfx.AsParallelWriter()
                : default;

            var projectileJob = new ProjectileLifetimeJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxWriter,
                HasVfxWriter = hasVfx
            };
            var aoeJob = new AoeLifetimeJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxWriter,
                HasVfxWriter = hasVfx
            };

            // AOE job chains after projectile job because both write to the same VFX queue writer.
            JobHandle projectileHandle = projectileJob.ScheduleParallel(state.Dependency);
            JobHandle aoeHandle = aoeJob.ScheduleParallel(projectileHandle);

            if (hasVfx)
            {
                vfx.ProducerHandle = JobHandle.CombineDependencies(vfx.ProducerHandle, aoeHandle);
            }

            state.Dependency = aoeHandle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(CombatLifetimeComponent))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            private void Execute(
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatRenderAuthoring authoring,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    active.ValueRW = false;
                    renderActive.ValueRW = false;
                    if (HasVfxWriter)
                    {
                        VfxPending.Enqueue(new VfxPendingSpawn
                        {
                            TypeId = identity.TypeId,
                            Trigger = 2,
                            Position = kinematics.Position,
                            AreaSize = math.max(authoring.VisualScale.x, authoring.VisualScale.y)
                        });
                    }
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(CombatLifetimeComponent))]
        private partial struct AoeLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            private void Execute(
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatRenderAuthoring authoring,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    active.ValueRW = false;
                    collisionActive.ValueRW = false;
                    renderActive.ValueRW = false;
                    if (HasVfxWriter)
                    {
                        VfxPending.Enqueue(new VfxPendingSpawn
                        {
                            TypeId = identity.TypeId,
                            Trigger = 2,
                            Position = kinematics.Position,
                            AreaSize = math.max(authoring.VisualScale.x, authoring.VisualScale.y)
                        });
                    }
                }
            }
        }
    }
}
