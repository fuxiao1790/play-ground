using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Aoe
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    public partial struct AoePulseVfxSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var vfx = state.World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
            bool hasVfx = vfx != null && vfx.HasQueue;
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxWriter = hasVfx
                ? vfx.AsParallelWriter()
                : default;

            JobHandle pulseHandle = new AoePulseVfxJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                VfxPending = vfxWriter,
                HasVfxWriter = hasVfx
            }.ScheduleParallel(state.Dependency);

            if (hasVfx)
            {
                vfx.ProducerHandle = JobHandle.CombineDependencies(vfx.ProducerHandle, pulseHandle);
            }

            state.Dependency = pulseHandle;
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active))]
        private partial struct AoePulseVfxJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            private void Execute(
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
                EnabledRefRO<CombatLifetimeComponent> lifetimeEnabled,
                ref AoePulseVfxComponent pulseVfx)
            {
                // pulse one-shots have CombatLifetimeComponent disabled — skip periodic pulse VFX for them
                if (pulseVfx.Interval <= 0f || !lifetimeEnabled.ValueRO)
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
