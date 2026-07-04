using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: singleton cleanup tunable; created by CombatPoolCleanupSystem on create.
    public struct CombatPoolCleanupConfig : IComponentData
    {
        // A chunk has its disabled (pooled) entities destroyed when it holds fewer than
        // this many active entities. Higher = more aggressive defrag / smaller reuse buffer.
        // Chunks at or above the threshold keep their disabled entities as a warm reuse pool.
        public int ChunkActiveThreshold;

        public static CombatPoolCleanupConfig Default => new CombatPoolCleanupConfig
        {
            ChunkActiveThreshold = 32
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
                ActiveThreshold = cfg.ChunkActiveThreshold,
                Ecb = ecb.AsParallelWriter()
            }.ScheduleParallel(_poolQuery, Dependency);

            Dependency.Complete();
            ecb.Playback(EntityManager);
            ecb.Dispose();

            LastDeletedCount = before - _poolQuery.CalculateEntityCount();
        }

        [BurstCompile]
        private struct PoolTrimJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<Active> ActiveHandle;
            public int ActiveThreshold;
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
                if (activeCount >= ActiveThreshold)
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
