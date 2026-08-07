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
using PlayGround.System.Combat.Targeted;
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
    [UpdateBefore(typeof(ProjectileDiscreteCollisionSystem))]
    [UpdateBefore(typeof(LingeringAoeCollisionSystem))]
    public partial struct CombatLifetimeSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            // The VFX lane is created unconditionally by CombatAoeVfxDispatchSystem's OnCreate.
            // Read it directly: a missing lane is a broken world and must throw, not be skipped.
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();

            var projectileJob = new ProjectileLifetimeJob
            {
                DeltaTime = deltaTime
            };
            var aoeJob = new AoeLifetimeJob
            {
                DeltaTime = deltaTime,
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter()
            };

            JobHandle projectileHandle = projectileJob.ScheduleParallel(state.Dependency);
            JobHandle aoeHandle = aoeJob.ScheduleParallel(projectileHandle);
            JobHandle targetedHandle = new TargetedLifetimeJob
            {
                DeltaTime = deltaTime,
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter()
            }.ScheduleParallel(aoeHandle);

            vfx.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, targetedHandle);

            state.Dependency = targetedHandle;
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
            public NativeQueue<CircularVfxSpawnRequest>.ParallelWriter CircularVfxPending;
            public NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter TimedCircularVfxPending;

            private void Execute(
                in AoeVfxIds vfxIds,
                in CombatKinematicsComponent kinematics,
                in CombatRenderAuthoring authoring,
                in VfxTimingData timing,
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
                        CircularVfxPending,
                        TimedCircularVfxPending,
                        vfxIds.ExpireId,
                        kinematics.Position,
                        math.max(authoring.VisualScale.x, authoring.VisualScale.y),
                        timing);
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(TargetedTag), typeof(Active), typeof(CombatLifetimeComponent))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct TargetedLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<CircularVfxSpawnRequest>.ParallelWriter CircularVfxPending;
            public NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter TimedCircularVfxPending;

            private void Execute(
                in TargetedVfxIds vfxIds,
                in TargetedVfxSizeComponent vfxSize,
                in VfxTimingData timing,
                in CombatKinematicsComponent kinematics,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    CombatDeathUtility.Kill(active, arming);
                    VfxEmit.Enqueue(
                        vfxIds.ExpireId,
                        kinematics.Position,
                        vfxSize.EffectSize,
                        timing,
                        CircularVfxPending,
                        TimedCircularVfxPending);
                }
            }
        }
    }
}
