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
            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatAoeVfxDispatchSingleton>(
                out RefRW<CombatAoeVfxDispatchSingleton> vfx);
            NativeQueue<VfxSpawnRequest> basicVfxQueue = hasVfx ? vfx.ValueRO.PendingBasicSpawns : default;
            NativeQueue<TimedVfxSpawnRequest> timedVfxQueue = hasVfx ? vfx.ValueRO.PendingTimedSpawns : default;
            bool hasBasicVfx = hasVfx && basicVfxQueue.IsCreated;
            bool hasTimedVfx = hasVfx && timedVfxQueue.IsCreated;
            hasVfx = hasBasicVfx || hasTimedVfx;

            JobHandle pulseHandle = new AoePulseVfxJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                BasicVfxPending = hasBasicVfx ? basicVfxQueue.AsParallelWriter() : default,
                HasBasicVfxWriter = hasBasicVfx,
                TimedVfxPending = hasTimedVfx ? timedVfxQueue.AsParallelWriter() : default,
                HasTimedVfxWriter = hasTimedVfx
            }.ScheduleParallel(state.Dependency);

            if (hasVfx)
            {
                vfx.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, pulseHandle);
            }

            state.Dependency = pulseHandle;
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct AoePulseVfxJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxSpawnRequest>.ParallelWriter BasicVfxPending;
            public bool HasBasicVfxWriter;
            public NativeQueue<TimedVfxSpawnRequest>.ParallelWriter TimedVfxPending;
            public bool HasTimedVfxWriter;

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
                    BasicVfxPending,
                    HasBasicVfxWriter,
                    TimedVfxPending,
                    HasTimedVfxWriter);
            }
        }
    }
}
