using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
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
        private ImpactAoeSpawnExpansionSystem impactAoeExpansion;
        private LingeringAoeSpawnExpansionSystem lingeringAoeExpansion;
        private ProjectileSpawnExpansionSystem projectileExpansion;
        private CombatApplyFinalizeSingleSystem hitApply;
        private HitEnergyActivationSystem hitEnergyActivation;
        private TargetProxyCreateApplySystem targetProxyCreateApply;
        private TargetProxyUpdateApplySystem targetProxyUpdateApply;
        private TargetProxyDeleteApplySystem targetProxyDeleteApply;
        private Entity scopeEntity;
        private SpawnTemplateRegistryState templateRegistryState;
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
            impactAoeExpansion = testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>();
            lingeringAoeExpansion = testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>();
            projectileExpansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            hitApply = testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>();
            hitEnergyActivation = testWorld.GetOrCreateSystemManaged<HitEnergyActivationSystem>();
            targetProxyCreateApply = testWorld.GetOrCreateSystemManaged<TargetProxyCreateApplySystem>();
            targetProxyUpdateApply = testWorld.GetOrCreateSystemManaged<TargetProxyUpdateApplySystem>();
            targetProxyDeleteApply = testWorld.GetOrCreateSystemManaged<TargetProxyDeleteApplySystem>();
            simGroup.AddSystemToUpdateList(targetProxyCreateApply);
            simGroup.AddSystemToUpdateList(targetProxyUpdateApply);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatArmingSystem>());
            simGroup.AddSystemToUpdateList(impactAoeExpansion);
            simGroup.AddSystemToUpdateList(lingeringAoeExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatLifetimeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoePulseVfxSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<TargetSpatialHashSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ImpactAoeCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<LingeringAoeCollisionSystem>());
            simGroup.AddSystemToUpdateList(hitApply);
            simGroup.AddSystemToUpdateList(hitEnergyActivation);
            simGroup.AddSystemToUpdateList(projectileExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileDiscreteSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileDiscreteCollisionSystem>());
            // Collision/arming/lifetime/expansion now write the VFX lane unconditionally, so its
            // owning system must exist (to create the lane singleton) and tick (to drain it). Added
            // to simGroup so it drains on TickSimulationOnly too; it no-ops without a VfxRoot.
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>());
            simGroup.SortSystems();

            presentationGroup = testWorld.GetOrCreateSystemManaged<PresentationSystemGroup>();
            presentationGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyBridge>());
            presentationGroup.AddSystemToUpdateList(targetProxyDeleteApply);
            presentationGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            templateRegistryState = SpawnTemplateRegistryTestState.Add(entityManager, scopeEntity);
            entityManager.AddBuffer<ImpactAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<LingeringAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<TargetProxyCreateEvent>(scopeEntity);
            entityManager.AddBuffer<TargetProxyUpdateEvent>(scopeEntity);
            entityManager.AddBuffer<TargetProxyDeleteEvent>(scopeEntity);

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
            SpawnTemplateRegistryTestState.Dispose(ref templateRegistryState);
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
        public void AoeEchoScatterKeepsOneCopyAtAimLocation()
        {
            const int TypeId = 9103;
            float2 aimLocation = new(3.5f, -1.25f);

            SpawnEchoAoe(TypeId, aimLocation, echoCount: 4, scatterRadius: 3f, jitterSeed: 987u, sourceId: 2050);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] snapshots = ReadAoeSnapshotsByType(TypeId);
            Assert.That(snapshots, Has.Length.EqualTo(4));
            bool hasCopyAtAimLocation = false;
            for (int i = 0; i < snapshots.Length; i++)
            {
                hasCopyAtAimLocation |= math.lengthsq(snapshots[i].Position - aimLocation) < 0.000001f;
            }

            Assert.That(hasCopyAtAimLocation, Is.True);
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
        public void AoeEchoScatterChangesByDeterministicTickIndex()
        {
            const int TypeA = 9113;
            const int TypeB = 9114;
            const int TypeC = 9115;
            const int EchoCount = 4;
            const int SourceId = 3300;
            const uint JitterSeed = 1001u;
            float2 center = new(-4f, -6f);

            SpawnEchoAoe(
                TypeA,
                center,
                EchoCount,
                scatterRadius: 2.5f,
                JitterSeed,
                SourceId,
                deterministicIdTickIndex: 7);
            SpawnEchoAoe(
                TypeB,
                center,
                EchoCount,
                scatterRadius: 2.5f,
                JitterSeed,
                SourceId,
                deterministicIdTickIndex: 7);
            SpawnEchoAoe(
                TypeC,
                center,
                EchoCount,
                scatterRadius: 2.5f,
                JitterSeed,
                SourceId,
                deterministicIdTickIndex: 8);
            TickSimulationOnly(0.01f);

            AoeSpawnSnapshot[] firstTick = ReadAoeSnapshotsByType(TypeA);
            AoeSpawnSnapshot[] sameTick = ReadAoeSnapshotsByType(TypeB);
            AoeSpawnSnapshot[] nextTick = ReadAoeSnapshotsByType(TypeC);

            Assert.That(firstTick, Has.Length.EqualTo(EchoCount));
            Assert.That(sameTick, Has.Length.EqualTo(EchoCount));
            Assert.That(nextTick, Has.Length.EqualTo(EchoCount));

            bool anyTickDifference = false;
            for (int i = 0; i < EchoCount; i++)
            {
                AssertFloat2(sameTick[i].Position, firstTick[i].Position);
                anyTickDifference |= math.lengthsq(nextTick[i].Position - firstTick[i].Position) > 0.000001f;
            }

            Assert.That(anyTickDifference, Is.True);
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
        public void ImpactAoeArmSecondsDelaysOneShotCollisionUntilArmed()
        {
            AddTarget(float2.zero, radius: 0.25f, targetMask: 0);
            SpawnCircle(float2.zero, radius: 1f, damage: 2f, armSeconds: 0.05f);

            TickSimulationOnly(0.01f);

            Entity impact = FirstImpactAoeEntity();
            Assert.That(entityManager.IsComponentEnabled<Active>(impact), Is.True);
            Assert.That(entityManager.IsComponentEnabled<ArmingTag>(impact), Is.True);

            TickSimulationOnly(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
            Assert.That(entityManager.IsComponentEnabled<Active>(impact), Is.True);

            TickSimulationOnly(0.05f);
            Assert.That(entityManager.IsComponentEnabled<ArmingTag>(impact), Is.False);
            Assert.That(ReadHitCount(), Is.EqualTo(1));
            Assert.That(entityManager.IsComponentEnabled<Active>(impact), Is.False);
        }

        [Test]
        public void AoeVariantEventsRouteToMatchingCommandListsAndEntities()
        {
            const int ImpactTypeId = 9140;
            const int LingeringTypeId = 9141;
            Hash128 impactKey = RegisterAoeTemplateForVariant(ImpactTypeId, lifetime: 0f);
            Hash128 lingeringKey = RegisterAoeTemplateForVariant(LingeringTypeId, lifetime: 5f);

            AppendAoeEvent(
                IntervalChildKind.ImpactAoe,
                impactKey,
                float2.zero,
                CombatFaction.Player,
                sourceId: 7000);
            AppendAoeEvent(
                IntervalChildKind.LingeringAoe,
                lingeringKey,
                new float2(1f, 0f),
                CombatFaction.Player,
                sourceId: 8000);

            impactAoeExpansion.Update();
            lingeringAoeExpansion.Update();
            CompletePendingHandle(impactAoeExpansion);
            CompletePendingHandle(lingeringAoeExpansion);

            NativeList<AoeSpawnCommand> impactCommands =
                AoeCommandList(impactAoeExpansion, "ImpactCommands");
            NativeList<AoeSpawnCommand> lingeringCommands =
                AoeCommandList(lingeringAoeExpansion, "LingeringCommands");
            Assert.That(impactCommands.IsCreated, Is.True);
            Assert.That(lingeringCommands.IsCreated, Is.True);
            Assert.That(impactCommands.Length, Is.EqualTo(1));
            Assert.That(lingeringCommands.Length, Is.EqualTo(1));
            Assert.That(impactCommands[0].TypeId, Is.EqualTo(ImpactTypeId));
            Assert.That(lingeringCommands[0].TypeId, Is.EqualTo(LingeringTypeId));

            testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnApplySystem>().Update();
            testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnApplySystem>().Update();

            Assert.That(ImpactAoeCount(), Is.EqualTo(1));
            Assert.That(LingeringAoeCount(), Is.EqualTo(1));
            Assert.That(AoeCountByType(ImpactTypeId), Is.EqualTo(1));
            Assert.That(AoeCountByType(LingeringTypeId), Is.EqualTo(1));
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
        public void PulseOverflowHitsFirstTargetsInCellScanOrder()
        {
            const int ExtraTargets = 4;
            int firstTargetId = nextTargetId + 1;
            int targetCount = CollisionConstants.MaxAoeTargetsPerTick + ExtraTargets;
            for (int i = 0; i < targetCount; i++)
            {
                AddTargetById(float2.zero, 0.25f, 1, firstTargetId + i);
            }

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);

            Assert.That(ReadHitCount(), Is.EqualTo(CollisionConstants.MaxAoeTargetsPerTick));
            for (int i = 0; i < targetCount; i++)
            {
                int expectedHits = i < CollisionConstants.MaxAoeTargetsPerTick ? 1 : 0;
                Assert.That(targetsById[firstTargetId + i].HitCount, Is.EqualTo(expectedHits));
            }
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
        public void LingeringTargetIsNotRehitUntilTickIntervalExpires()
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
        public void LingeringReentryWaitsForNextTickInterval()
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
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Player), Is.True);
            targetProxyCreateApply.Update();
            targetsById.Add(targetId, target);

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0),
                "Player AOE must not hit a Player target �?same-faction skip.");
        }

        [Test]
        public void SelectedFactionSameFactionImpactTargetIsHit()
        {
            int targetId = ++nextTargetId;
            var target = new TestCombatTarget(targetId);
            target.Position = float2.zero;
            target.Radius = 0.25f;
            target.Mask = 1;
            target.Health = 100f;
            Assert.That(
                CombatTargetProxy.Create(
                    entityManager, target, TargetFaction.AllowedFrom(CombatFaction.Player, CombatFaction.Player)),
                Is.True);
            targetProxyCreateApply.Update();
            targetsById.Add(targetId, target);

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1),
                "AllowedFactionOnly must accept an attacker faction equal to the target's own faction.");
        }

        [Test]
        public void SelectedFactionUnselectedAttackerImpactTargetIsNotHit()
        {
            int targetId = ++nextTargetId;
            var target = new TestCombatTarget(targetId);
            target.Position = float2.zero;
            target.Radius = 0.25f;
            target.Mask = 1;
            target.Health = 100f;
            Assert.That(
                CombatTargetProxy.Create(
                    entityManager, target, TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Mob)),
                Is.True);
            targetProxyCreateApply.Update();
            targetsById.Add(targetId, target);

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0),
                "Player AOE must not hit a target whose AllowedFactionOnly policy excludes Player.");
        }

        [Test]
        public void SelectedFactionLingeringTargetStillGatedByRepeatCooldown()
        {
            int targetId = ++nextTargetId;
            var target = new TestCombatTarget(targetId);
            target.Position = float2.zero;
            target.Radius = 0.25f;
            target.Mask = 1;
            target.Health = 100f;
            Assert.That(
                CombatTargetProxy.Create(
                    entityManager, target, TargetFaction.AllowedFrom(CombatFaction.Player, CombatFaction.Player)),
                Is.True);
            targetProxyCreateApply.Update();
            targetsById.Add(targetId, target);

            SpawnCircle(float2.zero, 1f, 2f, lifetime: 10f, tickInterval: 0.02f);

            Tick(0.01f);
            int hits = ReadHitCount();
            Tick(0.01f);
            hits += ReadHitCount();
            Tick(0.02f);
            hits += ReadHitCount();

            Assert.That(hits, Is.EqualTo(2),
                "The repeat-hit tick gate must still apply to a target selected via AllowedFactionOnly policy.");
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
        public void AoeReuse_OverwritesRenderBatchId()
        {
            const int FirstRenderType = 50;
            const int SecondRenderType = 60;

            SpawnCircle(float2.zero, 1f, 1f, renderTypeId: FirstRenderType);
            Tick(0.01f);
            Entity first = FirstAoeEntity();
            Assert.That(entityManager.GetComponentData<CombatRenderKindId>(first).Value, Is.EqualTo(FirstRenderType));

            SpawnCircle(float2.zero, 1f, 1f, renderTypeId: SecondRenderType);
            Tick(0.01f);
            Entity reused = FirstAoeEntity();

            Assert.That(reused, Is.EqualTo(first));
            Assert.That(entityManager.GetComponentData<CombatRenderKindId>(reused).Value, Is.EqualTo(SecondRenderType));
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

            Assert.That(entityManager.HasComponent<LingeringAoeTag>(impact), Is.False);
            Assert.That(entityManager.HasComponent<AoePulseVfxComponent>(impact), Is.False);
            Assert.That(entityManager.HasComponent<TimedSpawnComponent>(impact), Is.False);
            Assert.That(entityManager.HasComponent<LingeringAoeTag>(lingering), Is.True);
            Assert.That(entityManager.HasComponent<AoePulseVfxComponent>(lingering), Is.True);
            Assert.That(entityManager.HasComponent<TimedSpawnComponent>(lingering), Is.True);
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(lingering), Is.False);

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
            Assert.That(entityManager.IsComponentEnabled<CombatCollisionActiveTag>(impact), Is.False);
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
        public void LingeringTimedSpawnSlotsReuseAcrossEnabledState()
        {
            SpawnCircle(float2.zero, 1f, 1f, lifetime: 0.001f, tickInterval: 0.05f);
            Tick(0.01f);
            Tick(0.01f);
            Entity firstLingering = FirstLingeringAoeEntity();
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(firstLingering), Is.False);

            SpawnTimedCircle(float2.zero, lifetime: 0.001f);
            Tick(0.01f);
            Entity reusedAsTimed = FirstLingeringAoeEntity();
            Assert.That(reusedAsTimed, Is.EqualTo(firstLingering));
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(reusedAsTimed), Is.True);

            Tick(0.01f);
            SpawnCircle(float2.zero, 1f, 1f, lifetime: 0.001f, tickInterval: 0.05f);
            Tick(0.01f);
            Entity reusedAsNonTimed = FirstLingeringAoeEntity();

            Assert.That(reusedAsNonTimed, Is.EqualTo(firstLingering));
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(reusedAsNonTimed), Is.False);
            Assert.That(TotalAoeCount(), Is.EqualTo(1));
        }

        [Test]
        public void ParallelApply_ImpactOverflowReportsSplitThenConverges()
        {
            var impactApply = testWorld.GetExistingSystemManaged<ImpactAoeSpawnApplySystem>();
            CreateDisabledImpactAoeSlot();

            SpawnCircle(float2.zero, 1f, 1f, echoCount: 4);
            TickSimulationOnly(0.01f);

            Assert.That(ReadInternalInt(impactApply, "LastReuseCount"), Is.EqualTo(1));
            Assert.That(ReadInternalInt(impactApply, "LastColdCreateCount"), Is.EqualTo(3));
            Assert.That(ImpactAoeCount(), Is.EqualTo(4));

            DisableAllImpactAoes();
            SpawnCircle(float2.zero, 1f, 1f, echoCount: 4);
            TickSimulationOnly(0.01f);

            Assert.That(ReadInternalInt(impactApply, "LastReuseCount"), Is.EqualTo(4));
            Assert.That(ReadInternalInt(impactApply, "LastColdCreateCount"), Is.EqualTo(0));
            Assert.That(ImpactAoeCount(), Is.EqualTo(4));
        }

        [Test]
        public void ParallelApply_LingeringOverflowReportsSplitAndResetsTimedSpawn()
        {
            var lingeringApply = testWorld.GetExistingSystemManaged<LingeringAoeSpawnApplySystem>();
            Entity disabledTimedSlot = CreateDisabledLingeringAoeSlot(timedSpawnEnabled: true);

            SpawnCircle(float2.zero, 1f, 1f, lifetime: 10f, tickInterval: 0.05f, echoCount: 4);
            TickSimulationOnly(0.01f);

            Assert.That(ReadInternalInt(lingeringApply, "LastReuseCount"), Is.EqualTo(1));
            Assert.That(ReadInternalInt(lingeringApply, "LastColdCreateCount"), Is.EqualTo(3));
            Assert.That(LingeringAoeCount(), Is.EqualTo(4));
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(disabledTimedSlot), Is.False);
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
            targetProxyUpdateApply.Update();
            TargetPosition pushed = entityManager.GetComponentData<TargetPosition>(target.Proxy);
            Assert.That(pushed.Value.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(pushed.Value.y, Is.EqualTo(0f).Within(0.001f));

            SpawnCircle(float2.zero, 1f, 2f);
            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));

            CombatTargetProxy.Delete(entityManager, target.Proxy);
            targetProxyDeleteApply.Update();
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
            Assert.That(entityManager.GetComponentData<Health>(proxy).Current, Is.EqualTo(finalized[0].Health).Within(0.0001f));

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
            Assert.That(entityManager.GetComponentData<Health>(target.Proxy).Current, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
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
            Assert.That(entityManager.GetComponentData<Health>(target.Proxy).Current, Is.EqualTo(SeedHealth - TotalDamage).Within(0.0001f));
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
            targetProxyDeleteApply.Update();
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
        public void HitEnergyActivationExpiresPartialEnergyWithoutOutput()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueHitEnergyHit(target.Proxy, HitEnergy(accumulatorId: 101, energyRequired: 2f, retentionSeconds: 0.05f, energyPerHit: 1f, unusedArea: 1f, outputTypeId: 7));

            TickStatusPipelineOnly(0f);
            Assert.That(ReadHitEnergyEntry(target.Proxy, 101).StoredEnergy, Is.EqualTo(1f).Within(0.0001f));

            TickStatusPipelineOnly(0.06f);

            Assert.That(TryReadHitEnergyEntry(target.Proxy, 101, out _), Is.False);
            Assert.That(ImpactAoeEventQueue().Count + LingeringAoeEventQueue().Count, Is.EqualTo(0));
        }

        [Test]
        public void HitEnergyProjectileOutputExpiresPartialEnergyWithoutNova()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueHitEnergyHit(
                target.Proxy,
                ProjectileHitEnergy(
                accumulatorId: 301,
                energyRequired: 2f,
                retentionSeconds: 0.05f,
                energyPerHit: 1f,
                unusedProjectileCount: 3));

            TickStatusPipelineOnly(0f);
            Assert.That(ReadHitEnergyEntry(target.Proxy, 301).StoredEnergy, Is.EqualTo(1f).Within(0.0001f));

            TickStatusPipelineOnly(0.06f);

            Assert.That(TryReadHitEnergyEntry(target.Proxy, 301, out _), Is.False);
            Assert.That(ProjectileEventQueue().Count, Is.EqualTo(0));
        }

        [Test]
        public void HitEnergyFractionalDepositsActivateOnceAndRetainRemainderNextUpdate()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueHitEnergyHit(target.Proxy, HitEnergy(accumulatorId: 102, energyRequired: 1f, retentionSeconds: 10f, energyPerHit: 0.4f, unusedArea: 1f, outputTypeId: 7));
            QueueHitEnergyHit(target.Proxy, HitEnergy(accumulatorId: 102, energyRequired: 1f, retentionSeconds: 10f, energyPerHit: 0.4f, unusedArea: 2f, outputTypeId: 7));
            TickStatusPipelineOnly(0f);

            TargetHitEnergy partial = ReadHitEnergyEntry(target.Proxy, 102);
            Assert.That(partial.StoredEnergy, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(ImpactAoeEventQueue().Count, Is.Zero, "Activation runs before same-update finalization.");

            QueueHitEnergyHit(target.Proxy, HitEnergy(accumulatorId: 102, energyRequired: 1f, retentionSeconds: 10f, energyPerHit: 0.4f, unusedArea: 3f, outputTypeId: 7));
            TickStatusPipelineOnly(0f);
            Assert.That(ImpactAoeEventQueue().Count, Is.Zero, "Threshold crossing does not activate until next update.");
            TickStatusPipelineOnly(0f);

            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(1));
            Assert.That(ReadHitEnergyEntry(target.Proxy, 102).StoredEnergy, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void HitEnergyAoeActivationPreservesTargetPosition()
        {
            AddTarget(new float2(3f, -2f), 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: 104, energyRequired: 2f, retentionSeconds: 10f,
                energyPerHit: 1f, unusedArea: 2f, outputTypeId: 77));
            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: 104, energyRequired: 2f, retentionSeconds: 10f,
                energyPerHit: 1f, unusedArea: 4f, outputTypeId: 77));

            TickStatusPipelineOnly(0f); // Finalize accrues to threshold
            TickStatusPipelineOnly(0f); // Status detonates on the following tick

            ImpactAoeSpawnEvent output = DequeueSingleImpactAoeEvent();
            Assert.That(output.Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(output.Position.y, Is.EqualTo(-2f).Within(0.0001f));
        }

        [Test]
        public void HitEnergyActivationRoutesLingeringAoeOutput()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];
            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: 119,
                energyRequired: 1f,
                retentionSeconds: 10f,
                energyPerHit: 1f,
                unusedArea: 1f,
                outputTypeId: 8,
                kind: HitEnergySpawnKind.LingeringAoe));

            TickStatusPipelineOnly(0f);
            Assert.That(LingeringAoeEventQueue().Count, Is.Zero);
            TickStatusPipelineOnly(0f);

            Assert.That(LingeringAoeEventQueue().Count, Is.EqualTo(1));
            Assert.That(ImpactAoeEventQueue().Count, Is.Zero);
        }

        [Test]
        public void HitEnergyLargeDepositBanksRemainderAfterActivation()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            HitEnergyPayload hitEnergy = HitEnergy(
                accumulatorId: 120, energyRequired: 3f, retentionSeconds: 10f,
                energyPerHit: 2f, unusedArea: 1f, outputTypeId: 7);

            // Hit 1 banks 2 energy (< 3): no activation yet.
            QueueHitEnergyHit(target.Proxy, hitEnergy);
            TickStatusPipelineOnly(0f);
            Assert.That(ReadHitEnergyEntry(target.Proxy, 120).StoredEnergy, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(0));

            // Hit 2 banks to 4 (>= 3): detonates in two hits, not three. One full
            // threshold is consumed and the sub-threshold remainder stays banked.
            QueueHitEnergyHit(target.Proxy, hitEnergy);
            TickStatusPipelineOnly(0f); // Finalize banks to 4; activation already ran
            TickStatusPipelineOnly(0f); // Activation consumes one requirement; remainder stays banked
            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(1));
            Assert.That(ReadHitEnergyEntry(target.Proxy, 120).StoredEnergy, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void HitEnergyBurstFiresOneActivationPerCompleteRequirement()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            // Four applicator hits land in one update at requirement 2; following activation
            // emits twice without losing overflow.
            for (int i = 0; i < 4; i++)
            {
                QueueHitEnergyHit(target.Proxy, HitEnergy(
                    accumulatorId: 121, energyRequired: 2f, retentionSeconds: 10f,
                    energyPerHit: 1f, unusedArea: 1f, outputTypeId: 7));
            }

            TickStatusPipelineOnly(0f); // Finalize banks all four after activation phase
            TickStatusPipelineOnly(0f); // Activation emits twice on following update

            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(2));
            Assert.That(TryReadHitEnergyEntry(target.Proxy, 121, out _), Is.False);
        }

        [Test]
        public void HitEnergyActivationCapsLargeOverflowAndRetainsUnemittedEnergy()
        {
            const int MaxActivationsPerUpdate = 256;
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];
            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: 122,
                energyRequired: 1f,
                retentionSeconds: 10f,
                energyPerHit: 300f,
                unusedArea: 1f,
                outputTypeId: 7));

            TickStatusPipelineOnly(0f);
            Assert.That(ImpactAoeEventQueue().Count, Is.Zero, "Deposit cannot activate in same update.");
            TickStatusPipelineOnly(0f);

            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(MaxActivationsPerUpdate));
            Assert.That(ReadHitEnergyEntry(target.Proxy, 122).StoredEnergy,
                Is.EqualTo(44f).Within(0.0001f));
        }

        [Test]
        public void HitEnergyAccrualDropsNewAccumulatorWhenBufferIsFull()
        {
            const int MaxEntries = 32;
            const int FirstAccumulatorId = 2000;
            const int OverflowAccumulatorId = 9999;
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            for (int i = 0; i < MaxEntries; i++)
            {
                QueueHitEnergyHit(target.Proxy, HitEnergy(
                    accumulatorId: FirstAccumulatorId + i, energyRequired: 100f,
                    retentionSeconds: 10f, energyPerHit: 1f, unusedArea: 1f, outputTypeId: 7));
            }

            TickStatusPipelineOnly(0f);
            DynamicBuffer<TargetHitEnergy> entries = entityManager.GetBuffer<TargetHitEnergy>(target.Proxy);
            Assert.That(entries.Length, Is.EqualTo(MaxEntries));

            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: OverflowAccumulatorId, energyRequired: 100f,
                retentionSeconds: 10f, energyPerHit: 1f, unusedArea: 1f, outputTypeId: 7));

            TickStatusPipelineOnly(0f);

            entries = entityManager.GetBuffer<TargetHitEnergy>(target.Proxy);
            Assert.That(entries.Length, Is.EqualTo(MaxEntries));
            Assert.That(TryReadHitEnergyEntry(target.Proxy, OverflowAccumulatorId, out _), Is.False);
            Assert.That(ReadHitEnergyEntry(target.Proxy, FirstAccumulatorId).StoredEnergy, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void HitEnergyAccrualRefreshesExistingAccumulatorWhenBufferIsFull()
        {
            const int MaxEntries = 32;
            const int FirstAccumulatorId = 3000;
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            for (int i = 0; i < MaxEntries; i++)
            {
                QueueHitEnergyHit(target.Proxy, HitEnergy(
                    accumulatorId: FirstAccumulatorId + i, energyRequired: 100f,
                    retentionSeconds: 10f, energyPerHit: 1f, unusedArea: 1f, outputTypeId: 7));
            }

            TickStatusPipelineOnly(0f);

            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: FirstAccumulatorId, energyRequired: 100f,
                retentionSeconds: 20f, energyPerHit: 4f, unusedArea: 2f, outputTypeId: 7));

            TickStatusPipelineOnly(0f);

            DynamicBuffer<TargetHitEnergy> entries = entityManager.GetBuffer<TargetHitEnergy>(target.Proxy);
            TargetHitEnergy refreshed = ReadHitEnergyEntry(target.Proxy, FirstAccumulatorId);
            Assert.That(entries.Length, Is.EqualTo(MaxEntries));
            Assert.That(refreshed.StoredEnergy, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(refreshed.ExpiresAt, Is.EqualTo(elapsedTime + 20f).Within(0.0001));
        }

        [Test]
        public void HitEnergyProgressPushFiresOnlyWhenEnergyChanges()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueHitEnergyHit(target.Proxy, HitEnergy(
                accumulatorId: 105, energyRequired: 3f, retentionSeconds: 5f,
                energyPerHit: 1f, unusedArea: 1f, outputTypeId: 7));

            TickStatusPipelineOnly(0f);
            presentationGroup.Update();

            Assert.That(target.HitEnergyPushCount, Is.EqualTo(1));
            Assert.That(target.HitEnergySnapshots, Has.Count.EqualTo(1));
            Assert.That(target.HitEnergySnapshots[0].AccumulatorId, Is.EqualTo(105));
            Assert.That(target.HitEnergySnapshots[0].StoredEnergy, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(target.HitEnergySnapshots[0].EnergyRequired, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(target.HitEnergySnapshots[0].RetentionRemaining, Is.EqualTo(5f).Within(0.0001f));

            TickStatusPipelineOnly(0.25f);
            presentationGroup.Update();

            Assert.That(target.HitEnergyPushCount, Is.EqualTo(1));
        }

        [Test]
        public void AoeApplicatorHitEnergyQueuesProjectileNovaFromRegisteredTemplate()
        {
            const int ProjectileCount = 5;
            const int OutputTypeId = 70;
            var outputTemplateKey = new Hash128(0xBEEFu, (uint)OutputTypeId, 0u, 0u);
            RegisterProjectileTemplate(outputTemplateKey, new ProjectileSpawnCommand
            {
                TypeId = OutputTypeId,
                Count = ProjectileCount,
                Speed = 5f
            });

            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(
                float2.zero,
                1f,
                0f,
                hitEnergy: ProjectileHitEnergy(
                    accumulatorId: 302,
                    energyRequired: 1f,
                    retentionSeconds: 10f,
                    energyPerHit: 1f,
                    unusedProjectileCount: 999));

            // Tick 1: AOE materializes, hits target, Finalize banks the stack (Status ran first).
            // Tick 2: Status detonates the banked stack; projectile expansion materializes the nova.
            TickSimulationOnly(0.01f);
            TickSimulationOnly(0.01f);

            Assert.That(ProjectileCountByTypeId(OutputTypeId), Is.EqualTo(ProjectileCount));
        }

        [Test]
        public void HitEnergyActivationUnknownSpawnKindQueuesNoSpawn()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueHitEnergyHit(target.Proxy, new HitEnergyPayload
            {
                AccumulatorId = 303,
                EnergyRequired = 1f,
                EnergyPerHit = 1f,
                RetentionSeconds = 10f,
                Spawn = new HitEnergySpawn
                {
                    Faction = CombatFaction.Player,
                    Kind = (HitEnergySpawnKind)999,
                    TemplateKey = new Hash128(1u, 0u, 0u, 0u)
                }
            });

            TickStatusPipelineOnly(0f); // Finalize accrues to threshold
            TickStatusPipelineOnly(0f); // Activation reaches unknown-kind switch and enqueues nothing

            Assert.That(ImpactAoeEventQueue().Count + LingeringAoeEventQueue().Count, Is.EqualTo(0));
            Assert.That(ProjectileEventQueue().Count, Is.EqualTo(0));
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
            // Mirror production order: activation runs before finalization, so energy queued for
            // hit queued for this tick is accrued by Finalize (second) and only detonated by
            // activation on following update. Activation therefore lags accrual by one update.
            hitEnergyActivation.Update();
            hitApply.Update();
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
            HitEnergyPayload hitEnergy = default,
            int renderTypeId = 1,
            TimedSpawnComponent timedSpawn = default,
            bool hasTimedSpawner = false,
            int echoCount = 1,
            float armSeconds = 0f)
        {
            var template = new AoeSpawnCommand
            {
                TypeId = 1,
                RenderTypeId = renderTypeId,
                Lifetime = lifetime,
                ArmSeconds = armSeconds,
                RepeatHitCooldownSeconds = tickInterval,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = damage,
                    DirectDamageEnabled = damage > 0f,
                    HitEnergy = hitEnergy
                },
                Radius = radius,
                AreaSize = radius,
                ShapeType = CombatShapeType.Circle,
                EchoCount = echoCount,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                TimedSpawn = timedSpawn
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            registry.Map.TryAdd(key, template);
            AppendAoeEvent(
                AoeVariant.AoeChildKindFor(lifetime),
                key,
                position,
                CombatFaction.Player,
                ++nextAoeId);
        }

        private void SpawnTimedCircle(float2 position, float lifetime)
        {
            SpawnCircle(
                position,
                radius: 1f,
                damage: 1f,
                lifetime: lifetime,
                tickInterval: 0.05f,
                timedSpawn: new TimedSpawnComponent
                {
                    ChildKind = IntervalChildKind.Projectile,
                    TemplateKey = new Hash128(0x1234u, 0x5678u, 0x9ABCu, 0xDEF0u),
                    EnergyPerSecond = 0.01f,
                    EnergyThreshold = 1f,
                    JitterSeed = 7
                },
                hasTimedSpawner: true);
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
            AppendAoeEvent(
                AoeVariant.AoeChildKindFor(template.Lifetime),
                key,
                position,
                CombatFaction.Player,
                sourceId,
                jitterSeed,
                deterministicIdTickIndex);
        }

        private void AppendAoeEvent(
            IntervalChildKind kind,
            Hash128 templateKey,
            float2 position,
            CombatFaction faction,
            int sourceId,
            uint jitterSeed = 0,
            int deterministicIdTickIndex = 0)
        {
            if (kind == IntervalChildKind.LingeringAoe)
            {
                entityManager.GetBuffer<LingeringAoeSpawnEvent>(scopeEntity).Add(new LingeringAoeSpawnEvent
                {
                    Kind = kind,
                    TemplateKey = templateKey,
                    Position = position,
                    Faction = faction,
                    SourceId = sourceId,
                    JitterSeed = jitterSeed,
                    DeterministicIdTickIndex = deterministicIdTickIndex
                });
                return;
            }

            entityManager.GetBuffer<ImpactAoeSpawnEvent>(scopeEntity).Add(new ImpactAoeSpawnEvent
            {
                Kind = IntervalChildKind.ImpactAoe,
                TemplateKey = templateKey,
                Position = position,
                Faction = faction,
                SourceId = sourceId,
                JitterSeed = jitterSeed,
                DeterministicIdTickIndex = deterministicIdTickIndex
            });
        }

        private Hash128 RegisterAoeTemplateForVariant(int typeId, float lifetime)
        {
            var template = new AoeSpawnCommand
            {
                TypeId = typeId,
                Lifetime = lifetime,
                RepeatHitCooldownSeconds = lifetime > 0f ? 0.1f : 0f,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                },
                Radius = 1f,
                AreaSize = 1f,
                ShapeType = CombatShapeType.Circle,
                EchoCount = 1
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            registry.Map.TryAdd(key, template);
            return key;
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
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Mob), Is.True);
            targetProxyCreateApply.Update();
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

        private int ImpactAoeCount()
        {
            using EntityQuery q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithNone<LingeringAoeTag>()
                .Build(entityManager);
            return q.CalculateEntityCount();
        }

        private int LingeringAoeCount()
        {
            using EntityQuery q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<LingeringAoeTag>()
                .Build(entityManager);
            return q.CalculateEntityCount();
        }

        private Entity CreateDisabledImpactAoeSlot()
        {
            Entity entity = entityManager.CreateEntity(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(AoeHitGateComponent),
                typeof(CombatHitPayload),
                typeof(AoeAreaComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent));

            entityManager.SetComponentEnabled<Active>(entity, false);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, false);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            return entity;
        }

        private Entity CreateDisabledLingeringAoeSlot(bool timedSpawnEnabled)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(AoeTag),
                typeof(LingeringAoeTag),
                typeof(AoeIdentityComponent),
                typeof(CombatLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(CombatHitPayload),
                typeof(AoeAreaComponent),
                typeof(AoePulseVfxComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            entityManager.SetComponentEnabled<Active>(entity, false);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, false);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            entityManager.SetComponentEnabled<TimedSpawnComponent>(entity, timedSpawnEnabled);
            return entity;
        }

        private void DisableAllImpactAoes()
        {
            using EntityQuery q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithNone<LingeringAoeTag>()
                .Build(entityManager);
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                entityManager.SetComponentEnabled<Active>(entities[i], false);
                entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entities[i], false);
            }
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
                .WithNone<LingeringAoeTag>()
                .Build(entityManager);
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No impact AOE entities found.");
            return entities[0];
        }

        private Entity FirstLingeringAoeEntity()
        {
            using EntityQuery q = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<LingeringAoeTag>()
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
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatApplyResultSingleton>());
            if (query.IsEmptyIgnoreFilter)
            {
                return global::System.Array.Empty<CombatTickResult>();
            }

            CombatApplyResultSingleton lane = query.GetSingleton<CombatApplyResultSingleton>();
            lane.ProducerHandle.Complete();
            NativeList<CombatTickResult> results = lane.Results;
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

        private void QueueHitEnergyHit(Entity target, HitEnergyPayload hitEnergy)
        {
            Entity source = CreateHitSource(new CombatHitPayload
            {
                DirectDamageEnabled = false,
                HitEnergy = hitEnergy
            });
            NativeQueue<CombatHitEvent> hitQueue = HitQueue();
            hitQueue.Enqueue(new CombatHitEvent
            {
                Source = source,
                Target = target
            });
        }

        private void QueueDirectHit(
            Entity target,
            float damage,
            float critChance = 0f,
            float critMultiplier = 1f)
        {
            Entity source = CreateHitSource(new CombatHitPayload
            {
                DamageAmount = damage,
                CritChance = critChance,
                CritMultiplier = critMultiplier,
                DirectDamageEnabled = true
            });
            NativeQueue<CombatHitEvent> hitQueue = HitQueue();
            hitQueue.Enqueue(new CombatHitEvent
            {
                Source = source,
                Target = target
            });
        }

        private Entity CreateHitSource(CombatHitPayload payload)
        {
            Entity source = entityManager.CreateEntity(typeof(CombatHitPayload));
            entityManager.SetComponentData(source, payload);
            return source;
        }

        private static HitEnergyPayload HitEnergy(
            int accumulatorId,
            float energyRequired,
            float retentionSeconds,
            float energyPerHit,
            float unusedArea,
            int outputTypeId,
            HitEnergySpawnKind kind = HitEnergySpawnKind.ImpactAoe)
        {
            return new HitEnergyPayload
            {
                AccumulatorId = accumulatorId,
                EnergyRequired = energyRequired,
                EnergyPerHit = energyPerHit,
                RetentionSeconds = retentionSeconds,
                Spawn = new HitEnergySpawn
                {
                    Faction = CombatFaction.Player,
                    Kind = kind,
                    TemplateKey = new Hash128((uint)outputTypeId, 0xAABBCCDDu, 0u, 0u)
                }
            };
        }

        private static HitEnergyPayload ProjectileHitEnergy(
            int accumulatorId,
            float energyRequired,
            float retentionSeconds,
            float energyPerHit,
            int unusedProjectileCount,
            int projectileTypeId = 70)
        {
            return new HitEnergyPayload
            {
                AccumulatorId = accumulatorId,
                EnergyRequired = energyRequired,
                EnergyPerHit = energyPerHit,
                RetentionSeconds = retentionSeconds,
                Spawn = new HitEnergySpawn
                {
                    Faction = CombatFaction.Player,
                    Kind = HitEnergySpawnKind.Projectile,
                    TemplateKey = new Hash128(0xBEEFu, (uint)projectileTypeId, 0u, 0u)
                }
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

        private TargetHitEnergy ReadHitEnergyEntry(Entity target, int accumulatorId)
        {
            Assert.That(TryReadHitEnergyEntry(target, accumulatorId, out TargetHitEnergy entry), Is.True);
            return entry;
        }

        private bool TryReadHitEnergyEntry(Entity target, int accumulatorId, out TargetHitEnergy entry)
        {
            DynamicBuffer<TargetHitEnergy> entries = entityManager.GetBuffer<TargetHitEnergy>(target);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].AccumulatorId == accumulatorId)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private ImpactAoeSpawnEvent DequeueSingleImpactAoeEvent()
        {
            NativeQueue<ImpactAoeSpawnEvent> queue = ImpactAoeEventQueue();
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out ImpactAoeSpawnEvent evt), Is.True);
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
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatHitDispatchSingleton>());
            Assert.That(query.IsEmptyIgnoreFilter, Is.False);
            CombatHitDispatchSingleton lane = query.GetSingleton<CombatHitDispatchSingleton>();
            lane.ProducerHandle.Complete();
            return lane.HitQueue;
        }

        private static void CompletePendingHandle(object system)
        {
            FieldInfo field = system.GetType().GetField(
                "PendingHandle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var handle = (Unity.Jobs.JobHandle)field.GetValue(system);
            handle.Complete();
            field.SetValue(system, handle);
        }

        private static NativeList<AoeSpawnCommand> AoeCommandList(object system, string fieldName)
        {
            FieldInfo field = system.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeList<AoeSpawnCommand>)field.GetValue(system);
        }

        private NativeQueue<ImpactAoeSpawnEvent> ImpactAoeEventQueue()
        {
            FieldInfo field = typeof(ImpactAoeSpawnExpansionSystem).GetField(
                "EventQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<ImpactAoeSpawnEvent>)field.GetValue(impactAoeExpansion);
        }

        private NativeQueue<LingeringAoeSpawnEvent> LingeringAoeEventQueue()
        {
            FieldInfo field = typeof(LingeringAoeSpawnExpansionSystem).GetField(
                "EventQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<LingeringAoeSpawnEvent>)field.GetValue(lingeringAoeExpansion);
        }

        private NativeQueue<ProjectileSpawnEvent> ProjectileEventQueue()
        {
            FieldInfo field = typeof(ProjectileSpawnExpansionSystem).GetField(
                "EventQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<ProjectileSpawnEvent>)field.GetValue(projectileExpansion);
        }

        private static int ReadInternalInt(object target, string fieldName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = target.GetType().GetField(fieldName, Flags);
            Assert.That(field, Is.Not.Null);
            return (int)field.GetValue(target);
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
            public List<HitEnergyProgress> HitEnergySnapshots { get; } = new();
            public int HitEnergyPushCount { get; private set; }
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

            public void ReceiveHitEnergyProgress(IReadOnlyList<HitEnergyProgress> progress)
            {
                HitEnergyPushCount++;
                HitEnergySnapshots.Clear();
                for (int i = 0; i < progress.Count; i++)
                {
                    HitEnergySnapshots.Add(progress[i]);
                }
            }
        }
    }
}
