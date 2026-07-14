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

namespace PlayGround.System.Combat.Lifetime
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CombatLifetimeSystem))]
    public partial struct CombatArmingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatAoeVfxDispatchSingleton>(
                out RefRW<CombatAoeVfxDispatchSingleton> vfx);
            NativeQueue<AoeVfxSpawnRequest> vfxQueue = hasVfx ? vfx.ValueRO.PendingAoeSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;
            NativeQueue<AoeVfxSpawnRequest>.ParallelWriter vfxWriter = hasVfx
                ? vfxQueue.AsParallelWriter()
                : default;

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
                VfxPending = vfxWriter,
                HasVfxWriter = hasVfx
            }.ScheduleParallel(projectileHandle);

            if (hasVfx)
            {
                vfx.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, aoeHandle);
            }

            state.Dependency = aoeHandle;
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
            public NativeQueue<AoeVfxSpawnRequest>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            private void Execute(
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
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
                if (HasVfxWriter)
                {
                    VfxPending.Enqueue(new AoeVfxSpawnRequest
                    {
                        TypeId = identity.TypeId,
                        Trigger = AoeVfxTrigger.Spawn,
                        Position = kinematics.Position,
                        AreaSize = area.Size
                    });
                }
            }
        }
    }
}
