using NUnit.Framework;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Targeted;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.Tests.PlayMode
{
    public sealed class CombatStatsGatherSystemTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private CombatStatsGatherSystem gatherSystem;
        private CombatStatsResetSystem resetSystem;
        private NativeHashMap<Hash128, ProjectileSpawnCommand> projectileTemplateMap;
        private NativeHashMap<Hash128, AoeSpawnCommand> aoeTemplateMap;
        private NativeHashMap<Hash128, TargetedSpawnCommand> targetedTemplateMap;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CombatStatsGatherSystemTest");
            entityManager = testWorld.EntityManager;
            gatherSystem = testWorld.GetOrCreateSystemManaged<CombatStatsGatherSystem>();
            resetSystem = testWorld.GetOrCreateSystemManaged<CombatStatsResetSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (projectileTemplateMap.IsCreated)
                projectileTemplateMap.Dispose();

            if (aoeTemplateMap.IsCreated)
                aoeTemplateMap.Dispose();

            if (targetedTemplateMap.IsCreated)
                targetedTemplateMap.Dispose();

            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
        }

        private Entity StatsEntity()
        {
            return entityManager
                .CreateEntityQuery(ComponentType.ReadOnly<CombatStatsSingleton>())
                .GetSingletonEntity();
        }

        [Test]
        public void GatherMirrorsAccumulatedValuesIntoDisplaySingleton()
        {
            Entity statsEntity = StatsEntity();
            entityManager.SetComponentData(statsEntity, new CombatStatsSingleton
            {
                EntitiesSpawnedViaEcb = 11,
                EntitiesSpawnedViaReuse = 22,
                HitEventsCreated = 5,
                VfxEventsCreated = 6,
                EntitiesDespawned = 7,
                EntitiesDeleted = 8
            });

            gatherSystem.Update();

            CombatStatsDisplaySingleton display = entityManager.GetComponentData<CombatStatsDisplaySingleton>(statsEntity);
            Assert.That(display.EntitiesSpawnedViaEcb, Is.EqualTo(11));
            Assert.That(display.EntitiesSpawnedViaReuse, Is.EqualTo(22));
            Assert.That(display.HitEventsCreated, Is.EqualTo(5));
            Assert.That(display.VfxEventsCreated, Is.EqualTo(6));
            Assert.That(display.EntitiesDespawned, Is.EqualTo(7));
            Assert.That(display.EntitiesDeleted, Is.EqualTo(8));
            Assert.That(display.ActiveProjectiles, Is.Zero);
            Assert.That(display.ActiveAoes, Is.Zero);
        }

        [Test]
        public void GatherMirrorsSharedScopeTemplateRegistrySizesIntoDisplaySingleton()
        {
            Entity scope = entityManager.CreateEntity();
            projectileTemplateMap = new NativeHashMap<Hash128, ProjectileSpawnCommand>(4, Allocator.Persistent);
            aoeTemplateMap = new NativeHashMap<Hash128, AoeSpawnCommand>(4, Allocator.Persistent);
            targetedTemplateMap = new NativeHashMap<Hash128, TargetedSpawnCommand>(4, Allocator.Persistent);
            projectileTemplateMap.TryAdd(new Hash128(1u, 0u, 0u, 0u), default);
            projectileTemplateMap.TryAdd(new Hash128(2u, 0u, 0u, 0u), default);
            aoeTemplateMap.TryAdd(new Hash128(3u, 0u, 0u, 0u), default);
            targetedTemplateMap.TryAdd(new Hash128(4u, 0u, 0u, 0u), default);
            targetedTemplateMap.TryAdd(new Hash128(5u, 0u, 0u, 0u), default);
            targetedTemplateMap.TryAdd(new Hash128(6u, 0u, 0u, 0u), default);
            entityManager.AddComponentData(scope, new ProjectileSpawnTemplate { Map = projectileTemplateMap });
            entityManager.AddComponentData(scope, new AoeSpawnTemplate { Map = aoeTemplateMap });
            entityManager.AddComponentData(scope, new TargetedSpawnTemplate { Map = targetedTemplateMap });

            gatherSystem.Update();

            CombatStatsDisplaySingleton display = entityManager.GetComponentData<CombatStatsDisplaySingleton>(StatsEntity());
            Assert.That(display.ProjectileTemplateRegistryEntries, Is.EqualTo(2));
            Assert.That(display.AoeTemplateRegistryEntries, Is.EqualTo(1));
            Assert.That(display.TargetedTemplateRegistryEntries, Is.EqualTo(3));
        }

        [Test]
        public void ResetZeroesInternalAccumulatorButLeavesDisplayMirrorIntact()
        {
            Entity statsEntity = StatsEntity();
            entityManager.SetComponentData(statsEntity, new CombatStatsSingleton
            {
                EntitiesSpawnedViaEcb = 3,
                HitEventsCreated = 4
            });

            gatherSystem.Update();
            resetSystem.Update();

            CombatStatsSingleton internalStats = entityManager.GetComponentData<CombatStatsSingleton>(statsEntity);
            CombatStatsDisplaySingleton display = entityManager.GetComponentData<CombatStatsDisplaySingleton>(statsEntity);

            Assert.That(internalStats.EntitiesSpawnedViaEcb, Is.Zero,
                "The frame reset must still zero the internal accumulator.");
            Assert.That(internalStats.HitEventsCreated, Is.Zero);
            Assert.That(display.EntitiesSpawnedViaEcb, Is.EqualTo(3),
                "Display mirror must survive the frame reset; only CombatStatsGatherSystem may write to it.");
            Assert.That(display.HitEventsCreated, Is.EqualTo(4));
        }
    }
}
