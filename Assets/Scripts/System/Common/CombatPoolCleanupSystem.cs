using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using PlayGround.System.Stats;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: singleton cleanup tunable; created by CombatPoolCleanupSystem on create.
    public struct CombatPoolCleanupConfig : IComponentData
    {
        // Percentage of active entities in a chunk (0-100). Chunks with active count at or above
        // this threshold keep their disabled entities as a warm reuse pool. Higher = more aggressive
        // defrag / smaller reuse buffer (e.g., 50 means keep pool only if 50%+ entities are active).
        public float ChunkActiveThresholdPercent;

        public static CombatPoolCleanupConfig Default => new CombatPoolCleanupConfig
        {
            ChunkActiveThresholdPercent = 20f
        };
    }

    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial class CombatPoolCleanupSystem : SystemBase
    {
        private EntityQuery _poolQuery;
        private EntityTypeHandle _entityHandle;
        private ComponentTypeHandle<Active> _activeHandle;

        // Pool entities destroyed on the last update; pulled by CombatStatsGatherSystem for the overlay.
        internal int LastDeletedCount;

        protected override void OnCreate()
        {
            if (!SystemAPI.HasSingleton<CombatPoolCleanupConfig>())
            {
                Entity configEntity = EntityManager.CreateEntity(typeof(CombatPoolCleanupConfig));
                EntityManager.SetComponentData(configEntity, CombatPoolCleanupConfig.Default);
            }

            // One query spans every reuse pool (projectiles + both AOE archetypes). Chunks are
            // already archetype-separated, so the per-chunk trim decision is pool-agnostic.
            _poolQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Active>()
                .WithAny<ProjectileTag, AoeTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);

            _entityHandle = GetEntityTypeHandle();
            _activeHandle = GetComponentTypeHandle<Active>(true);
        }

        protected override void OnUpdate()
        {
            LastDeletedCount = 0;

            if (_poolQuery.IsEmpty)
            {
                return;
            }

            CombatPoolCleanupConfig cfg = SystemAPI.GetSingleton<CombatPoolCleanupConfig>();

            _entityHandle.Update(this);
            _activeHandle.Update(this);

            // Only disabled entities are destroyed and nothing else mutates the pool mid-update,
            // so the drop in total pool count equals the number deleted.
            int before = _poolQuery.CalculateEntityCount();

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

            Dependency = new PoolTrimJob
            {
                EntityHandle = _entityHandle,
                ActiveHandle = _activeHandle,
                ActiveThresholdPercent = cfg.ChunkActiveThresholdPercent,
                Ecb = ecb.AsParallelWriter()
            }.ScheduleParallel(_poolQuery, Dependency);

            Dependency.Complete();
            ecb.Playback(EntityManager);
            ecb.Dispose();

            LastDeletedCount = before - _poolQuery.CalculateEntityCount();

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.EntitiesDeleted += LastDeletedCount;
            }
        }

        [BurstCompile]
        private struct PoolTrimJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<Active> ActiveHandle;
            public float ActiveThresholdPercent;
            public EntityCommandBuffer.ParallelWriter Ecb;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);

                int count = chunk.Count;
                int activeCount = 0;
                for (int i = 0; i < count; i++)
                {
                    if (activeMask[i])
                    {
                        activeCount++;
                    }
                }

                // Busy chunk: leave its disabled entities as a warm reuse buffer.
                int threshold = (int)(count * ActiveThresholdPercent / 100f);
                if (activeCount >= threshold)
                {
                    return;
                }

                NativeArray<Entity> entities = chunk.GetNativeArray(EntityHandle);
                for (int i = 0; i < count; i++)
                {
                    if (!activeMask[i])
                    {
                        Ecb.DestroyEntity(unfilteredChunkIndex, entities[i]);
                    }
                }
            }
        }
    }
}
