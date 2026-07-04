using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial class CombatPoolCleanupSystem : SystemBase
    {
        private EntityQuery[] _allPoolQueries;
        private EntityQuery[] _disabledPoolQueries;
        private int _nextPoolStart;

        protected override void OnCreate()
        {
            if (!SystemAPI.HasSingleton<CombatPoolCleanupConfig>())
            {
                Entity configEntity = EntityManager.CreateEntity(typeof(CombatPoolCleanupConfig));
                EntityManager.SetComponentData(configEntity, CombatPoolCleanupConfig.Default);
            }

            _allPoolQueries = new[]
            {
                BuildProjectileQuery(includeDisabledOnly: false),
                BuildImpactAoeQuery(includeDisabledOnly: false),
                BuildLingeringAoeQuery(includeDisabledOnly: false)
            };

            _disabledPoolQueries = new[]
            {
                BuildProjectileQuery(includeDisabledOnly: true),
                BuildImpactAoeQuery(includeDisabledOnly: true),
                BuildLingeringAoeQuery(includeDisabledOnly: true)
            };
        }

        protected override void OnUpdate()
        {
            CombatPoolCleanupConfig cfg = SystemAPI.GetSingleton<CombatPoolCleanupConfig>();
            CombatFrameClock clock = SystemAPI.GetSingleton<CombatFrameClock>();

            if (clock.SmoothedFrameMs > 0f && clock.SmoothedFrameMs >= cfg.BudgetMs)
            {
                return;
            }

            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            double elapsedMs = (now - clock.FrameStartTime) * 1000.0;
            if (elapsedMs >= cfg.BudgetMs)
            {
                return;
            }

            CompleteDependency();

            int remaining = math.max(0, cfg.MaxDeletesPerFrame);
            if (remaining <= 0)
            {
                return;
            }

            double sliceStart = UnityEngine.Time.realtimeSinceStartupAsDouble;
            int start = _nextPoolStart % _allPoolQueries.Length;
            int visited = 0;

            for (int offset = 0; offset < _allPoolQueries.Length; offset++)
            {
                if (remaining <= 0)
                {
                    break;
                }

                int poolIndex = (start + offset) % _allPoolQueries.Length;
                CountPool(_allPoolQueries[poolIndex], out int active, out int disabled);
                visited++;

                int deleted = TryTrimPool(
                    _disabledPoolQueries[poolIndex],
                    active,
                    disabled,
                    cfg,
                    remaining);
                remaining -= deleted;

                if (cfg.SliceMs > 0f)
                {
                    double sliceElapsedMs = (UnityEngine.Time.realtimeSinceStartupAsDouble - sliceStart) * 1000.0;
                    if (sliceElapsedMs >= cfg.SliceMs)
                    {
                        break;
                    }
                }
            }

            _nextPoolStart = (_nextPoolStart + math.max(1, visited)) % _allPoolQueries.Length;
        }

        private EntityQuery BuildProjectileQuery(bool includeDisabledOnly)
        {
            EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>();

            return BuildPoolQuery(builder, includeDisabledOnly);
        }

        private EntityQuery BuildImpactAoeQuery(bool includeDisabledOnly)
        {
            EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithNone<CombatLifetimeComponent>();

            return BuildPoolQuery(builder, includeDisabledOnly);
        }

        private EntityQuery BuildLingeringAoeQuery(bool includeDisabledOnly)
        {
            EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<CombatLifetimeComponent>();

            return BuildPoolQuery(builder, includeDisabledOnly);
        }

        private EntityQuery BuildPoolQuery(EntityQueryBuilder builder, bool includeDisabledOnly)
        {
            if (includeDisabledOnly)
            {
                return builder
                    .WithDisabled<Active>()
                    .Build(this);
            }

            return builder
                .WithAll<Active>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);
        }

        private void CountPool(EntityQuery query, out int active, out int disabled)
        {
            active = 0;
            disabled = 0;
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (EntityManager.IsComponentEnabled<Active>(entities[i]))
                {
                    active++;
                }
                else
                {
                    disabled++;
                }
            }
        }

        private int TryTrimPool(
            EntityQuery disabledQuery,
            int active,
            int disabled,
            CombatPoolCleanupConfig cfg,
            int remaining)
        {
            float ratioFloor = active * cfg.PoolRatioMultiplier;
            if (disabled <= cfg.RetentionTarget || disabled <= ratioFloor)
            {
                return 0;
            }

            int floor = math.max(cfg.RetentionTarget, (int)math.ceil(ratioFloor));
            int cap = math.min(cfg.PerPoolDeleteCap, remaining);
            int toDelete = math.clamp(disabled - floor, 0, cap);
            if (toDelete <= 0)
            {
                return 0;
            }

            using NativeArray<Entity> entities = disabledQuery.ToEntityArray(Allocator.Temp);
            int deleteCount = math.min(toDelete, entities.Length);
            if (deleteCount > 0)
            {
                EntityManager.DestroyEntity(entities.GetSubArray(0, deleteCount));
            }

            return deleteCount;
        }
    }
}
