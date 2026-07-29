using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Aoes
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    public partial struct AoePulseVfxSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // The VFX lane is created unconditionally by CombatAoeVfxDispatchSystem's OnCreate.
            // Read it directly: a missing lane is a broken world and must throw, not be skipped.
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();

            JobHandle pulseHandle = new AoePulseVfxJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            vfx.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, pulseHandle);

            state.Dependency = pulseHandle;
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct AoePulseVfxJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<CircularVfxSpawnRequest>.ParallelWriter CircularVfxPending;
            public NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter TimedCircularVfxPending;

            private void Execute(
                in AoeVfxIds vfxIds,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
                in VfxTimingData timing,
                ref AoePulseVfxComponent pulseVfx)
            {
                if (pulseVfx.Interval <= 0f)
                {
                    return;
                }

                pulseVfx.RemainingInterval -= DeltaTime;
                if (pulseVfx.RemainingInterval > 0f)
                {
                    return;
                }

                pulseVfx.RemainingInterval = pulseVfx.Interval;
                VfxEmit.Enqueue(
                    vfxIds.PulseId,
                    kinematics.Position,
                    area.Size,
                    timing,
                    CircularVfxPending,
                    TimedCircularVfxPending);
            }
        }
    }
}
