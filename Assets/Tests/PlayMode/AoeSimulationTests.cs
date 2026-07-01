using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;
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
        private CombatApplyFinalizeSingleSystem hitApply;
        private StatusProcessSystem statusProcess;
        private Entity scopeEntity;
        private Entity aoeTemplateEntity;
        private Entity projectileTemplateEntity;
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
            hitApply = testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>();
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
            simGroup.AddSystemToUpdateList(projectileExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<BasicProjectileSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ChildSpawnerProjectileSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileCollisionSystem>());
            simGroup.SortSystems();

            presentationGroup = testWorld.GetOrCreateSystemManaged<PresentationSystemGroup>();
            presentationGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyBridge>());
            presentationGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<AoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);

            aoeTemplateEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(aoeTemplateEntity, new AoeSpawnTemplate
            {
                Map = new NativeHashMap<Hash128, AoeSpawnCommand>(32, Allocator.Persistent)
            });

            projectileTemplateEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(projectileTemplateEntity, new ProjectileSpawnTemplate
            {
                Map = new NativeHashMap<Hash128, ProjectileSpawnCommand>(16, Allocator.Persistent)
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld.IsCreated)
            {
                if (entityManager.Exists(aoeTemplateEntity))
                {
                    AoeSpawnTemplate templates = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
                    if (templates.Map.IsCreated)
                        templates.Map.Dispose();
                }
                if (entityManager.Exists(projectileTemplateEntity))
                {
                    ProjectileSpawnTemplate templates = entityManager.GetComponentData<ProjectileSpawnTemplate>(projectileTemplateEntity);
                    if (templates.Map.IsCreated)
                        templates.Map.Dispose();
                }
                testWorld.Dispose();
            }
        }

        [Test]
        public void AoeEchoZeroScatterSpawnsOverlappingCopies()
        {
            const int TypeId = 9101;
            const int EchoCount = 4;
            float2 center = new(2.5f, -3.25f);

            SpawnEchoAoe(TypeId, center, EchoCount, scatterRadius: 0f, jitterSeed: 123u, sourceId: 2000);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] snapshots = ReadAoeSnapshotsByType(TypeId);
            Assert.That(snapshots, Has.Length.EqualTo(EchoCount));
            for (int i = 0; i < snapshots.Length; i++)
            {
                AssertFloat2(snapshots[i].Position, center);
            }
        }

        [Test]
        public void AoeEchoScatterStaysWithinRadiusAndMovesCopies()
        {
            const int TypeId = 9102;
            const int EchoCount = 8;
            const float ScatterRadius = 3f;
            float2 center = new(-1f, 4f);

            SpawnEchoAoe(TypeId, center, EchoCount, ScatterRadius, jitterSeed: 456u, sourceId: 2100);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] snapshots = ReadAoeSnapshotsByType(TypeId);
            Assert.That(snapshots, Has.Length.EqualTo(EchoCount));

            bool anyMoved = false;
            float2 first = snapshots[0].Position;
            for (int i = 0; i < snapshots.Length; i++)
            {
                Assert.That(math.distance(snapshots[i].Position, center), Is.LessThanOrEqualTo(ScatterRadius + 0.0001f));
                anyMoved |= math.lengthsq(snapshots[i].Position - first) > 0.000001f;
            }

            Assert.That(anyMoved, Is.True);
        }

        [Test]
        public void AoeEchoScatterIsDeterministicByJitterSeed()
        {
            const int TypeA = 9110;
            const int TypeB = 9111;
            const int TypeC = 9112;
            const int EchoCount = 6;
            float2 center = new(5f, 6f);

            SpawnEchoAoe(TypeA, center, EchoCount, scatterRadius: 2.5f, jitterSeed: 999u, sourceId: 3000);
            SpawnEchoAoe(TypeB, center, EchoCount, scatterRadius: 2.5f, jitterSeed: 999u, sourceId: 3100);
            SpawnEchoAoe(TypeC, center, EchoCount, scatterRadius: 2.5f, jitterSeed: 1000u, sourceId: 3200);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] first = ReadAoeSnapshotsByType(TypeA);
            AoeSpawnSnapshot[] sameSeed = ReadAoeSnapshotsByType(TypeB);
            AoeSpawnSnapshot[] differentSeed = ReadAoeSnapshotsByType(TypeC);

            Assert.That(first, Has.Length.EqualTo(EchoCount));
            Assert.That(sameSeed, Has.Length.EqualTo(EchoCount));
            Assert.That(differentSeed, Has.Length.EqualTo(EchoCount));

            bool anyDifferent = false;
            for (int i = 0; i < EchoCount; i++)
            {
                AssertFloat2(sameSeed[i].Position, first[i].Position);
                anyDifferent |= math.lengthsq(differentSeed[i].Position - first[i].Position) > 0.000001f;
            }

            Assert.That(anyDifferent, Is.True);
        }

        [Test]
        public void AoeEchoScatterBoundsMatchScatteredPosition()
        {
            const int TypeId = 9120;
            const int EchoCount = 5;
            const float Radius = 0.75f;
            float2 center = new(1.25f, -2.5f);

            SpawnEchoAoe(TypeId, center, EchoCount, scatterRadius: 4f, jitterSeed: 222u, sourceId: 4000, radius: Radius);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] snapshots = ReadAoeSnapshotsByType(TypeId);
            Assert.That(snapshots, Has.Length.EqualTo(EchoCount));
            for (int i = 0; i < snapshots.Length; i++)
            {
                CombatCollisionMath.ComputeWorldBounds(
                    snapshots[i].Position,
                    Radius,
                    float2.zero,
                    0f,
                    CombatShapeType.Circle,
                    out float2 expectedMin,
                    out float2 expectedMax);

                AssertFloat2(snapshots[i].BoundsMin, expectedMin);
                AssertFloat2(snapshots[i].BoundsMax, expectedMax);
            }
        }

        [Test]
        public void AoeEchoIdsAreUniqueForSequentialAndDeterministicPaths()
        {
            const int SequentialTypeId = 9130;
            const int DeterministicTypeId = 9131;
            const int EchoCount = 5;

            SpawnEchoAoe(SequentialTypeId, float2.zero, EchoCount, scatterRadius: 0f, jitterSeed: 77u, sourceId: 5000);
            SpawnEchoAoe(
                DeterministicTypeId,
                new float2(2f, 0f),
                EchoCount,
                scatterRadius: 1f,
                jitterSeed: 88u,
                sourceId: 6000,
                deterministicIdTickIndex: 12);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] sequential = ReadAoeSnapshotsByType(SequentialTypeId);
            AoeSpawnSnapshot[] deterministic = ReadAoeSnapshotsByType(DeterministicTypeId);
            Assert.That(sequential, Has.Length.EqualTo(EchoCount));
            Assert.That(deterministic, Has.Length.EqualTo(EchoCount));

            AssertDistinctAoeIds(sequential);
            AssertDistinctAoeIds(deterministic);
            for (int i = 0; i < sequential.Length; i++)
            {
                Assert.That(sequential[i].AoeId, Is.EqualTo(5000 + i));
            }
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
        public void SameFactionTargetIsNotHit()
        {
            int targetId = ++nextTargetId;
            var target = new TestCombatTarget(targetId);
            target.Position = float2.zero;
            target.Radius = 0.25f;
            target.Mask = 1;
            target.Health = 100f;
            target.Proxy = CombatTargetProxy.Create(entityManager, target, CombatFaction.Player);
            targetsById.Add(targetId, target);

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0),
                "Player AOE must not hit a Player target — same-faction skip.");
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
        public void StatusProcessFizzleRemovesPartialStackWithoutDetonation()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 101, threshold: 2, lifetime: 0.05f, damage: 3f, area: 1f, detonationTypeId: 7));

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

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 102, threshold: 3, lifetime: 10f, damage: 2f, area: 1f, detonationTypeId: 7));
            QueueStackHit(target.Proxy, StackEffect(debuffKey: 102, threshold: 3, lifetime: 10f, damage: 5f, area: 2f, detonationTypeId: 7));
            TickStatusPipelineOnly(0f);

            TargetStackEntry partial = ReadStackEntry(target.Proxy, 102);
            Assert.That(partial.Count, Is.EqualTo(2));
            Assert.That(partial.SummedDamage, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(partial.SummedArea, Is.EqualTo(3f).Within(0.0001f));

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 102, threshold: 3, lifetime: 10f, damage: 11f, area: 3f, detonationTypeId: 7));
            TickStatusPipelineOnly(0f);

            Assert.That(AoeEventQueue().Count, Is.EqualTo(1));
            Assert.That(TryReadStackEntry(target.Proxy, 102, out _), Is.False);
        }

        [Test]
        public void StatusProcessAoeDetonationPreservesGeometryAndDamage()
        {
            AddTarget(new float2(3f, -2f), 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: 104,
                threshold: 2,
                lifetime: 10f,
                damage: 4f,
                area: 2f,
                detonationTypeId: 77));
            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: 104,
                threshold: 2,
                lifetime: 10f,
                damage: 6f,
                area: 4f,
                detonationTypeId: 77));

            TickStatusPipelineOnly(0f);

            AoeSpawnEvent detonation = DequeueSingleAoeEvent();
            Assert.That(detonation.Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(detonation.Position.y, Is.EqualTo(-2f).Within(0.0001f));
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
                detonationTypeId: 7));

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
            const int ProjectileCount = 5;
            const int DetonationTypeId = 70;
            var detonationKey = new Hash128(0xBEEFu, (uint)DetonationTypeId, 0u, 0u);
            RegisterProjectileTemplate(detonationKey, new ProjectileSpawnCommand
            {
                TypeId = DetonationTypeId,
                Count = ProjectileCount,
                Speed = 5f
            });

            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(
                float2.zero,
                1f,
                0f,
                stackEffect: ProjectileStackEffect(
                    debuffKey: 302,
                    threshold: 1,
                    lifetime: 10f,
                    damage: 12f,
                    projectileCount: ProjectileCount));

            TickSimulationOnly(0.01f);

            Assert.That(ProjectileCountByTypeId(DetonationTypeId), Is.EqualTo(ProjectileCount));
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
                Faction = CombatFaction.Player,
                DetonationKind = (StackDetonationKind)999,
                DetonationKey = new Hash128(1u, 0u, 0u, 0u)
            });

            TickStatusPipelineOnly(0f);

            Assert.That(AoeEventQueue().Count, Is.EqualTo(0));
            Assert.That(ProjectileEventQueue().Count, Is.EqualTo(0));
        }

        [Test]
        public void AoeOnHitProjectileBurstMaterializesFromRegistry()
        {
            const int ProjectileTypeId = 55;
            var projTemplate = new ProjectileSpawnCommand
            {
                TypeId = ProjectileTypeId,
                Count = 1,
                PierceRemaining = 99,
                Speed = 0f,
                Lifetime = 10f,
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle
            };
            var projKey = SpawnTemplateHash.Of(in projTemplate);
            RegisterProjectileTemplate(projKey, projTemplate);

            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(
                float2.zero, 2f, damage: 0f, lifetime: 5f, tickInterval: 100f,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Projectile, TemplateKey = projKey });

            // Tick 1: lingering AOE materializes, hits target, emits projectile event.
            // Projectile expansion + apply run in the same tick (after status process).
            TickSimulationOnly(0.01f);

            Assert.That(ProjectileCountByTypeId(ProjectileTypeId), Is.EqualTo(1));
        }

        [Test]
        public void AoeOnHitAoeMaterializesFromRegistry()
        {
            const int SecondAoeTypeId = 66;
            var secondAoeTemplate = new AoeSpawnCommand
            {
                TypeId = SecondAoeTypeId,
                Radius = 1f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new CombatHitPayload { DamageAmount = 1f, DirectDamageEnabled = true },
                EchoCount = 1
            };
            var secondAoeKey = SpawnTemplateHash.Of(in secondAoeTemplate);
            AoeSpawnTemplate aoeRegistry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            aoeRegistry.Map.TryAdd(secondAoeKey, secondAoeTemplate);

            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(
                float2.zero, 2f, damage: 0f, lifetime: 5f, tickInterval: 100f,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Aoe, TemplateKey = secondAoeKey });

            // Tick 1: first AOE materializes, hits target, emits second AOE event.
            TickSimulationOnly(0.01f);
            // The on-hit AOE event lands in aoeExpansion.EventQueue during tick 1's collision.
            // aoeExpansion already ran this tick. So the second AOE materializes in tick 2.
            TickSimulationOnly(0.01f);

            Assert.That(AoeCountByType(SecondAoeTypeId), Is.EqualTo(1));
        }

        [Test]
        public void ThreeDeepStackingChain_LingeringAoeToOnHitProjectileToStackDetonation()
        {
            // Chain: Level1=lingering AOE (OnHitSpawn=Projectile)
            //        Level2=on-hit projectile (DirectDamage + StackEffect{threshold=1})
            //        Level3=detonation projectile (spawned by StatusProcessSystem on threshold)
            const int Lvl2TypeId = 20;
            const int Lvl3TypeId = 30;
            const int Lvl3Count = 2;
            const int DebuffKey = 111;

            var detonationKey = new Hash128(0xABCDu, 0x1234u, 0u, 0u);
            var lvl3Template = new ProjectileSpawnCommand
            {
                TypeId = Lvl3TypeId,
                Count = Lvl3Count,
                Speed = 5f,
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle
            };
            RegisterProjectileTemplate(detonationKey, lvl3Template);

            var lvl2Template = new ProjectileSpawnCommand
            {
                TypeId = Lvl2TypeId,
                Count = 1,
                PierceRemaining = 0,
                Speed = 0f,
                Lifetime = 10f,
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true,
                    StackEffect = new StackEffectSnapshot
                    {
                        DebuffKey = DebuffKey,
                        Threshold = 1,
                        Lifetime = 10f,
                        Contribution = new StackContribution { Damage = 5f },
                        DetonationKind = StackDetonationKind.Projectile,
                        DetonationKey = detonationKey
                    }
                })
            };
            var lvl2Key = SpawnTemplateHash.Of(in lvl2Template);
            RegisterProjectileTemplate(lvl2Key, lvl2Template);

            // Level 1: lingering AOE with on-hit projectile spawn. No direct damage.
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(
                float2.zero, 2f, damage: 0f, lifetime: 5f, tickInterval: 100f,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Projectile, TemplateKey = lvl2Key });

            // Tick 1: AOE materializes, hits target, emits lvl-2 projectile event.
            //         Projectile expansion creates lvl-2 entity this tick (after status process).
            //         Projectile collision runs: lvl-2 projectile is contact-gated from the hit target.
            // Ticks 2–10: contact gate ticks down (0.1f / 0.01f = 10 ticks to expire).
            // Tick 11: gate expired, lvl-2 hits target, CombatHitEvent with stack queued.
            // Tick 12: hitApply processes stack (count=1 >= threshold=1);
            //          statusProcess fires lvl-3 detonation event;
            //          projectile expansion creates lvl-3 entities.
            const int TicksToExpireGate = 11;
            const int TicksAfterGate = 2;
            for (int i = 0; i < TicksToExpireGate + TicksAfterGate; i++)
                TickSimulationOnly(0.01f);

            Assert.That(ProjectileCountByTypeId(Lvl3TypeId), Is.GreaterThanOrEqualTo(Lvl3Count),
                "Level-3 detonation projectiles must materialize from the registry-keyed stacking chain.");
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
            StackEffectSnapshot stackEffect = default,
            OnHitSpawnRef onHitSpawn = default)
        {
            var template = new AoeSpawnCommand
            {
                TypeId = 1,
                Lifetime = lifetime,
                RepeatHitCooldownSeconds = tickInterval,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = damage,
                    DirectDamageEnabled = damage > 0f,
                    StackEffect = stackEffect
                },
                OnHitSpawn = onHitSpawn,
                Radius = radius,
                AreaSize = radius,
                ShapeType = CombatShapeType.Circle,
                EchoCount = 1
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            registry.Map.TryAdd(key, template);
            entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity).Add(new AoeSpawnEvent
            {
                Kind = IntervalChildKind.Aoe,
                TemplateKey = key,
                Position = position,
                Faction = CombatFaction.Player,
                SourceId = ++nextAoeId
            });
        }

        private void SpawnEchoAoe(
            int typeId,
            float2 position,
            int echoCount,
            float scatterRadius,
            uint jitterSeed,
            int sourceId,
            int deterministicIdTickIndex = 0,
            float radius = 1f)
        {
            var template = new AoeSpawnCommand
            {
                TypeId = typeId,
                Lifetime = 5f,
                RepeatHitCooldownSeconds = 100f,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                },
                Radius = radius,
                AreaSize = radius,
                ShapeType = CombatShapeType.Circle,
                EchoCount = echoCount,
                ScatterRadius = scatterRadius
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            registry.Map.TryAdd(key, template);
            entityManager.GetBuffer<AoeSpawnEvent>(scopeEntity).Add(new AoeSpawnEvent
            {
                Kind = IntervalChildKind.Aoe,
                TemplateKey = key,
                Position = position,
                Faction = CombatFaction.Player,
                SourceId = sourceId,
                JitterSeed = jitterSeed,
                DeterministicIdTickIndex = deterministicIdTickIndex
            });
        }

        private void RegisterProjectileTemplate(Unity.Entities.Hash128 key, ProjectileSpawnCommand template)
        {
            ProjectileSpawnTemplate registry = entityManager.GetComponentData<ProjectileSpawnTemplate>(projectileTemplateEntity);
            registry.Map.TryAdd(key, template);
        }

        private int ProjectileCountByTypeId(int typeId)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<ProjectileIdentityComponent> identities =
                q.ToComponentDataArray<ProjectileIdentityComponent>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].TypeId == typeId)
                    count++;
            }
            return count;
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
            target.Proxy = CombatTargetProxy.Create(entityManager, target, CombatFaction.Mob);
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

        private static StackEffectSnapshot StackEffect(
            int debuffKey,
            int threshold,
            float lifetime,
            float damage,
            float area,
            int detonationTypeId)
        {
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
                Faction = CombatFaction.Player,
                DetonationKind = StackDetonationKind.Aoe,
                DetonationKey = new Hash128((uint)detonationTypeId, 0xAABBCCDDu, 0u, 0u)
            };
        }

        private static StackEffectSnapshot ProjectileStackEffect(
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
                Faction = CombatFaction.Player,
                DetonationKind = StackDetonationKind.Projectile,
                DetonationKey = new Hash128(0xBEEFu, (uint)projectileTypeId, 0u, 0u)
            };
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

        private AoeSpawnSnapshot[] ReadAoeSnapshotsByType(int typeId)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>());
            using NativeArray<AoeIdentityComponent> identities = q.ToComponentDataArray<AoeIdentityComponent>(Allocator.Temp);
            using NativeArray<CombatKinematicsComponent> kinematics = q.ToComponentDataArray<CombatKinematicsComponent>(Allocator.Temp);
            using NativeArray<CombatCollisionComponent> collisions = q.ToComponentDataArray<CombatCollisionComponent>(Allocator.Temp);

            var snapshots = new List<AoeSpawnSnapshot>();
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].TypeId != typeId)
                    continue;

                snapshots.Add(new AoeSpawnSnapshot(
                    identities[i].AoeId,
                    identities[i].TypeId,
                    kinematics[i].Position,
                    collisions[i].BoundsMin,
                    collisions[i].BoundsMax));
            }

            snapshots.Sort((left, right) => left.AoeId.CompareTo(right.AoeId));
            return snapshots.ToArray();
        }

        private static void AssertFloat2(float2 actual, float2 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
        }

        private static void AssertDistinctAoeIds(AoeSpawnSnapshot[] snapshots)
        {
            var ids = new HashSet<int>();
            for (int i = 0; i < snapshots.Length; i++)
            {
                Assert.That(ids.Add(snapshots[i].AoeId), Is.True, $"Duplicate AOE id {snapshots[i].AoeId}.");
            }
        }

        private NativeQueue<CombatHitEvent> HitQueue()
        {
            FieldInfo field = typeof(CombatApplyFinalizeSingleSystem).GetField(
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

        private readonly struct AoeSpawnSnapshot
        {
            public AoeSpawnSnapshot(int aoeId, int typeId, float2 position, float2 boundsMin, float2 boundsMax)
            {
                AoeId = aoeId;
                TypeId = typeId;
                Position = position;
                BoundsMin = boundsMin;
                BoundsMax = boundsMax;
            }

            public int AoeId { get; }
            public int TypeId { get; }
            public float2 Position { get; }
            public float2 BoundsMin { get; }
            public float2 BoundsMax { get; }
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
