using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Stats
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatVfxDispatchSystem))]
    public partial class CombatStatsGatherSystem : SystemBase
    {
        private Entity _statsEntity;
        private EntityQuery activeProjectileRenderQuery;
        private EntityQuery activeAoeRenderQuery;

        protected override void OnCreate()
        {
            _statsEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_statsEntity, new CombatStatsSingleton());
            EntityManager.AddComponentObject(_statsEntity, new CombatStatsBinding());

            activeProjectileRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderComponent>()
                .WithAll<Active>()
                .WithAll<ProjectileTag>()
                .Build(this);

            activeAoeRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderComponent>()
                .WithAll<Active>()
                .WithAll<AoeTag>()
                .Build(this);
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
            // Producers accumulated their counts into the blackboard earlier this frame; this
            // system only reads it and fills in the render-active counts it owns, then displays.
            CombatStatsSingleton snapshot = EntityManager.GetComponentData<CombatStatsSingleton>(_statsEntity);
            snapshot.ActiveProjectiles = activeProjectileRenderQuery.CalculateEntityCount();
            snapshot.ActiveAoes = activeAoeRenderQuery.CalculateEntityCount();
            EntityManager.SetComponentData(_statsEntity, snapshot);

            CombatStatsBinding binding =
                EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity);
            binding.Display?.Apply(in snapshot);
        }
    }
}
