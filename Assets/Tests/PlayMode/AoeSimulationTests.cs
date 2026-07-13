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
            impactAoeExpansion = testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>();
            lingeringAoeExpansion = testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>();
            projectileExpansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            hitApply = testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>();
            statusProcess = testWorld.GetOrCreateSystemManaged<StatusProcessSystem>();
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
            simGroup.AddSystemToUpdateList(statusProcess);
            simGroup.AddSystemToUpdateList(projectileExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileCollisionSystem>());
            simGroup.SortSystems();

            presentationGroup = testWorld.GetOrCreateSystemManaged<PresentationSystemGroup>();
            presentationGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyBridge>());
            presentationGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<ImpactAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<LingeringAoeSpawnEvent>(scopeEntity);
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
            target.Proxy = CombatTargetProxy.Create(entityManager, target, CombatFaction.Player);
            targetsById.Add(targetId, target);

            SpawnCircle(float2.zero, 1f, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0),
                "Player AOE must not hit a Player target �?same-faction skip.");
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
        public void StatusProcessFizzleRemovesPartialStackWithoutDetonation()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            QueueStackHit(target.Proxy, StackEffect(debuffKey: 101, threshold: 2, lifetime: 0.05f, damage: 3f, area: 1f, detonationTypeId: 7));

            TickStatusPipelineOnly(0f);
            Assert.That(ReadStackEntry(target.Proxy, 101).Count, Is.EqualTo(1));

            TickStatusPipelineOnly(0.06f);

            Assert.That(TryReadStackEntry(target.Proxy, 101, out _), Is.False);
            Assert.That(ImpactAoeEventQueue().Count + LingeringAoeEventQueue().Count, Is.EqualTo(0));
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
            TickStatusPipelineOnly(0f); // Finalize accrues to threshold (Status ran first this tick)
            TickStatusPipelineOnly(0f); // Status detonates the banked stack on the following tick

            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(1));
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

            TickStatusPipelineOnly(0f); // Finalize accrues to threshold
            TickStatusPipelineOnly(0f); // Status detonates on the following tick

            ImpactAoeSpawnEvent detonation = DequeueSingleImpactAoeEvent();
            Assert.That(detonation.Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(detonation.Position.y, Is.EqualTo(-2f).Within(0.0001f));
        }

        [Test]
        public void StatusProcessStacksPerHitBanksTowardThreshold()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            StackEffectSnapshot stack = StackEffect(
                debuffKey: 120, threshold: 3, lifetime: 10f, damage: 2f, area: 1f, detonationTypeId: 7);
            stack.StacksPerHit = 2;

            // Hit 1 banks 2 stacks (< 3): no detonation yet.
            QueueStackHit(target.Proxy, stack);
            TickStatusPipelineOnly(0f);
            Assert.That(ReadStackEntry(target.Proxy, 120).Count, Is.EqualTo(2));
            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(0));

            // Hit 2 banks to 4 (>= 3): detonates in two hits, not three. One full
            // threshold is consumed and the sub-threshold remainder stays banked.
            QueueStackHit(target.Proxy, stack);
            TickStatusPipelineOnly(0f); // Finalize banks to 4; Status ran first, so no detonation yet
            TickStatusPipelineOnly(0f); // Status detonates one threshold, remainder stays banked
            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(1));
            Assert.That(ReadStackEntry(target.Proxy, 120).Count, Is.EqualTo(1));
        }

        [Test]
        public void StatusProcessBurstFiresOneDetonationPerThreshold()
        {
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            // Four applicator hits land in one tick at threshold 2 => once Status processes
            // the banked stack it fires two full detonations, not one with the overflow lost.
            for (int i = 0; i < 4; i++)
            {
                QueueStackHit(target.Proxy, StackEffect(
                    debuffKey: 121, threshold: 2, lifetime: 10f, damage: 2f, area: 1f, detonationTypeId: 7));
            }

            TickStatusPipelineOnly(0f); // Finalize banks all four (Status ran first)
            TickStatusPipelineOnly(0f); // Status fires both detonations on the following tick

            Assert.That(ImpactAoeEventQueue().Count, Is.EqualTo(2));
            Assert.That(TryReadStackEntry(target.Proxy, 121, out _), Is.False);
        }

        [Test]
        public void StackAccrualDropsNewDebuffWhenStackBufferIsFull()
        {
            const int MaxStacks = 32;
            const int FirstDebuffKey = 2000;
            const int OverflowDebuffKey = 9999;
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            for (int i = 0; i < MaxStacks; i++)
            {
                QueueStackHit(target.Proxy, StackEffect(
                    debuffKey: FirstDebuffKey + i,
                    threshold: 100,
                    lifetime: 10f,
                    damage: 1f,
                    area: 1f,
                    detonationTypeId: 7));
            }

            TickStatusPipelineOnly(0f);
            DynamicBuffer<TargetStackEntry> entries = entityManager.GetBuffer<TargetStackEntry>(target.Proxy);
            Assert.That(entries.Length, Is.EqualTo(MaxStacks));

            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: OverflowDebuffKey,
                threshold: 100,
                lifetime: 10f,
                damage: 1f,
                area: 1f,
                detonationTypeId: 7));

            TickStatusPipelineOnly(0f);

            entries = entityManager.GetBuffer<TargetStackEntry>(target.Proxy);
            Assert.That(entries.Length, Is.EqualTo(MaxStacks));
            Assert.That(TryReadStackEntry(target.Proxy, OverflowDebuffKey, out _), Is.False);
            Assert.That(ReadStackEntry(target.Proxy, FirstDebuffKey).Count, Is.EqualTo(1));
        }

        [Test]
        public void StackAccrualRefreshesExistingDebuffWhenStackBufferIsFull()
        {
            const int MaxStacks = 32;
            const int FirstDebuffKey = 3000;
            AddTarget(float2.zero, 0.25f, 1);
            TestCombatTarget target = targetsById[nextTargetId];

            for (int i = 0; i < MaxStacks; i++)
            {
                QueueStackHit(target.Proxy, StackEffect(
                    debuffKey: FirstDebuffKey + i,
                    threshold: 100,
                    lifetime: 10f,
                    damage: 1f,
                    area: 1f,
                    detonationTypeId: 7));
            }

            TickStatusPipelineOnly(0f);

            QueueStackHit(target.Proxy, StackEffect(
                debuffKey: FirstDebuffKey,
                threshold: 100,
                lifetime: 20f,
                damage: 4f,
                area: 2f,
                detonationTypeId: 7));

            TickStatusPipelineOnly(0f);

            DynamicBuffer<TargetStackEntry> entries = entityManager.GetBuffer<TargetStackEntry>(target.Proxy);
            TargetStackEntry refreshed = ReadStackEntry(target.Proxy, FirstDebuffKey);
            Assert.That(entries.Length, Is.EqualTo(MaxStacks));
            Assert.That(refreshed.Count, Is.EqualTo(2));
            Assert.That(refreshed.SummedDamage, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(refreshed.SummedArea, Is.EqualTo(3f).Within(0.0001f));
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

            // Tick 1: AOE materializes, hits target, Finalize banks the stack (Status ran first).
            // Tick 2: Status detonates the banked stack; projectile expansion materializes the nova.
            TickSimulationOnly(0.01f);
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

            TickStatusPipelineOnly(0f); // Finalize accrues to threshold
            TickStatusPipelineOnly(0f); // Status reaches the detonation switch; unhandled kind enqueues nothing

            Assert.That(ImpactAoeEventQueue().Count + LingeringAoeEventQueue().Count, Is.EqualTo(0));
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
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.ImpactAoe, TemplateKey = secondAoeKey });

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
            // Ticks 2�?0: contact gate ticks down (0.1f / 0.01f = 10 ticks to expire).
            // Tick 11: gate expired, lvl-2 hits target, CombatHitEvent with stack queued.
            // Tick 12: hitApply processes stack (count=1 >= threshold=1);
            //          statusProcess fires lvl-3 detonation event;
            //          projectile expansion creates lvl-3 entities.
            const int TicksToExpireGate = 11;
            // Status now runs before Finalize, so the threshold-reaching hit is accrued one
            // tick before Status can detonate it -- one extra tick of margin over the old order.
            const int TicksAfterGate = 3;
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
            // Mirror the production sim-group order: StatusProcess runs before Finalize, so a
            // hit queued for this tick is accrued by Finalize (second) and only detonated by
            // Status on the following tick. Detonation therefore lags accrual by one tick.
            statusProcess.Update();
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
            StackEffectSnapshot stackEffect = default,
            OnHitSpawnRef onHitSpawn = default,
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
                    StackEffect = stackEffect
                },
                OnHitSpawn = onHitSpawn,
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
                    IntervalSeconds = 100f,
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
                typeof(AoeHitSpawnComponent),
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
                typeof(AoeHitSpawnComponent),
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
                DetonationKind = StackDetonationKind.ImpactAoe,
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
