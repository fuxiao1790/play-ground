using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Core;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class CombatHitDamageScaleEditModeTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private CombatApplyFinalizeSingleSystem finalizeSystem;
        private double elapsedTime;

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

        [Test]
        public void HitEnergyOnlyHit_IncrementsHitCountButLeavesDamageAndCritUntouched()
        {
            Entity target = CreateHitEnergyTarget();
            CombatTickResult result = FinalizeHits(
                target,
                new CombatHitPayload
                {
                    DirectDamageEnabled = false,
                    HitEnergy = EnabledHitEnergy()
                });

            Assert.That(result.HitCount, Is.EqualTo(1));
            Assert.That(result.DamageTaken, Is.Zero);
            Assert.That(result.CritCount, Is.Zero);
        }

        [Test]
        public void MixedDirectAndHitEnergyEvents_AggregateIntoOneResultWithTotalHitCountAndDirectDamageOnlyTotals()
        {
            Entity target = CreateHitEnergyTarget();
            CombatTickResult result = FinalizeHits(
                target,
                new CombatHitPayload
                {
                    DamageAmount = 10f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                },
                new CombatHitPayload
                {
                    DirectDamageEnabled = false,
                    HitEnergy = EnabledHitEnergy()
                },
                new CombatHitPayload
                {
                    DamageAmount = 5f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                });

            Assert.That(result.HitCount, Is.EqualTo(3), "Total accepted-hit count includes the hit-energy-only event.");
            Assert.That(result.DamageTaken, Is.EqualTo(15f).Within(0.0001f), "Direct-damage-only aggregate excludes the hit-energy-only event.");
        }

        [Test]
        public void FinalizerUpdate_StampsTickDeltaSecondsFromWorldTime()
        {
            const float deltaTime = 0.0625f;
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            Entity target = CreateHitEnergyTarget();
            CombatTickResult result = FinalizeHits(
                target,
                new CombatHitPayload
                {
                    DamageAmount = 1f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                });

            Assert.That(result.TickDeltaSeconds, Is.EqualTo(deltaTime).Within(0.0001f));
        }

        // Full bridge-callback wiring is feasible in this EditMode fixture: CombatApplyBridge only
        // needs a target proxy carrying TargetCompanion plus a manual Update() call, both available
        // outside a PresentationSystemGroup. This directly observes CombatApplyBridge.ReplayCombat
        // forwarding HitCount/TickDeltaSeconds unchanged (no bridge-side transformation exists) and
        // that replay happens exactly once for the aggregated per-target result.
        [Test]
        public void BridgeCallback_ObservesFinalizerHitCountAndTickDeltaSecondsExactlyOnce()
        {
            const float deltaTime = 0.04f;
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            Entity source = entityManager.CreateEntity(typeof(CombatHitPayload));
            entityManager.SetComponentData(source, new CombatHitPayload
            {
                DamageAmount = 4f,
                CritMultiplier = 1f,
                DirectDamageEnabled = true
            });

            Entity target = entityManager.CreateEntity(typeof(Health), typeof(TargetCompanion));
            entityManager.SetComponentData(target, new Health { Current = 100f, Max = 100f });
            var listener = new CapturingCombatTarget();
            entityManager.SetComponentData(target, new TargetCompanion { Target = listener });

            using EntityQuery hitQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatHitDispatchSingleton>());
            CombatHitDispatchSingleton hitDispatch = hitQuery.GetSingleton<CombatHitDispatchSingleton>();
            hitDispatch.HitQueue.Enqueue(new CombatHitEvent { Source = source, Target = target });
            hitDispatch.HitQueue.Enqueue(new CombatHitEvent { Source = source, Target = target });

            finalizeSystem.Update();

            CombatApplyBridge bridge = testWorld.GetOrCreateSystemManaged<CombatApplyBridge>();
            bridge.Update();

            Assert.That(listener.ReceiveCombatTickCallCount, Is.EqualTo(1),
                "Presentation replay must occur exactly once for the aggregated target result.");
            Assert.That(listener.LastResult.HitCount, Is.EqualTo(2));
            Assert.That(listener.LastResult.TickDeltaSeconds, Is.EqualTo(deltaTime).Within(0.0001f));
        }

        private static HitEnergyPayload EnabledHitEnergy() => new()
        {
            AccumulatorId = 1,
            EnergyPerHit = 1f,
            EnergyRequired = 1f,
            RetentionSeconds = 10f,
            Spawn = new HitEnergySpawn
            {
                Kind = HitEnergySpawnKind.Projectile,
                TemplateKey = new Unity.Entities.Hash128(0xBEEFu, 0xCAFEu, 0u, 0u)
            }
        };

        private Entity CreateHitEnergyTarget()
        {
            Entity target = entityManager.CreateEntity(typeof(Health), typeof(TargetHitEnergy));
            entityManager.SetComponentData(target, new Health { Current = 100f, Max = 100f });
            return target;
        }

        // Sibling to FinalizeDirectHits: enqueues one hit per payload (each from its own source
        // entity) against a single shared target, for cases that don't fit FinalizeDirectHits'
        // one-payload-many-damage-scales signature (hit-energy-only and mixed direct+energy events).
        private CombatTickResult FinalizeHits(Entity target, params CombatHitPayload[] payloads)
        {
            using EntityQuery hitQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatHitDispatchSingleton>());
            CombatHitDispatchSingleton hitDispatch = hitQuery.GetSingleton<CombatHitDispatchSingleton>();
            for (int i = 0; i < payloads.Length; i++)
            {
                Entity source = entityManager.CreateEntity(typeof(CombatHitPayload));
                entityManager.SetComponentData(source, payloads[i]);
                hitDispatch.HitQueue.Enqueue(new CombatHitEvent
                {
                    Source = source,
                    Target = target
                });
            }

            finalizeSystem.Update();

            using EntityQuery resultQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatApplyResultSingleton>());
            CombatApplyResultSingleton resultDispatch = resultQuery.GetSingleton<CombatApplyResultSingleton>();
            resultDispatch.ProducerHandle.Complete();
            Assert.That(resultDispatch.Results.Length, Is.EqualTo(1));
            return resultDispatch.Results[0];
        }

        private sealed class CapturingCombatTarget : ICombatTarget
        {
            public int TargetId => 1;
            public Vector2 CombatTargetPosition => Vector2.zero;
            public float CombatTargetRadius => 0.25f;
            public Vector2 CombatTargetHalfExtents => Vector2.zero;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => 0;
            public bool IsCombatTargetActive => true;
            public int ReceiveCombatTickCallCount { get; private set; }
            public CombatTickResult LastResult { get; private set; }

            public void ReceiveHit(in CombatHitData hit)
            {
            }

            public void ReceiveCombatTick(in CombatTickResult result, IReadOnlyList<HitEnergyProgress> progress)
            {
                ReceiveCombatTickCallCount++;
                LastResult = result;
            }
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
