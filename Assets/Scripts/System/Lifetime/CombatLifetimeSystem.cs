using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Lifetime
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
            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatAoeVfxDispatchSingleton>(
                out RefRW<CombatAoeVfxDispatchSingleton> vfx);
            NativeQueue<AoeVfxSpawnRequest> vfxQueue = hasVfx ? vfx.ValueRO.PendingAoeSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;
            NativeQueue<AoeVfxSpawnRequest>.ParallelWriter vfxWriter = hasVfx
                ? vfxQueue.AsParallelWriter()
                : default;

            var projectileJob = new ProjectileLifetimeJob
            {
                DeltaTime = deltaTime
            };
            var aoeJob = new AoeLifetimeJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxWriter,
                HasVfxWriter = hasVfx
            };

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
        [WithDisabled(typeof(ArmingTag))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    CombatDeathUtility.Kill(active, arming);
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(CombatLifetimeComponent))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct AoeLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<AoeVfxSpawnRequest>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            private void Execute(
                in AoeVfxIds vfxIds,
                in CombatKinematicsComponent kinematics,
                in CombatRenderAuthoring authoring,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatCollisionActiveTag> collisionActive,
                EnabledRefRW<ArmingTag> arming)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    CombatDeathUtility.Kill(
                        active,
                        collisionActive,
                        arming,
                        VfxPending,
                        HasVfxWriter,
                        vfxIds.ExpireId,
                        kinematics.Position,
                        math.max(authoring.VisualScale.x, authoring.VisualScale.y));
                }
            }
        }
    }
}
