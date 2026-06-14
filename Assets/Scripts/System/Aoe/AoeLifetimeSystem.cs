using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeSimulationSystem))]
    [UpdateBefore(typeof(AoeCollisionSystem))]
    public partial struct AoeLifetimeSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            float deltaTime = SystemAPI.Time.DeltaTime;

            var lifetimeJob = new AoeLifetimeJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxPending.AsParallelWriter()
            };
            var pulseJob = new AoePulseVfxJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxPending.AsParallelWriter()
            };

            // pulseJob chains after lifetimeJob — both write to vfxPending.AsParallelWriter()
            // and NativeQueue<T> safety doesn't permit concurrent parallel writers across jobs.
            JobHandle lifetimeHandle = lifetimeJob.ScheduleParallel(state.Dependency);
            JobHandle pulseHandle = pulseJob.ScheduleParallel(lifetimeHandle);

            JobHandle vfxFlushHandle = new VfxFlushJob
            {
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(pulseHandle);

            state.Dependency = vfxPending.Dispose(vfxFlushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag))]
        private partial struct AoeLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
                ref AoeLifetimeComponent lifetime,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                if (lifetime.IsPulse == 1 || lifetime.RemainingLifetime <= 0f)
                {
                    return;
                }

                lifetime.RemainingLifetime -= DeltaTime;
                if (lifetime.RemainingLifetime > 0f)
                {
                    return;
                }

                lifetime.RemainingLifetime = 0f;
                active.ValueRW = false;
                renderActive.ValueRW = false;
                VfxPending.Enqueue(new VfxPendingSpawn
                {
                    Scope = identity.Scope,
                    TypeId = identity.TypeId,
                    Trigger = 2,
                    Position = kinematics.Position,
                    AreaSize = area.Size
                });
            }
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag))]
        private partial struct AoePulseVfxJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                in AoeIdentityComponent identity,
                in AoeLifetimeComponent lifetime,
                in CombatKinematicsComponent kinematics,
                in AoeAreaComponent area,
                ref AoePulseVfxComponent pulseVfx)
            {
                if (pulseVfx.Interval <= 0f || lifetime.IsPulse == 1)
                {
                    return;
                }

                pulseVfx.RemainingInterval -= DeltaTime;
                if (pulseVfx.RemainingInterval > 0f)
                {
                    return;
                }

                pulseVfx.RemainingInterval = pulseVfx.Interval;
                VfxPending.Enqueue(new VfxPendingSpawn
                {
                    Scope = identity.Scope,
                    TypeId = identity.TypeId,
                    Trigger = 3,
                    Position = kinematics.Position,
                    AreaSize = area.Size
                });
            }
        }
    }
}
