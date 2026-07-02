using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Entities;

namespace PlayGround.System.Stats
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatVfxDispatchSystem))]
    [UpdateAfter(typeof(CombatBatchedRenderSystem))]
    public partial class CombatStatsGatherSystem : SystemBase
    {
        private Entity _statsEntity;
        private ProjectileSpawnApplySystem _projectiles;
        private ImpactAoeSpawnApplySystem _impactAoe;
        private LingeringAoeSpawnApplySystem _lingeringAoe;
        private CombatApplyFinalizeSingleSystem _finalize;
        private CombatVfxDispatchSystem _vfx;
        private CombatBatchedRenderSystem _render;

        protected override void OnCreate()
        {
            _statsEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_statsEntity, new CombatStatsSingleton());
            EntityManager.AddComponentObject(_statsEntity, new CombatStatsBinding());
        }

        internal void Bind(global::PerformanceText display)
        {
            if (_statsEntity != Entity.Null && EntityManager.Exists(_statsEntity))
            {
                EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity).Display = display;
            }
        }

        internal void Unbind(global::PerformanceText display)
        {
            if (_statsEntity != Entity.Null && EntityManager.Exists(_statsEntity))
            {
                CombatStatsBinding binding =
                    EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity);
                if (binding.Display == display)
                {
                    binding.Display = null;
                }
            }
        }

        protected override void OnUpdate()
        {
            CacheProducerSystems();

            int ecb =
                (_projectiles?.LastColdCreateCount ?? 0) +
                (_impactAoe?.LastColdCreateCount ?? 0) +
                (_lingeringAoe?.LastColdCreateCount ?? 0);
            int reuse =
                (_projectiles?.LastReuseCount ?? 0) +
                (_impactAoe?.LastReuseCount ?? 0) +
                (_lingeringAoe?.LastReuseCount ?? 0);

            var snapshot = new CombatStatsSingleton
            {
                EntitiesSpawnedViaEcb = ecb,
                EntitiesSpawnedViaReuse = reuse,
                ActiveProjectiles = _render?.LastActiveProjectileCount ?? 0,
                ActiveAoes = _render?.LastActiveAoeCount ?? 0,
                HitEventsCreated = _finalize?.LastHitEventCount ?? 0,
                VfxEventsCreated = _vfx?.LastVfxEventCount ?? 0,
            };

            EntityManager.SetComponentData(_statsEntity, snapshot);

            CombatStatsBinding binding =
                EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity);
            binding.Display?.Apply(in snapshot);
        }

        private void CacheProducerSystems()
        {
            _projectiles ??= World.GetExistingSystemManaged<ProjectileSpawnApplySystem>();
            _impactAoe ??= World.GetExistingSystemManaged<ImpactAoeSpawnApplySystem>();
            _lingeringAoe ??= World.GetExistingSystemManaged<LingeringAoeSpawnApplySystem>();
            _finalize ??= World.GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>();
            _vfx ??= World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
            _render ??= World.GetExistingSystemManaged<CombatBatchedRenderSystem>();
        }
    }
}
