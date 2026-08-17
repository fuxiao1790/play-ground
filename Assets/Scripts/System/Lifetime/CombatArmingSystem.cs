using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Audio;
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

namespace PlayGround.System.Combat.Lifetime
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CombatLifetimeSystem))]
    public partial struct CombatArmingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // The VFX lane is created unconditionally by CombatAoeVfxDispatchSystem's OnCreate.
            // Read it directly: a missing lane is a broken world and must throw, not be skipped.
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            RefRW<SoundEventSingleton> sounds =
                SystemAPI.GetSingletonRW<SoundEventSingleton>();

            float deltaTime = SystemAPI.Time.DeltaTime;

            JobHandle projectileHandle = new ProjectileArmingJob
            {
                DeltaTime = deltaTime
            }.ScheduleParallel(state.Dependency);

            // AOE job chains after the projectile job: both write ArmingTag /
            // CombatArmingComponent, and the AOE job also writes the shared VFX queue.
            JobHandle aoeHandle = new AoeArmingJob
            {
                DeltaTime = deltaTime,
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter(),
                SoundsPending = sounds.ValueRO.Events.AsParallelWriter()
            }.ScheduleParallel(projectileHandle);

            JobHandle targetedHandle = new TargetedArmingJob
            {
                DeltaTime = deltaTime
            }.ScheduleParallel(aoeHandle);

            vfx.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, targetedHandle);
            sounds.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(sounds.ValueRW.ProducerHandle, targetedHandle);

            state.Dependency = targetedHandle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(ArmingTag))]
        private partial struct ProjectileArmingJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatArmingComponent arming,
                EnabledRefRW<ArmingTag> armingTag)
            {
                arming.Remaining -= DeltaTime;
                if (arming.Remaining <= 0f)
                {
                    arming.Remaining = 0f;
                    armingTag.ValueRW = false;
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(ArmingTag))]
        private partial struct AoeArmingJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ImpactCircleVfxEvent>.ParallelWriter CircularVfxPending;
            public NativeQueue<LingeringCircleVfxEvent>.ParallelWriter TimedCircularVfxPending;
            public NativeQueue<SoundEvent>.ParallelWriter SoundsPending;

            private void Execute(
                in AoeVfxIds vfxIds,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
                in VfxTimingData timing,
                ref CombatArmingComponent arming,
                EnabledRefRW<ArmingTag> armingTag)
            {
                arming.Remaining -= DeltaTime;
                if (arming.Remaining > 0f)
                {
                    return;
                }

                arming.Remaining = 0f;
                armingTag.ValueRW = false;

                // The spawn VFX plays at the moment the AOE goes live, not when the
                // entity was materialized, so an arming AOE telegraphs first and only
                // shows its spawn burst once armed.
                VfxEmit.Enqueue(
                    vfxIds.SpawnId,
                    kinematics.Position,
                    area.Size,
                    timing,
                    CircularVfxPending,
                    TimedCircularVfxPending);
                SoundEmit.Enqueue(
                    identity.SoundIds.SpawnId,
                    kinematics.Position,
                    identity.SpawnSoundRadius,
                    SoundCategory.Spawn,
                    identity.Faction,
                    SoundsPending);
            }
        }

        [BurstCompile]
        [WithAll(typeof(TargetedTag), typeof(Active), typeof(ArmingTag))]
        private partial struct TargetedArmingJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatArmingComponent arming,
                EnabledRefRW<ArmingTag> armingTag)
            {
                arming.Remaining -= DeltaTime;
                if (arming.Remaining <= 0f)
                {
                    arming.Remaining = 0f;
                    armingTag.ValueRW = false;
                }
            }
        }
    }
}
