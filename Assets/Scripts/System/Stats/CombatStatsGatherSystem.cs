using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Stats
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatAoeVfxDispatchSystem))]
    [UpdateAfter(typeof(SoundEventDispatchSystem))]
    public partial class CombatStatsGatherSystem : SystemBase
    {
        private Entity _statsEntity;
        private EntityQuery activeProjectileRenderQuery;
        private EntityQuery activeAoeRenderQuery;
        private EntityQuery activeTargetedQuery;
        private EntityQuery templateRegistryQuery;

        protected override void OnCreate()
        {
            _statsEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_statsEntity, new CombatStatsSingleton
            {
                TargetedLinkCounts = new NativeQueue<int>(Allocator.Persistent)
            });
            EntityManager.AddComponentData(_statsEntity, new CombatStatsDisplaySingleton());

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

            activeTargetedQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TargetedTag>()
                .WithAll<Active>()
                .Build(this);

            templateRegistryQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileSpawnTemplate>()
                .WithAll<AoeSpawnTemplate>()
                .WithAll<TargetedSpawnTemplate>()
                .Build(this);
        }

        protected override void OnDestroy()
        {
            if (_statsEntity == Entity.Null || !EntityManager.Exists(_statsEntity))
            {
                return;
            }

            CombatStatsSingleton stats = EntityManager.GetComponentData<CombatStatsSingleton>(_statsEntity);
            stats.TargetedLinkProducerHandle.Complete();
            if (stats.TargetedLinkCounts.IsCreated)
            {
                stats.TargetedLinkCounts.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            // Producers accumulated their counts into the blackboard earlier this frame; this
            // system only reads it and fills in the render-active and template-registry counts it
            // owns, then writes the snapshot to CombatStatsSingleton and mirrors it to
            // CombatStatsDisplaySingleton.
            // Game-object readers (e.g. the debug overlay) pull the display mirror from the
            // world; the simulation no longer pushes to any Debugging type.
            CombatStatsSingleton snapshot = EntityManager.GetComponentData<CombatStatsSingleton>(_statsEntity);
            snapshot.ActiveProjectiles = activeProjectileRenderQuery.CalculateEntityCount();
            snapshot.ActiveAoes = activeAoeRenderQuery.CalculateEntityCount();
            snapshot.ActiveTargeted = activeTargetedQuery.CalculateEntityCount();
            snapshot.ProjectileTemplateRegistryEntries = 0;
            snapshot.AoeTemplateRegistryEntries = 0;
            snapshot.TargetedTemplateRegistryEntries = 0;
            if (templateRegistryQuery.CalculateEntityCount() == 1)
            {
                Entity scope = templateRegistryQuery.GetSingletonEntity();
                ProjectileSpawnTemplate projectileRegistry = EntityManager.GetComponentData<ProjectileSpawnTemplate>(scope);
                AoeSpawnTemplate aoeRegistry = EntityManager.GetComponentData<AoeSpawnTemplate>(scope);
                TargetedSpawnTemplate targetedRegistry = EntityManager.GetComponentData<TargetedSpawnTemplate>(scope);
                snapshot.ProjectileTemplateRegistryEntries = projectileRegistry.Map.IsCreated ? projectileRegistry.Map.Count : 0;
                snapshot.AoeTemplateRegistryEntries = aoeRegistry.Map.IsCreated ? aoeRegistry.Map.Count : 0;
                snapshot.TargetedTemplateRegistryEntries = targetedRegistry.Map.IsCreated ? targetedRegistry.Map.Count : 0;
            }
            snapshot.TargetedLinkProducerHandle.Complete();
            while (snapshot.TargetedLinkCounts.TryDequeue(out int links))
            {
                snapshot.TargetedLinksResolved += links;
            }

            snapshot.TargetedLinkProducerHandle = default;
            EntityManager.SetComponentData(_statsEntity, snapshot);

            // Publish a full copy to the display mirror. This is the only write this component
            // ever receives, so game-object readers never observe a reset/mid-accumulation value.
            EntityManager.SetComponentData(_statsEntity, new CombatStatsDisplaySingleton
            {
                EntitiesSpawnedViaEcb = snapshot.EntitiesSpawnedViaEcb,
                EntitiesSpawnedViaReuse = snapshot.EntitiesSpawnedViaReuse,
                TargetedEntitiesSpawned = snapshot.TargetedEntitiesSpawned,
                TargetedLinksResolved = snapshot.TargetedLinksResolved,
                ActiveProjectiles = snapshot.ActiveProjectiles,
                ActiveAoes = snapshot.ActiveAoes,
                ActiveTargeted = snapshot.ActiveTargeted,
                ProjectileTemplateRegistryEntries = snapshot.ProjectileTemplateRegistryEntries,
                AoeTemplateRegistryEntries = snapshot.AoeTemplateRegistryEntries,
                TargetedTemplateRegistryEntries = snapshot.TargetedTemplateRegistryEntries,
                HitEventsCreated = snapshot.HitEventsCreated,
                VfxEventsCreated = snapshot.VfxEventsCreated,
                SoundEventsCreated = snapshot.SoundEventsCreated,
                EntitiesDespawned = snapshot.EntitiesDespawned,
                EntitiesDeleted = snapshot.EntitiesDeleted
            });
        }
    }
}
