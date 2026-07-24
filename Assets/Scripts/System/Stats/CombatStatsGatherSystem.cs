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
    [UpdateAfter(typeof(CombatAoeVfxDispatchSystem))]
    public partial class CombatStatsGatherSystem : SystemBase
    {
        private Entity _statsEntity;
        private EntityQuery activeProjectileRenderQuery;
        private EntityQuery activeAoeRenderQuery;

        protected override void OnCreate()
        {
            _statsEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_statsEntity, new CombatStatsSingleton());

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

        protected override void OnUpdate()
        {
            // Producers accumulated their counts into the blackboard earlier this frame; this
            // system only reads it and fills in the render-active counts it owns, then writes the
            // snapshot to CombatStatsSingleton. Readers (e.g. the debug overlay) pull it from the
            // world; the simulation no longer pushes to any Debugging type.
            CombatStatsSingleton snapshot = EntityManager.GetComponentData<CombatStatsSingleton>(_statsEntity);
            snapshot.ActiveProjectiles = activeProjectileRenderQuery.CalculateEntityCount();
            snapshot.ActiveAoes = activeAoeRenderQuery.CalculateEntityCount();
            EntityManager.SetComponentData(_statsEntity, snapshot);
        }
    }
}
