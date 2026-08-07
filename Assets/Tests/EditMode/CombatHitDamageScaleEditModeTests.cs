using NUnit.Framework;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Targets;
using Unity.Entities;

namespace PlayGround.Tests.EditMode
{
    public sealed class CombatHitDamageScaleEditModeTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private CombatApplyFinalizeSingleSystem finalizeSystem;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CombatHitDamageScaleEditModeTest");
            entityManager = testWorld.EntityManager;
            finalizeSystem = testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
        }

        [Test]
        public void DamageScale_OneAndHalf_AggregatesToOnePointFiveTimesPayload()
        {
            CombatTickResult result = FinalizeDirectHits(
                new CombatHitPayload
                {
                    DamageAmount = 10f,
                    DirectDamageEnabled = true
                },
                1f,
                0.5f);

            Assert.That(result.DamageTaken, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(result.HitCount, Is.EqualTo(2));
            Assert.That(result.CritCount, Is.Zero);
        }

        [Test]
        public void DamageScale_Unset_UsesFullPayloadDamage()
        {
            CombatTickResult result = FinalizeDirectHits(
                new CombatHitPayload
                {
                    DamageAmount = 10f,
                    DirectDamageEnabled = true
                },
                null);

            Assert.That(result.DamageTaken, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void DamageScale_IsAppliedBeforeCritMultiplier()
        {
            CombatTickResult result = FinalizeDirectHits(
                new CombatHitPayload
                {
                    DamageAmount = 10f,
                    CritChance = 1f,
                    CritMultiplier = 2f,
                    DirectDamageEnabled = true
                },
                0.5f);

            Assert.That(result.DamageTaken, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(result.CritCount, Is.EqualTo(1));
        }

        private CombatTickResult FinalizeDirectHits(CombatHitPayload payload, params float?[] damageScales)
        {
            Entity source = entityManager.CreateEntity(typeof(CombatHitPayload));
            entityManager.SetComponentData(source, payload);
            Entity target = entityManager.CreateEntity(typeof(Health));
            entityManager.SetComponentData(target, new Health { Current = 100f, Max = 100f });

            using EntityQuery hitQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatHitDispatchSingleton>());
            CombatHitDispatchSingleton hitDispatch = hitQuery.GetSingleton<CombatHitDispatchSingleton>();
            for (int i = 0; i < damageScales.Length; i++)
            {
                var hit = new CombatHitEvent
                {
                    Source = source,
                    Target = target
                };
                if (damageScales[i].HasValue)
                {
                    hit.DamageScale = damageScales[i].Value;
                }

                hitDispatch.HitQueue.Enqueue(hit);
            }

            finalizeSystem.Update();

            using EntityQuery resultQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatApplyResultSingleton>());
            CombatApplyResultSingleton resultDispatch = resultQuery.GetSingleton<CombatApplyResultSingleton>();
            resultDispatch.ProducerHandle.Complete();
            Assert.That(resultDispatch.Results.Length, Is.EqualTo(1));
            return resultDispatch.Results[0];
        }
    }
}
