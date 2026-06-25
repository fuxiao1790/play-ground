using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.Tests.PlayMode
{
    public sealed class AoeSimulationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private PresentationSystemGroup presentationGroup;
        private AoeSpawnExpansionSystem aoeExpansion;
        private ProjectileSpawnExpansionSystem projectileExpansion;
        private CombatApplyFinalizeSystem hitApply;
        private StatusProcessSystem statusProcess;
        private Entity scopeEntity;
        private double elapsedTime;
        private int nextAoeId;
        private int nextTargetId = 5000;
        private readonly Dictionary<int, TestCombatTarget> targetsById = new();
        private int lastHitCount;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("AoeSimulationTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            aoeExpansion = testWorld.GetOrCreateSystemManaged<AoeSpawnExpansionSystem>();
            projectileExpansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            hitApply = testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSystem>();
            statusProcess = testWorld.GetOrCreateSystemManaged<StatusProcessSystem>();
            simGroup.AddSystemToUpdateList(aoeExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<AoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatLifetimeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoePulseVfxSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ImpactAoeCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<LingeringAoeCollisionSystem>());
            simGroup.AddSystemToUpdateList(hitApply);
            simGroup.AddSystemToUpdateList(statusProcess);
            simGroup.SortSystems();

            presentationGroup = testWorld.GetOrCreateSystemManaged<PresentationSystemGroup>();
            presentationGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyBridge>());
            presentationGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<AoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld.IsCreated)
                testWorld.Dispose();
        }

        [Test]
        public void PulseHitsOverlappingTargetOnce()
        {
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void OverlappingTargetRegisteredInMultipleCellsHitsOnce()
        {
            AddTarget(new float2(64f, 0f), 1f, 1);
            SpawnCircle(new float2(64f, 0f), 2f, 2f);

            Tick(0.01f);

            Assert.That(ReadHitCount(), Is.EqualTo(1));
        }

        [Test]
        public void PulseHitsAtMostMaxAoeTargetsPerTick()
        {
            const int ExtraTargets = 5;
            int targetCount = CollisionConstants.MaxAoeTargetsPerTick + ExtraTargets;
            for (int i = 0; i < targetCount; i++)
            {
                AddTarget(float2.zero, 0.25f, 1);
            }

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);

            Assert.That(ReadHitCount(), Is.EqualTo(CollisionConstants.MaxAoeTargetsPerTick));
        }

        [Test]
        public void PulseHitsEveryOverlappingTargetBelowCap()
        {
            const int TargetCount = 7;
            for (int i = 0; i < TargetCount; i++)
            {
                AddTarget(float2.zero, 0.25f, 1);
            }

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);

            Assert.That(ReadHitCount(), Is.EqualTo(TargetCount));
        }

        [Test]
        public void LingeringTargetIsNotRehitUntilGateCooldownExpires()
        {
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 2f, lifetime: 10f, tickInterval: 0.05f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));

            Tick(0.041f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));
        }

        [Test]
        public void LingeringHitsImmediatelyThenRepeatsAfterCooldown()
        {
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 2f, lifetime: 10f, tickInterval: 0.02f);

            Tick(0.01f);
            int hits = ReadHitCount();
            Tick(0.01f);
            hits += ReadHitCount();
            Tick(0.02f);
            hits += ReadHitCount();

            Assert.That(hits, Is.EqualTo(2));
        }

        [Test]
        public void LingeringReentryRespectsCooldown()
        {
            int targetId = ++nextTargetId;
            AddTargetById(float2.zero, 0.25f, 1, targetId);
            SpawnCircle(float2.zero, 1f, 2f, lifetime: 10f, tickInterval: 100f);

            Tick(0.01f);
            int hits = ReadHitCount();

            ReplaceTarget(targetId, new float2(5f, 0f), 0.25f, 1);
            Tick(0.01f);
            hits += ReadHitCount();

            ReplaceTarget(targetId, float2.zero, 0.25f, 1);
            Tick(0.01f);
            hits += ReadHitCount();

            Assert.That(hits, Is.EqualTo(1));
        }

        [Test]
        public void LingeringExpiresAndDeactivates()
        {
            SpawnCircle(float2.zero, 1f, 2f, lifetime: 0.001f, tickInterval: 1f);

            Tick(0.01f);
            Tick(0.01f);

            Assert.That(ActiveAoeCount(), Is.EqualTo(0));
            Assert.That(TotalAoeCount(), Is.EqualTo(1));
        }

        [Test]
        public void PulseDoesNotHitTargetOutsideRadius()
        {
            AddTarget(new float2(3f, 0f), 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void PulseEntityIsReusedOnRespawn()
        {
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 1f);
            Tick(0.01f);
            Entity first = FirstAoeEntity();

            SpawnCircle(float2.zero, 1f, 1f);
            Tick(0.01f);
            Entity reused = FirstAoeEntity();

            Assert.That(reused, Is.EqualTo(first));
            Assert.That(TotalAoeCount(), Is.EqualTo(1));
        }

        [Test]
        public void ImpactArchetypeOmitsLingeringOnlyComponentsAndHasLargerChunkCapacity()
        {
            SpawnCircle(float2.zero, 1f, 1f);
            Tick(0.01f);
            Entity impact = FirstImpactAoeEntity();

            SpawnCircle(float2.zero, 1f, 1f, lifetime: 10f, tickInterval: 0.05f);
            Tick(0.01f);
            Entity lingering = FirstLingeringAoeEntity();

            Assert.That(entityManager.HasComponent<CombatLifetimeComponent>(impact), Is.False);
            Assert.That(entityManager.HasComponent<AoeContactGateElement>(impact), Is.False);
            Assert.That(entityManager.HasComponent<AoePulseVfxComponent>(impact), Is.False);
            Assert.That(entityManager.HasComponent<CombatLifetimeComponent>(lingering), Is.True);
            Assert.That(entityManager.HasComponent<AoeContactGateElement>(lingering), Is.True);
            Assert.That(entityManager.HasComponent<AoePulseVfxComponent>(lingering), Is.True);

            int impactCapacity = entityManager.GetChunk(impact).Capacity;
            int lingeringCapacity = entityManager.GetChunk(lingering).Capacity;
            Assert.That(impactCapacity, Is.GreaterThan(lingeringCapacity));
        }

        [Test]
        public void ImpactWithoutCollisionPayloadDoesNotLeakActiveSlot()
        {
            SpawnCircle(float2.zero, 1f, 0f);

            Tick(0.01f);

            Entity impact = FirstImpactAoeEntity();
            Assert.That(entityManager.IsComponentEnabled<Active>(impact), Is.False);
            Assert.That(entityManager.IsComponentEnabled<AoeCollisionActiveTag>(impact), Is.False);
            Assert.That(entityManager.IsComponentEnabled<CombatRenderActiveTag>(impact), Is.False);
            Assert.That(ActiveAoeCount(), Is.EqualTo(0));
            Assert.That(TotalAoeCount(), Is.EqualTo(1));
        }

        [Test]
        public void ImpactAndLingeringSlotsReuseOnlyWithinMatchingPools()
        {
            SpawnCircle(float2.zero, 1f, 1f);
            Tick(0.01f);
            Tick(0.01f);
            Entity firstImpact = FirstImpactAoeEntity();

            SpawnCircle(float2.zero, 1f, 1f, lifetime: 0.001f, tickInterval: 0.05f);
            Tick(0.01f);
            Tick(0.01f);
            Entity firstLingering = FirstLingeringAoeEntity();

            Assert.That(firstLingering, Is.Not.EqualTo(firstImpact));
            Assert.That(TotalAoeCount(), Is.EqualTo(2));

            SpawnCircle(float2.zero, 1f, 1f);
            Tick(0.01f);
            Tick(0.01f);
            Entity reusedImpact = FirstImpactAoeEntity();

            SpawnCircle(float2.zero, 1f, 1f, lifetime: 0.001f, tickInterval: 0.05f);
            Tick(0.01f);
            Tick(0.01f);
            Entity reusedLingering = FirstLingeringAoeEntity();

            Assert.That(reusedImpact, Is.EqualTo(firstImpact));
            Assert.That(reusedLingering, Is.EqualTo(firstLingering));
            Assert.That(TotalAoeCount(), Is.EqualTo(2));
        }

        [Test]
        public void TargetProxyLifecycle_CreatePushDeleteControlsCollisionVisibility()
        {
            int targetId = ++nextTargetId;
            AddTargetById(new float2(5f, 0f), 0.25f, 1, targetId);
            TestCombatTarget target = targetsById[targetId];

            Assert.That(entityManager.Exists(target.Proxy), Is.True);
            Assert.That(entityManager.HasComponent<TargetCompanion>(target.Proxy), Is.True);

            target.Position = float2.zero;
            Assert.That(CombatTargetProxy.Push(entityManager, target.Proxy, target), Is.True);
            TargetPosition pushed = entityManager.GetComponentData<TargetPosition>(target.Proxy);
            Assert.That(pushed.Value.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(pushed.Value.y, Is.EqualTo(0f).Within(0.001f));

            SpawnCircle(float2.zero, 1f, 2f);
            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));

            CombatTargetProxy.Delete(entityManager, target.Proxy);
            target.Proxy = Entity.Null;
            SpawnCircle(float2.zero, 1f, 2f);
            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void EntityKeyedDamage_FinalizedEventUsesHitProxyAndDispatchResolvesCompanion()
        {
            AddTarget(float2.zero, 0.25f, 1);
            Entity proxy = FirstTargetProxy();
            SpawnCircle(float2.zero, 1f, 2f);

            TickSimulationOnly(0.01f);

            CombatTickResult[] finalized = ReadFinalizedCombatResults();
            Assert.That(finalized, Has.Length.EqualTo(1));
            Assert.That(finalized[0].TargetProxy, Is.EqualTo(proxy));
            Assert.That(finalized[0].HitCount, Is.EqualTo(1));
            Assert.That(finalized[0].DamageTaken, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<TargetHealth>(proxy).Current, Is.EqualTo(finalized[0].Health).Within(0.0001f));

            presentationGroup.Update();
            Assert.That(TotalHitCount(), Is.EqualTo(1));
        }

        [Test]
        public void CombatApplyAggregatesEveryHitForOneTarget()
        {
            const float SeedHealth = 10f;
            const float TotalDamage = 6f;
            AddTarget(float2.zero, 0.25f, 1, SeedHealth);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueDirectHit(target.Proxy, 1f);
            QueueDirectHit(target.Proxy, 2f);
            QueueDirectHit(target.Proxy, 3f);

            TickStatusPipelineOnly(0f);
            CombatTickResult[] finalized = ReadFinalizedCombatResults();

            presentationGroup.Update();

            Assert.That(finalized, Has.Length.EqualTo(1));
            Assert.That(finalized[0].HitCount, Is.EqualTo(3));
            Assert.That(finalized[0].DamageTaken, Is.EqualTo(TotalDamage).Within(0.0001f));
            Assert.That(finalized[0].Health, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<TargetHealth>(target.Proxy).Current, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
            Assert.That(target.Hits, Has.Count.EqualTo(1));
            Assert.That(SummedHitDamage(target.Hits), Is.EqualTo(TotalDamage).Within(0.0001f));
        }

        [Test]
        public void EcsCritRollsAreDeterministicForSameSeedInputs()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueDirectHit(target.Proxy, 10f, critChance: 0.5f, critMultiplier: 2f);
            TickStatusPipelineOnly(0f);
            presentationGroup.Update();

            DamageSnapshot first = target.Hits[0].Damage;

            QueueDirectHit(target.Proxy, 10f, critChance: 0.5f, critMultiplier: 2f);
            TickStatusPipelineOnly(0f);
            presentationGroup.Update();

            DamageSnapshot second = target.Hits[1].Damage;
            Assert.That(second.IsCrit, Is.EqualTo(first.IsCrit));
            Assert.That(second.Amount, Is.EqualTo(first.Amount).Within(0.0001f));
        }

        [Test]
        public void EcsPushesLethalOverkillAndTargetOwnsHealthClamp()
        {
            const float SeedHealth = 5f;
            const float TotalDamage = 16f;
            AddTarget(float2.zero, 0.25f, 1, SeedHealth);
            TestCombatTarget target = targetsById[nextTargetId];
            target.ClampHealthOnHit = true;

            QueueDirectHit(target.Proxy, 7f);
            QueueDirectHit(target.Proxy, 9f);

            TickStatusPipelineOnly(0f);
            CombatTickResult[] finalized = ReadFinalizedCombatResults();

            presentationGroup.Update();

            Assert.That(finalized, Has.Length.EqualTo(1));
            Assert.That(finalized[0].HitCount, Is.EqualTo(2));
            Assert.That(finalized[0].DamageTaken, Is.EqualTo(TotalDamage).Within(0.0001f));
            Assert.That(finalized[0].Health, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<TargetHealth>(target.Proxy).Current, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
            Assert.That(target.Hits, Has.Count.EqualTo(1));
            Assert.That(target.LastPreClampHealth, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
            Assert.That(target.Health, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(target.IsCombatTargetActive, Is.False);
            Assert.That(entityManager.Exists(target.Proxy), Is.True);
        }

        [Test]
        public void ProxyDeletionSafety_TargetCanDieDuringDispatchBeforeProxyDelete()
        {
            int targetId = ++nextTargetId;
            AddTargetById(float2.zero, 0.25f, 1, targetId);
            TestCombatTarget target = targetsById[targetId];
            target.DeactivateOnHit = true;

            SpawnCircle(float2.zero, 1f, 2f);
            Tick(0.01f);

            Assert.That(target.IsCombatTargetActive, Is.False);
            Assert.That(entityManager.Exists(target.Proxy), Is.True,
                "Target-side death should not delete the proxy during dispatch.");

            CombatTargetProxy.Delete(entityManager, target.Proxy);
            target.Proxy = Entity.Null;
            SpawnCircle(float2.zero, 1f, 2f);
            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void CommonCombatEntityWithoutAoeTagIsIgnored()
        {
            Entity alien = entityManager.CreateEntity(
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active));
            entityManager.SetComponentData(alien, new CombatKinematicsComponent
            {
                Position = new float2(1f, 2f)
            });

            Tick(0.01f);

            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(alien);
            Assert.That(kinematics.Position.x, Is.EqualTo(1f));
            Assert.That(kinematics.Position.y, Is.EqualTo(2f));
            entityManager.DestroyEntity(alien);
        }

        [Test]
        public void ContactGateSystemSkipsCollisionInactiveAoeSlots()
        {
            Entity disabledAoe = entityManager.CreateEntity(
                typeof(AoeTag),
                typeof(AoeCollisionActiveTag),
                typeof(AoeContactGateElement));
            DynamicBuffer<AoeContactGateElement> gates = entityManager.GetBuffer<AoeContactGateElement>(disabledAoe);
            gates.Add(new AoeContactGateElement
            {
                TargetId = 1,
                CooldownRemaining = 1f
            });
            entityManager.SetComponentEnabled<AoeCollisionActiveTag>(disabledAoe, false);

            TickSimulationOnly(0.25f);

            gates = entityManager.GetBuffer<AoeContactGateElement>(disabledAoe);
            Assert.That(gates.Length, Is.EqualTo(1));
            Assert.That(gates[0].CooldownRemaining, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void AoeOnHitSpawnQueuesNextAoeAtHitTarget()
        {
            AddTarget(float2.zero, 0.25f, 1);
            var linkedAoe = new AoeOnHitSpawnSnapshot(
                typeId: 2,
                targetMask: ~0,
                damageAmount: 3f,
                directDamageEnabled: true,
                lifetimeSeconds: 0f,
                tickIntervalSeconds: 0f,
                geometry: new AoeSpawnGeometry(
                    1f,
                    CombatShapeType.Circle,
                    1f,
                    Vector2.zero,
                    0f,
                    Vector2.zero,
                    0f),
                critChance: 0f,
                critMultiplier: 1.5f);
            SpawnCircle(float2.zero, 1f, 0f, aoeSpawn: linkedAoe);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));
            Assert.That(TotalAoeCount(), Is.EqualTo(2));
        }

        [Test]
        public void StatusProcessFizzleRemovesPartialStackWithoutDetonation()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 101, threshold: 2, lifetime: 0.05f, damage: 3f, area: 1f, detonationTypeId: 7, next: default));

            TickStatusPipelineOnly(0f);
            Assert.That(ReadStackEntry(target.Proxy, 101).Count, Is.EqualTo(1));

            TickStatusPipelineOnly(0.06f);

            Assert.That(TryReadStackEntry(target.Proxy, 101, out _), Is.False);
            Assert.That(AoeEventQueue().Count, Is.EqualTo(0));
        }

        [Test]
        public void StatusProcessProjectileDetonationFizzleQueuesNoNova()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(
                target.Proxy,
                ProjectileStackEffect(
                debuffKey: 301,
                threshold: 2,
                lifetime: 0.05f,
                damage: 7f,
                projectileCount: 3));

            TickStatusPipelineOnly(0f);
            Assert.That(ReadStackEntry(target.Proxy, 301).Count, Is.EqualTo(1));

            TickStatusPipelineOnly(0.06f);

            Assert.That(TryReadStackEntry(target.Proxy, 301, out _), Is.False);
            Assert.That(ProjectileEventQueue().Count, Is.EqualTo(0));
        }

        [Test]
        public void StatusProcessSumsFireTimeContributionsUntilThreshold()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 102, threshold: 3, lifetime: 10f, damage: 2f, area: 1f, detonationTypeId: 7, next: default));
            QueueStackHit(target.Proxy, StackEffect(debuffKey: 102, threshold: 3, lifetime: 10f, damage: 5f, area: 2f, detonationTypeId: 7, next: default));
            TickStatusPipelineOnly(0f);

            TargetStackEntry partial = ReadStackEntry(target.Proxy, 102);
            Assert.That(partial.Count, Is.EqualTo(2));
            Assert.That(partial.SummedDamage, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(partial.SummedArea, Is.EqualTo(3f).Within(0.0001f));

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 102, threshold: 3, lifetime: 10f, damage: 11f, area: 3f, detonationTypeId: 7, next: default));
            TickStatusPipelineOnly(0f);

            AoeSpawnEvent detonation = DequeueSingleAoeEvent();
            Assert.That(TryReadStackEntry(target.Proxy, 102, out _), Is.False);
            Assert.That(detonation.HitPayload.DamageAmount, Is.EqualTo(18f).Within(0.0001f));
            // Damage still sums across stacks, but area is capped at the configured geometry
            // (areaScale clamped to 1): SummedArea 6 over a geometry AreaSize of 1 -> 1.
            Assert.That(detonation.AreaSize, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void StatusProcessAoeDetonationPreservesGeometryAndDamage()
        {
            AddTarget(new float2(3f, -2f), 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];
            AoeSpawnGeometry geometry = new(
                2f,
                CombatShapeType.Rectangle,
                2f,
                new Vector2(1f, 0.5f),
                0.25f,
                new Vector2(0.5f, 0.25f),
                15f);

            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: 104,
                threshold: 2,
                lifetime: 10f,
                damage: 4f,
                area: 2f,
                detonationTypeId: 77,
                next: default,
                geometry: geometry,
                lifetimeSeconds: 0.5f,
                tickIntervalSeconds: 0.125f));
            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: 104,
                threshold: 2,
                lifetime: 10f,
                damage: 6f,
                area: 4f,
                detonationTypeId: 77,
                next: default,
                geometry: geometry,
                lifetimeSeconds: 0.5f,
                tickIntervalSeconds: 0.125f));

            TickStatusPipelineOnly(0f);

            AoeSpawnEvent detonation = DequeueSingleAoeEvent();
            Assert.That(detonation.TypeId, Is.EqualTo(77));
            Assert.That(detonation.Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(detonation.Position.y, Is.EqualTo(-2f).Within(0.0001f));
            Assert.That(detonation.HitPayload.DamageAmount, Is.EqualTo(10f).Within(0.0001f));
            // Damage sums (4 + 6 = 10), but area is capped at the configured geometry: SummedArea
            // 6 over geometry AreaSize 2 clamps areaScale to 1, so radius/half-extents stay at the
            // authored values rather than scaling up with stack count.
            Assert.That(detonation.AreaSize, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(detonation.Radius, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(detonation.HalfExtents.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(detonation.HalfExtents.y, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(detonation.Lifetime, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(detonation.RepeatHitCooldownSeconds, Is.EqualTo(0.125f).Within(0.0001f));
        }

        [Test]
        public void StatusPushFiresOnlyWhenStackChanges()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: 105,
                threshold: 3,
                lifetime: 5f,
                damage: 2f,
                area: 1f,
                detonationTypeId: 7,
                next: default));

            TickStatusPipelineOnly(0f);
            presentationGroup.Update();

            Assert.That(target.StatusPushCount, Is.EqualTo(1));
            Assert.That(target.StatusSnapshots, Has.Count.EqualTo(1));
            Assert.That(target.StatusSnapshots[0].DebuffKey, Is.EqualTo(105));
            Assert.That(target.StatusSnapshots[0].Count, Is.EqualTo(1));
            Assert.That(target.StatusSnapshots[0].LifetimeRemaining, Is.EqualTo(5f).Within(0.0001f));

            TickStatusPipelineOnly(0.25f);
            presentationGroup.Update();

            Assert.That(target.StatusPushCount, Is.EqualTo(1));
        }

        [Test]
        public void AoeApplicatorProjectileDetonationQueuesNovaWithSummedContribution()
        {
            AddTarget(float2.zero, 0.25f, 1);
            const float TotalDamage = 12f;
            const int ProjectileCount = 5;

            SpawnCircle(
                float2.zero,
                1f,
                0f,
                stackEffect: ProjectileStackEffect(
                    debuffKey: 302,
                    threshold: 1,
                    lifetime: 10f,
                    damage: TotalDamage,
                    projectileCount: ProjectileCount));

            TickSimulationOnly(0.01f);

            ProjectileSpawnEvent detonation = DequeueSingleProjectileEvent();
            Assert.That(detonation.Count, Is.EqualTo(ProjectileCount));
            Assert.That(detonation.TypeId, Is.EqualTo(70));
            Assert.That(detonation.HitPayload.DamageAmount * detonation.Count, Is.EqualTo(TotalDamage).Within(0.0001f));
        }

        [Test]
        public void StatusProcessUnhandledDetonationKindQueuesNoSpawn()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, new StackEffectSnapshot
            {
                DebuffKey = 303,
                Threshold = 1,
                Lifetime = 10f,
                Contribution = new StackContribution
                {
                    Damage = 5f,
                    ProjectileCount = 2,
                    AreaSize = 1f
                },
                Detonation = new DetonationSnapshot
                {
                    Kind = (StackDetonationKind)999,
                    Faction = CombatFaction.Player,
                    TargetMask = ~0,
                    TypeId = 71
                }
            });

            TickStatusPipelineOnly(0f);

            Assert.That(AoeEventQueue().Count, Is.EqualTo(0));
            Assert.That(ProjectileEventQueue().Count, Is.EqualTo(0));
        }

        [Test]
        public void ThreeStackingExplosionsComposeThroughAoeHitSpawnLinks()
        {
            AddTarget(float2.zero, 0.25f, 1);
            AoeSpawnGeometry geometry = UnitAoeGeometry();
            AoeOnHitSpawnSnapshot thirdApplicator = StackingApplicatorSnapshot(
                typeId: 3,
                debuffKey: 203,
                detonationTypeId: 30,
                detonationDamage: 9f,
                next: default);
            AoeOnHitSpawnSnapshot secondApplicator = StackingApplicatorSnapshot(
                typeId: 2,
                debuffKey: 202,
                detonationTypeId: 20,
                detonationDamage: 7f,
                next: thirdApplicator);
            StackEffectSnapshot firstStack = StackEffect(
                debuffKey: 201,
                threshold: 1,
                lifetime: 10f,
                damage: 5f,
                area: geometry.AreaSize,
                detonationTypeId: 10,
                next: secondApplicator);

            SpawnCircle(float2.zero, 1f, 0f, stackEffect: firstStack);
            for (int i = 0; i < 8; i++)
                Tick(0.01f);

            Assert.That(AoeCountByType(10), Is.EqualTo(1));
            Assert.That(AoeCountByType(20), Is.EqualTo(1));
            Assert.That(AoeCountByType(30), Is.EqualTo(1));
            Assert.That(TotalHitCount(), Is.EqualTo(3));
        }

        private void Tick(float dt)
        {
            int hitsBefore = TotalHitCount();
            TickSimulationOnly(dt);
            presentationGroup.Update();
            lastHitCount = TotalHitCount() - hitsBefore;
        }

        private void TickStatusPipelineOnly(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            hitApply.Update();
            statusProcess.Update();
        }

        private void TickSimulationOnly(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private void SpawnCircle(
            float2 position,
            float radius,
            float damage,
            float lifetime = 0f,
            float tickInterval = 0f,
            AoeOnHitSpawnSnapshot aoeSpawn = default,
            StackEffectSnapshot stackEffect = default)
        {
            entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity).Add(new AoeSpawnEvent
            {
                Faction = CombatFaction.Player,
                AoeId = ++nextAoeId,
                TypeId = 1,
                Lifetime = lifetime,
                RepeatHitCooldownSeconds = tickInterval,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = damage,
                    DirectDamageEnabled = damage > 0f,
                    StackEffect = stackEffect
                },
                Radius = radius,
                Position = position,
                HalfExtents = float2.zero,
                ShapeType = CombatShapeType.Circle,
                AoeSpawn = aoeSpawn
            });
        }

        private void AddTarget(float2 position, float radius, int targetMask, float health = 100f)
        {
            AddTargetById(position, radius, targetMask, ++nextTargetId, health);
        }

        private void AddTargetById(float2 position, float radius, int targetMask, int targetId, float health = 100f)
        {
            if (!targetsById.TryGetValue(targetId, out TestCombatTarget target))
            {
                target = new TestCombatTarget(targetId);
                targetsById.Add(targetId, target);
            }

            target.Position = position;
            target.Radius = radius;
            target.Mask = targetMask;
            target.Health = health;
            target.Proxy = CombatTargetProxy.Create(entityManager, target, CombatFaction.Player);
        }

        private void ReplaceTarget(int targetId, float2 position, float radius, int targetMask)
        {
            AddTargetById(position, radius, targetMask, targetId);
        }

        private int ReadHitCount() => lastHitCount;

        private int TotalHitCount()
        {
            int count = 0;
            foreach (TestCombatTarget target in targetsById.Values)
            {
                count += target.HitCount;
            }

            return count;
        }

        private static float SummedHitDamage(IReadOnlyList<CombatHitData> hits)
        {
            float total = 0f;
            for (int i = 0; i < hits.Count; i++)
            {
                total += hits[i].Damage.Amount;
            }

            return total;
        }

        private void WriteTargetProxy(TestCombatTarget target)
        {
            float2 min = target.Position - target.Radius;
            float2 max = target.Position + target.Radius;
            entityManager.SetComponentData(target.Proxy, new TargetPosition { Value = target.Position });
            entityManager.SetComponentData(target.Proxy, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = target.Radius,
                HalfExtents = float2.zero,
                BoundsMin = min,
                BoundsMax = max,
                Mask = target.Mask
            });
        }

        private int ActiveAoeCount()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<Active>());
            return q.CalculateEntityCount();
        }

        private int TotalAoeCount()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeTag>());
            return q.CalculateEntityCount();
        }

        private Entity FirstAoeEntity()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeTag>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No AOE entities found.");
            return entities[0];
        }

        private Entity FirstImpactAoeEntity()
        {
            using EntityQuery q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithNone<CombatLifetimeComponent>()
                .Build(entityManager);
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No impact AOE entities found.");
            return entities[0];
        }

        private Entity FirstLingeringAoeEntity()
        {
            using EntityQuery q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<CombatLifetimeComponent>()
                .Build(entityManager);
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No lingering AOE entities found.");
            return entities[0];
        }

        private Entity FirstTargetProxy()
        {
            foreach (TestCombatTarget target in targetsById.Values)
            {
                return target.Proxy;
            }

            Assert.Fail("No target proxy found.");
            return Entity.Null;
        }

        private CombatTickResult[] ReadFinalizedCombatResults()
        {
            CombatApplyBridge bridge = testWorld.GetExistingSystemManaged<CombatApplyBridge>();
            const global::System.Reflection.BindingFlags Flags =
                global::System.Reflection.BindingFlags.Instance |
                global::System.Reflection.BindingFlags.NonPublic;
            var resultsField = typeof(CombatApplyBridge).GetField("finalizedResults", Flags);
            Assert.That(resultsField, Is.Not.Null);
            var results = (NativeArray<CombatTickResult>)resultsField.GetValue(bridge);
            if (!results.IsCreated)
            {
                return global::System.Array.Empty<CombatTickResult>();
            }

            var copy = new CombatTickResult[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                copy[i] = results[i];
            }

            return copy;
        }

        private void QueueStackHit(Entity target, StackEffectSnapshot stackEffect)
        {
            NativeQueue<CombatHitEvent> hitQueue = HitQueue();
            hitQueue.Enqueue(new CombatHitEvent
            {
                TargetProxy = target,
                Kind = CombatHitKind.Aoe,
                DirectDamageEnabled = false,
                StackEffect = stackEffect
            });
        }

        private void QueueDirectHit(
            Entity target,
            float damage,
            float critChance = 0f,
            float critMultiplier = 1f)
        {
            NativeQueue<CombatHitEvent> hitQueue = HitQueue();
            hitQueue.Enqueue(new CombatHitEvent
            {
                TargetProxy = target,
                Kind = CombatHitKind.Aoe,
                DamageAmount = damage,
                CritChance = critChance,
                CritMultiplier = critMultiplier,
                DirectDamageEnabled = true,
                HitPosition = float2.zero
            });
        }

        private StackEffectSnapshot StackEffect(
            int debuffKey,
            int threshold,
            float lifetime,
            float damage,
            float area,
            int detonationTypeId,
            AoeOnHitSpawnSnapshot next,
            AoeSpawnGeometry geometry = default,
            float lifetimeSeconds = 0f,
            float tickIntervalSeconds = 0f)
        {
            if (geometry.AreaSize <= 0f)
            {
                geometry = UnitAoeGeometry();
            }

            return new StackEffectSnapshot
            {
                DebuffKey = debuffKey,
                Threshold = threshold,
                Lifetime = lifetime,
                Contribution = new StackContribution
                {
                    Damage = damage,
                    AreaSize = area
                },
                Detonation = new DetonationSnapshot
                {
                    Kind = StackDetonationKind.Aoe,
                    Faction = CombatFaction.Player,
                    TargetMask = ~0,
                    TypeId = detonationTypeId,
                    LifetimeSeconds = lifetimeSeconds,
                    TickIntervalSeconds = tickIntervalSeconds,
                    AoeGeometry = geometry,
                    CritMultiplier = 1.5f,
                    AoeOnHitSpawn = next
                }
            };
        }

        private StackEffectSnapshot ProjectileStackEffect(
            int debuffKey,
            int threshold,
            float lifetime,
            float damage,
            int projectileCount,
            int projectileTypeId = 70)
        {
            return new StackEffectSnapshot
            {
                DebuffKey = debuffKey,
                Threshold = threshold,
                Lifetime = lifetime,
                Contribution = new StackContribution
                {
                    Damage = damage,
                    ProjectileCount = projectileCount,
                    AreaSize = 0f
                },
                Detonation = ProjectileDetonationSnapshot(projectileTypeId)
            };
        }

        private static DetonationSnapshot ProjectileDetonationSnapshot(int projectileTypeId)
        {
            return new DetonationSnapshot
            {
                Kind = StackDetonationKind.Projectile,
                Faction = CombatFaction.Player,
                TargetMask = ~0,
                TypeId = projectileTypeId,
                ProjectileBurst = new AoeProjectileBurstSnapshot(
                    projectileTypeId,
                    ~0,
                    1,
                    45f,
                    6f,
                    4f,
                    0.25f,
                    Vector2.zero,
                    0f,
                    CombatShapeType.Circle,
                    new DamageSnapshot(1f),
                    true,
                    pierceCount: 1,
                    repeatHitCooldownSeconds: 0.1f)
            };
        }

        private AoeOnHitSpawnSnapshot StackingApplicatorSnapshot(
            int typeId,
            int debuffKey,
            int detonationTypeId,
            float detonationDamage,
            AoeOnHitSpawnSnapshot next)
        {
            AoeSpawnGeometry geometry = UnitAoeGeometry();
            return new AoeOnHitSpawnSnapshot(
                typeId,
                ~0,
                0f,
                false,
                0f,
                0f,
                geometry,
                0f,
                1.5f,
                debuffKey,
                1,
                10f,
                new StackContribution
                {
                    Damage = detonationDamage,
                    AreaSize = geometry.AreaSize
                },
                StackDetonationKind.Aoe,
                detonationTypeId,
                0f,
                0f,
                geometry,
                0f,
                1.5f,
                TailSnapshot(next));
        }

        private static AoeOnHitSpawnTailSnapshot TailSnapshot(AoeOnHitSpawnSnapshot snapshot)
        {
            return snapshot.Enabled
                ? new AoeOnHitSpawnTailSnapshot(
                    snapshot.TypeId,
                    snapshot.TargetMask,
                    snapshot.DamageAmount,
                    snapshot.DirectDamageEnabled,
                    snapshot.LifetimeSeconds,
                    snapshot.TickIntervalSeconds,
                    snapshot.Geometry,
                    snapshot.CritChance,
                    snapshot.CritMultiplier,
                    snapshot.StackDebuffKey,
                    snapshot.StackThreshold,
                    snapshot.StackLifetime,
                    snapshot.StackContribution,
                    snapshot.StackDetonationKind,
                    snapshot.StackDetonationTypeId,
                    snapshot.StackDetonationLifetimeSeconds,
                    snapshot.StackDetonationTickIntervalSeconds,
                    snapshot.StackDetonationAoeGeometry,
                    snapshot.StackDetonationCritChance,
                    snapshot.StackDetonationCritMultiplier)
                : default;
        }

        private static AoeSpawnGeometry UnitAoeGeometry()
        {
            return new AoeSpawnGeometry(
                1f,
                CombatShapeType.Circle,
                1f,
                Vector2.zero,
                0f,
                Vector2.zero,
                0f);
        }

        private TargetStackEntry ReadStackEntry(Entity target, int debuffKey)
        {
            Assert.That(TryReadStackEntry(target, debuffKey, out TargetStackEntry entry), Is.True);
            return entry;
        }

        private bool TryReadStackEntry(Entity target, int debuffKey, out TargetStackEntry entry)
        {
            DynamicBuffer<TargetStackEntry> entries = entityManager.GetBuffer<TargetStackEntry>(target);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].DebuffKey == debuffKey)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private AoeSpawnEvent DequeueSingleAoeEvent()
        {
            NativeQueue<AoeSpawnEvent> queue = AoeEventQueue();
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out AoeSpawnEvent evt), Is.True);
            return evt;
        }

        private ProjectileSpawnEvent DequeueSingleProjectileEvent()
        {
            NativeQueue<ProjectileSpawnEvent> queue = ProjectileEventQueue();
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out ProjectileSpawnEvent evt), Is.True);
            return evt;
        }

        private int AoeCountByType(int typeId)
        {
            int count = 0;
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeIdentityComponent>());
            using NativeArray<AoeIdentityComponent> identities = q.ToComponentDataArray<AoeIdentityComponent>(Allocator.Temp);
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].TypeId == typeId)
                    count++;
            }

            return count;
        }

        private NativeQueue<CombatHitEvent> HitQueue()
        {
            FieldInfo field = typeof(CombatApplyFinalizeSystem).GetField(
                "HitQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<CombatHitEvent>)field.GetValue(hitApply);
        }

        private NativeQueue<AoeSpawnEvent> AoeEventQueue()
        {
            FieldInfo field = typeof(AoeSpawnExpansionSystem).GetField(
                "EventQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<AoeSpawnEvent>)field.GetValue(aoeExpansion);
        }

        private NativeQueue<ProjectileSpawnEvent> ProjectileEventQueue()
        {
            FieldInfo field = typeof(ProjectileSpawnExpansionSystem).GetField(
                "EventQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<ProjectileSpawnEvent>)field.GetValue(projectileExpansion);
        }

        private sealed class TestCombatTarget : ICombatTarget
        {
            public TestCombatTarget(int targetId)
            {
                TargetId = targetId;
            }

            public Entity Proxy { get; set; }
            public float2 Position { get; set; }
            public float Radius { get; set; }
            public int Mask { get; set; }
            public int HitCount { get; private set; }
            public List<CombatHitData> Hits { get; } = new();
            public List<StatusStackSnapshot> StatusSnapshots { get; } = new();
            public int StatusPushCount { get; private set; }
            public bool ClampHealthOnHit { get; set; }
            public float Health { get; set; } = 100f;
            public float LastPreClampHealth { get; private set; }
            public bool Active { get; private set; } = true;
            public bool DeactivateOnHit { get; set; }
            public int TargetId { get; }
            public Entity CombatTargetProxy
            {
                get => Proxy;
                set => Proxy = value;
            }
            public Vector2 CombatTargetPosition => new(Position.x, Position.y);
            public float CombatTargetRadius => Radius;
            public Vector2 CombatTargetHalfExtents => Vector2.zero;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => Mask;
            public float CombatMaxHealth => Health;
            public bool IsCombatTargetActive => Active;
            public void ReceiveHit(in CombatHitData hit)
            {
                HitCount++;
                Hits.Add(hit);
                if (ClampHealthOnHit && hit.DirectDamageEnabled)
                {
                    LastPreClampHealth = Health - hit.Damage.Amount;
                    Health = Mathf.Max(0f, LastPreClampHealth);
                    if (Health <= 0f)
                    {
                        Active = false;
                    }
                }

                if (DeactivateOnHit)
                {
                    Active = false;
                }
            }

            public void ReceiveStatus(IReadOnlyList<StatusStackSnapshot> stacks)
            {
                StatusPushCount++;
                StatusSnapshots.Clear();
                for (int i = 0; i < stacks.Count; i++)
                {
                    StatusSnapshots.Add(stacks[i]);
                }
            }
        }
    }
}
