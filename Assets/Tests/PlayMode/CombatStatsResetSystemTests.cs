using NUnit.Framework;
using PlayGround.System.Combat.Stats;
using Unity.Entities;

namespace PlayGround.Tests.PlayMode
{
    public sealed class CombatStatsResetSystemTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private CombatStatsResetSystem resetSystem;
        private Entity statsEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CombatStatsResetSystemTest");
            entityManager = testWorld.EntityManager;
            resetSystem = testWorld.GetOrCreateSystemManaged<CombatStatsResetSystem>();
            statsEntity = entityManager.CreateEntity(typeof(CombatStatsSingleton));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
        }

        [Test]
        public void ResetZeroesAccumulatorsButPreservesActiveLevels()
        {
            entityManager.SetComponentData(statsEntity, new CombatStatsSingleton
            {
                EntitiesSpawnedViaEcb = 11,
                EntitiesSpawnedViaReuse = 22,
                ActiveProjectiles = 333,
                ActiveAoes = 44,
                HitEventsCreated = 5,
                VfxEventsCreated = 6,
                EntitiesDespawned = 7,
                EntitiesDeleted = 8
            });

            resetSystem.Update();

            CombatStatsSingleton stats = entityManager.GetComponentData<CombatStatsSingleton>(statsEntity);
            Assert.That(stats.ActiveProjectiles, Is.EqualTo(333),
                "Active counts are level stats; mid-frame readers (the cleanup calm-down gate) need last frame's values to survive the reset.");
            Assert.That(stats.ActiveAoes, Is.EqualTo(44));
            Assert.That(stats.EntitiesSpawnedViaEcb, Is.Zero);
            Assert.That(stats.EntitiesSpawnedViaReuse, Is.Zero);
            Assert.That(stats.HitEventsCreated, Is.Zero);
            Assert.That(stats.VfxEventsCreated, Is.Zero);
            Assert.That(stats.EntitiesDespawned, Is.Zero);
            Assert.That(stats.EntitiesDeleted, Is.Zero);
        }
    }
}
