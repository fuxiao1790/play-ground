using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetSpatialHashSystem))]
    public partial class TargetProxyUpdateApplySystem : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxyUpdateEvent>());
        }

        protected override void OnUpdate()
        {
            NativeArray<ArchetypeChunk> scopeChunks = scopeQuery.ToArchetypeChunkArray(Allocator.TempJob);
            Dependency = new ApplyProxyUpdatesJob
            {
                ScopeChunks = scopeChunks,
                EventHandle = GetBufferTypeHandle<TargetProxyUpdateEvent>(false),
                PositionLookup = GetComponentLookup<TargetPosition>(false),
                ShapeLookup = GetComponentLookup<TargetCollisionShape>(false),
                HealthLookup = GetComponentLookup<Health>(false),
                ManaLookup = GetComponentLookup<Mana>(false)
            }.Schedule(Dependency);
            Dependency = scopeChunks.Dispose(Dependency);
        }

        [BurstCompile]
        private struct ApplyProxyUpdatesJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> ScopeChunks;
            public BufferTypeHandle<TargetProxyUpdateEvent> EventHandle;
            public ComponentLookup<TargetPosition> PositionLookup;
            public ComponentLookup<TargetCollisionShape> ShapeLookup;
            public ComponentLookup<Health> HealthLookup;
            public ComponentLookup<Mana> ManaLookup;

            public void Execute()
            {
                for (int chunkIndex = 0; chunkIndex < ScopeChunks.Length; chunkIndex++)
                {
                    BufferAccessor<TargetProxyUpdateEvent> accessor =
                        ScopeChunks[chunkIndex].GetBufferAccessor(ref EventHandle);
                    for (int bufferIndex = 0; bufferIndex < accessor.Length; bufferIndex++)
                    {
                        DynamicBuffer<TargetProxyUpdateEvent> buffer = accessor[bufferIndex];
                        for (int eventIndex = 0; eventIndex < buffer.Length; eventIndex++)
                        {
                            Apply(buffer[eventIndex]);
                        }

                        buffer.Clear();
                    }
                }
            }

            private void Apply(in TargetProxyUpdateEvent updateEvent)
            {
                switch (updateEvent.Kind)
                {
                    case TargetProxyUpdateKind.Push:
                        if (PositionLookup.HasComponent(updateEvent.Proxy)
                            && ShapeLookup.HasComponent(updateEvent.Proxy))
                        {
                            PositionLookup[updateEvent.Proxy] = updateEvent.Position;
                            ShapeLookup[updateEvent.Proxy] = updateEvent.Shape;
                        }
                        break;

                    case TargetProxyUpdateKind.PushResourceMaxes:
                        if (HealthLookup.HasComponent(updateEvent.Proxy)
                            && ManaLookup.HasComponent(updateEvent.Proxy))
                        {
                            Health health = HealthLookup[updateEvent.Proxy];
                            health.Max = updateEvent.MaxHealth;
                            health.RegenPerSecond = updateEvent.HealthRegenPerSecond;
                            health.Current = math.clamp(health.Current, 0f, health.Max);
                            HealthLookup[updateEvent.Proxy] = health;

                            Mana mana = ManaLookup[updateEvent.Proxy];
                            mana.Max = updateEvent.MaxMana;
                            mana.RegenPerSecond = updateEvent.ManaRegenPerSecond;
                            mana.Current = math.clamp(mana.Current, 0f, mana.Max);
                            ManaLookup[updateEvent.Proxy] = mana;
                        }
                        break;

                    case TargetProxyUpdateKind.SetHealth:
                        if (HealthLookup.HasComponent(updateEvent.Proxy))
                        {
                            Health health = HealthLookup[updateEvent.Proxy];
                            health.Current = math.clamp(updateEvent.CurrentValue, 0f, health.Max);
                            HealthLookup[updateEvent.Proxy] = health;
                        }
                        break;

                    case TargetProxyUpdateKind.SetMana:
                        if (ManaLookup.HasComponent(updateEvent.Proxy))
                        {
                            Mana mana = ManaLookup[updateEvent.Proxy];
                            mana.Current = math.clamp(updateEvent.CurrentValue, 0f, mana.Max);
                            ManaLookup[updateEvent.Proxy] = mana;
                        }
                        break;
                }
            }
        }
    }
}
