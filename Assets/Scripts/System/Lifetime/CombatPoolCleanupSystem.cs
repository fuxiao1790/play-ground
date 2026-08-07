using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Lifetime
{
    // ECS Lifecycle: singleton cleanup tunable; created by CombatPoolCleanupSystem on create.
    public struct CombatPoolCleanupConfig : IComponentData
    {
        // Percentage of active entities in a chunk (0-100). Chunks with active count at or above
        // this threshold keep their disabled entities as a warm reuse pool. Higher = more aggressive
        // defrag / smaller reuse buffer (e.g., 50 means keep pool only if 50%+ entities are active).
        public float ChunkActiveThresholdPercent;

        // Scene calm-down gate. Cleanup runs only while the smoothed despawn rate exceeds the
        // smoothed spawn rate by this factor (e.g., 1.25 = despawns must beat spawns by 25%).
        // A climbing scene (spawns ahead) and a busy equilibrium (rates roughly equal) never
        // trim; trimming happens during the wind-down, while the scene is still calming.
        public float DespawnOverSpawnMargin;

        // Time constant in seconds for the exponential smoothing of both rates. Larger = steadier
        // gate that ignores bursty spawn patterns but reacts slower to a real wind-down.
        public float RateSmoothingTime;

        public static CombatPoolCleanupConfig Default => new CombatPoolCleanupConfig
        {
            ChunkActiveThresholdPercent = 5f,
            DespawnOverSpawnMargin = 1.25f,
            RateSmoothingTime = 0.5f
        };
    }

    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial class CombatPoolCleanupSystem : SystemBase
    {
        private EntityQuery _poolQuery;
        private EntityQuery _targetedPoolQuery;
        private EntityQuery _lingeringTargetedPoolQuery;
        private EntityTypeHandle _entityHandle;
        private ComponentTypeHandle<Active> _activeHandle;

        // Smoothed despawns/frame must also clear this absolute floor before the gate opens, so
        // zero-vs-zero rate noise in a dead-calm scene cannot trigger trim passes.
        private const float CalmDespawnFloor = 0.5f;

        // Calm-down gate state: EMA-smoothed spawn/despawn rates (entities per frame) plus the
        // previous frame's raw readings the conservation math needs.
        private float _spawnRateEma;
        private float _despawnRateEma;
        private int _prevActiveLoad;
        private int _prevSpawns;

        // Pool entities destroyed on the last update; pulled by CombatStatsGatherSystem for the overlay.
        internal int LastDeletedCount;

        protected override void OnCreate()
        {
            if (!SystemAPI.HasSingleton<CombatPoolCleanupConfig>())
            {
                Entity configEntity = EntityManager.CreateEntity(typeof(CombatPoolCleanupConfig));
                EntityManager.SetComponentData(configEntity, CombatPoolCleanupConfig.Default);
            }

            // Projectile/AOE pools share the existing query. Targeted pools stay split by their
            // lingering discriminator so single-hit and interval slots trim independently.
            _poolQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Active>()
                .WithAny<ProjectileTag, AoeTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);
            _targetedPoolQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Active>()
                .WithAll<TargetedTag>()
                .WithNone<LingeringTargetedTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);
            _lingeringTargetedPoolQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Active>()
                .WithAll<TargetedTag>()
                .WithAll<LingeringTargetedTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);

            _entityHandle = GetEntityTypeHandle();
            _activeHandle = GetComponentTypeHandle<Active>(true);
        }

        protected override void OnUpdate()
        {
            LastDeletedCount = 0;

            CombatPoolCleanupConfig cfg = SystemAPI.GetSingleton<CombatPoolCleanupConfig>();

            // Scene calm-down gate, evaluated before any query work so a gated frame costs one
            // singleton lookup and a few float ops. Spawns/frame come straight from the stats
            // singleton (spawn-apply systems finished accumulating earlier this frame). Despawns
            // are never counted anywhere; conservation derives them — only spawns and despawns
            // move the active count (cleanup destroys already-disabled slots only):
            //   despawns = spawns - (active - prevActive)
            // Active counts are gathered in Presentation and read one frame stale here, so the
            // derivation is aligned to the previous frame via _prevSpawns; the residual skew
            // washes out in the EMAs. Worlds without the stats singleton (tests, stripped worlds)
            // skip the gate and trim unconditionally, same as the old busy-gate fallback.
            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> loadStats))
            {
                int activeLoad = loadStats.ValueRO.ActiveProjectiles
                    + loadStats.ValueRO.ActiveAoes
                    + loadStats.ValueRO.ActiveTargeted;
                int spawns = loadStats.ValueRO.EntitiesSpawnedViaEcb + loadStats.ValueRO.EntitiesSpawnedViaReuse;
                int despawns = math.max(0, _prevSpawns - (activeLoad - _prevActiveLoad));
                loadStats.ValueRW.EntitiesDespawned += despawns;

                float alpha = 1f - math.exp(-SystemAPI.Time.DeltaTime / math.max(cfg.RateSmoothingTime, 1e-3f));
                _spawnRateEma = math.lerp(_spawnRateEma, _prevSpawns, alpha);
                _despawnRateEma = math.lerp(_despawnRateEma, despawns, alpha);
                _prevActiveLoad = activeLoad;
                _prevSpawns = spawns;

                // Climbing (spawns ahead) and busy equilibrium (rates roughly equal) keep the gate
                // closed; only a wind-down — despawns clearly ahead of spawns — opens it.
                if (_despawnRateEma <= _spawnRateEma * cfg.DespawnOverSpawnMargin + CalmDespawnFloor)
                {
                    return;
                }
            }

            if (_poolQuery.IsEmpty
                && _targetedPoolQuery.IsEmpty
                && _lingeringTargetedPoolQuery.IsEmpty)
            {
                return;
            }

            // Only disabled entities are destroyed and nothing else mutates the pool mid-update,
            // so the drop in total pool count equals the number deleted.
            int before = _poolQuery.CalculateEntityCount()
                + _targetedPoolQuery.CalculateEntityCount()
                + _lingeringTargetedPoolQuery.CalculateEntityCount();

            _entityHandle.Update(this);
            _activeHandle.Update(this);

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);
            EntityCommandBuffer.ParallelWriter ecbWriter = ecb.AsParallelWriter();

            Dependency = ScheduleTrim(_poolQuery, cfg, ecbWriter, Dependency);
            Dependency = ScheduleTrim(_targetedPoolQuery, cfg, ecbWriter, Dependency);
            Dependency = ScheduleTrim(_lingeringTargetedPoolQuery, cfg, ecbWriter, Dependency);

            Dependency.Complete();
            ecb.Playback(EntityManager);
            ecb.Dispose();

            LastDeletedCount = before
                - _poolQuery.CalculateEntityCount()
                - _targetedPoolQuery.CalculateEntityCount()
                - _lingeringTargetedPoolQuery.CalculateEntityCount();

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.EntitiesDeleted += LastDeletedCount;
            }
        }

        private JobHandle ScheduleTrim(
            EntityQuery poolQuery,
            in CombatPoolCleanupConfig cfg,
            EntityCommandBuffer.ParallelWriter ecb,
            JobHandle dependency)
        {
            return new PoolTrimJob
            {
                EntityHandle = _entityHandle,
                ActiveHandle = _activeHandle,
                ActiveThresholdPercent = cfg.ChunkActiveThresholdPercent,
                Ecb = ecb
            }.ScheduleParallel(poolQuery, dependency);
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
