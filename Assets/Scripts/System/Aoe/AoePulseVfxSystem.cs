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
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public partial struct AoePulseVfxSystem : ISystem
    {
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        public void OnUpdate(ref SystemState state)
        {
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);

            JobHandle pulseHandle = new AoePulseVfxJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                VfxPending = vfxPending.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            JobHandle vfxFlushHandle = new VfxFlushJob
            {
                Scope = scopeQuery.GetSingletonEntity(),
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(pulseHandle);

            state.Dependency = vfxPending.Dispose(vfxFlushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag))]
        private partial struct AoePulseVfxJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

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
                VfxPending.Enqueue(new VfxPendingSpawn
                {
                    Faction = identity.Faction,
                    TypeId = identity.TypeId,
                    Trigger = 3,
                    Position = kinematics.Position,
                    AreaSize = area.Size
                });
            }
        }
    }
}
