using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct AoeSimulationSystem : ISystem
    {
        private EntityQuery activeAoeQuery;
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            activeAoeQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeActiveTag>());
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<AoeScope>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (scopeQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < scopes.Length; i++)
            {
                state.EntityManager.GetBuffer<AoeHitElement>(scopes[i]).Clear();
            }

            scopes.Dispose();

            if (activeAoeQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            var recycled = new NativeQueue<AoePendingRecycle>(Allocator.TempJob);
            var job = new AoeLifetimeTickJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Recycled = recycled.AsParallelWriter()
            };

            JobHandle lifetimeHandle = job.ScheduleParallel(state.Dependency);
            JobHandle recycleFlushHandle = new AoeRecycleFlushJob
            {
                Recycled = recycled,
                RecycleBuffers = SystemAPI.GetBufferLookup<AoeRecycleElement>()
            }.Schedule(lifetimeHandle);

            state.Dependency = recycled.Dispose(recycleFlushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag))]
        private partial struct AoeLifetimeTickJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<AoePendingRecycle>.ParallelWriter Recycled;

            private void Execute(
                Entity entity,
                ref AoeLifetimeComponent lifetime,
                in AoeIdentityComponent identity,
                in CombatRenderElement renderElement,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                if (lifetime.IsPulse == 1)
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
                Recycled.Enqueue(new AoePendingRecycle
                {
                    Scope = identity.Scope,
                    AoeEntity = entity,
                    TypeId = identity.TypeId,
                    Render = renderElement
                });
            }
        }

        [BurstCompile]
        private struct AoeRecycleFlushJob : IJob
        {
            public NativeQueue<AoePendingRecycle> Recycled;
            public BufferLookup<AoeRecycleElement> RecycleBuffers;

            public void Execute()
            {
                while (Recycled.TryDequeue(out AoePendingRecycle recycle))
                {
                    if (recycle.Scope == Entity.Null || !RecycleBuffers.HasBuffer(recycle.Scope))
                    {
                        continue;
                    }

                    RecycleBuffers[recycle.Scope].Add(new AoeRecycleElement
                    {
                        AoeEntity = recycle.AoeEntity,
                        TypeId = recycle.TypeId,
                        Render = recycle.Render
                    });
                }
            }
        }
    }
}
