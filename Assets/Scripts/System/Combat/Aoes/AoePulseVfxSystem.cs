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
            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatVfxDispatchSingleton>(
                out RefRW<CombatVfxDispatchSingleton> vfx);
            NativeQueue<VfxPendingSpawn> vfxQueue = hasVfx ? vfx.ValueRO.PendingSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxWriter = hasVfx
                ? vfxQueue.AsParallelWriter()
                : default;

            JobHandle pulseHandle = new AoePulseVfxJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                VfxPending = vfxWriter,
                HasVfxWriter = hasVfx
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
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            private void Execute(
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
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
                if (HasVfxWriter)
                {
                    VfxPending.Enqueue(new VfxPendingSpawn
                    {
                        TypeId = identity.TypeId,
                        Trigger = 3,
                        Position = kinematics.Position,
                        AreaSize = area.Size
                    });
                }
            }
        }
    }
}
