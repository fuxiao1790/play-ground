using PlayGround.System.Common;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;

namespace PlayGround.System.Aoe
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayGround.System.Projectile.ProjectileCollisionSystem))]
    [UpdateBefore(typeof(AoeCollisionSystem))]
    public partial struct AoeContactGateSystem : ISystem
    {
        private EntityQuery activeGateQuery;
        private BufferTypeHandle<AoeContactGateElement> contactGateHandle;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            activeGateQuery = new EntityQueryBuilder(Unity.Collections.Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<AoeCollisionActiveTag>()
                .WithAllRW<AoeContactGateElement>()
                .Build(ref state);
            state.RequireForUpdate(activeGateQuery);

            contactGateHandle = state.GetBufferTypeHandle<AoeContactGateElement>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            contactGateHandle.Update(ref state);

            state.Dependency = new AoeContactGateJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ContactGateHandle = contactGateHandle
            }.ScheduleParallel(activeGateQuery, state.Dependency);
        }

        [BurstCompile]
        private struct AoeContactGateJob : IJobChunk
        {
            public float DeltaTime;
            public BufferTypeHandle<AoeContactGateElement> ContactGateHandle;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                BufferAccessor<AoeContactGateElement> gates = chunk.GetBufferAccessor(ref ContactGateHandle);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    TickGates(gates[i]);
                }
            }

            private void TickGates(DynamicBuffer<AoeContactGateElement> contactGates)
            {
                // Pass 1: subtract DeltaTime from all cooldowns (no branches; Burst can vectorize).
                for (int i = 0; i < contactGates.Length; i++)
                    contactGates.ElementAt(i).CooldownRemaining -= DeltaTime;

                // Pass 2: compact expired gates (structural mutation kept separate).
                for (int i = contactGates.Length - 1; i >= 0; i--)
                    if (contactGates[i].CooldownRemaining <= 0f)
                        contactGates.RemoveAt(i);
            }
        }
    }
}
