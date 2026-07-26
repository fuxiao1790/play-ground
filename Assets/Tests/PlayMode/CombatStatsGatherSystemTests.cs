using NUnit.Framework;
using PlayGround.System.Combat.Stats;
using Unity.Entities;

namespace PlayGround.Tests.PlayMode
{
    public sealed class CombatStatsGatherSystemTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private CombatStatsGatherSystem gatherSystem;
        private CombatStatsResetSystem resetSystem;

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
