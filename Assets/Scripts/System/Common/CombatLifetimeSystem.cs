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
    [UpdateBefore(typeof(LingeringAoeCollisionSystem))]
    public partial struct CombatLifetimeSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatVfxDispatchSingleton>(
                out RefRW<CombatVfxDispatchSingleton> vfx);
            NativeQueue<VfxPendingSpawn> vfxQueue = hasVfx ? vfx.ValueRO.PendingSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxWriter = hasVfx
                ? vfxQueue.AsParallelWriter()
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
                vfx.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, aoeHandle);
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
                EnabledRefRW<Active> active)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    CombatDeathUtility.Kill(
                        active,
                        VfxPending,
                        HasVfxWriter,
                        identity.TypeId,
                        kinematics.Position,
                        math.max(authoring.VisualScale.x, authoring.VisualScale.y));
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
                EnabledRefRW<CombatCollisionActiveTag> collisionActive)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    CombatDeathUtility.Kill(
                        active,
                        collisionActive,
                        VfxPending,
                        HasVfxWriter,
                        identity.TypeId,
                        kinematics.Position,
                        math.max(authoring.VisualScale.x, authoring.VisualScale.y));
                }
            }
        }
    }
}
