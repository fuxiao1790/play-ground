using NUnit.Framework;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedResolveEditModeTests
    {
        private World _world;
        private EntityManager _entityManager;
        private SimulationSystemGroup _simulation;
        private double _elapsedTime;

        [SetUp]
        public void SetUp()
        {
            _world = new World("TargetedResolveEditModeTest");
            _entityManager = _world.EntityManager;
            _simulation = _world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            _world.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>();
            _world.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>();
            _world.GetOrCreateSystemManaged<CombatStatsGatherSystem>();
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystem<TargetSpatialHashSystem>());
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystem<CombatArmingSystem>());
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystem<TargetedResolveSystem>());
            _simulation.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated)
            {
                _world.Dispose();
            }
        }

        [Test]
        public void ShapeOverlap_SelectsTargetWhoseCentreIsOutsideChainDistance()
        {
            Entity target = AddCircleTarget(new float2(2f, 0f), 1.1f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 1f, chainCount: 1);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Source, Is.EqualTo(chain));
            Assert.That(hits[0].Target, Is.EqualTo(target));
        }

        [Test]
        public void ZeroLengthArm_ClearsAndWalksInTheSameUpdate()
        {
            // Chains spawn armed so their sprite never draws on the caster. A zero-length arm must
            // cost the walk nothing: arming clears and link 0 lands in the same update.
            Entity target = AddCircleTarget(new float2(1f, 0f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 2f, chainCount: 1, armed: true);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(_entityManager.IsComponentEnabled<ArmingTag>(chain), Is.False);
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(target));
        }

        [Test]
        public void LinkZero_SkipsNearerSameFactionProxy()
        {
            AddCircleTarget(new float2(0.5f, 0f), 0.25f, CombatFaction.Player);
            Entity hostile = AddCircleTarget(new float2(2f, 0f), 0.25f);
            AddChain(instanceIndex: 0, chainDistance: 5f, chainCount: 1);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(hostile));
        }

        [Test]
        public void ChainHop_SkipsNearerSameFactionProxy()
        {
            Entity firstHostile = AddCircleTarget(new float2(1f, 0f), 0.25f);
            AddCircleTarget(new float2(1.2f, 0f), 0.25f, CombatFaction.Player);
            Entity secondHostile = AddCircleTarget(new float2(2f, 0f), 0.25f);
            AddChain(instanceIndex: 0, chainDistance: 3f, chainCount: 2);

            Tick();

            // Hit order is not guaranteed: the lane is a parallel-writer queue.
            Assert.That(TargetsFor(DrainHits()),
                Is.EquivalentTo(new[] { firstHostile, secondHostile }));
        }

        [Test]
        public void MobChain_WalksPlayerFactionProxiesOnly()
        {
            AddCircleTarget(new float2(1f, 0f), 0.25f);
            Entity hostile = AddCircleTarget(new float2(2f, 0f), 0.25f, CombatFaction.Player);
            AddChain(instanceIndex: 0, chainDistance: 5f, chainCount: 1, faction: CombatFaction.Mob);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(hostile));
        }

        [Test]
        public void Walk_EndsWhenOnlySameFactionProxiesRemain()
        {
            Entity hostile = AddCircleTarget(new float2(1f, 0f), 0.25f);
            AddCircleTarget(new float2(1.2f, 0f), 0.25f, CombatFaction.Player);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 3f, chainCount: 4);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(hostile));

            Tick();

            Assert.That(DrainHits(), Is.Empty);
            Assert.That(_entityManager.IsComponentEnabled<Active>(chain), Is.False);
        }

        [Test]
        public void ForkRank_DedupesMultiCellTargetBeforeSelectingSecondNearest()
        {
            Entity near = AddCircleTarget(new float2(1f, 0f), 8f);
            Entity far = AddCircleTarget(new float2(12f, 0f), 0.25f);
            Entity firstFork = AddChain(instanceIndex: 0, chainDistance: 20f, chainCount: 1);
            Entity secondFork = AddChain(instanceIndex: 1, chainDistance: 20f, chainCount: 1);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(2));
            Assert.That(TargetFor(hits, firstFork), Is.EqualTo(near));
            Assert.That(TargetFor(hits, secondFork), Is.EqualTo(far));
        }

        [Test]
        public void LargeCompiledChainDistance_IsNotGloballyClamped()
        {
            Entity target = AddCircleTarget(new float2(2048f, 0f), 0.25f);
            AddChain(instanceIndex: 0, chainDistance: 4096f, chainCount: 1);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(target));
        }

        [Test]
        public void ChainDelay_LeavesSingleChainActiveUntilNextGateSlot()
        {
            AddCircleTarget(new float2(1f, 0f), 0.25f);
            AddCircleTarget(new float2(2f, 0f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 2f, chainCount: 2);
            TargetedResolveConfig config = _entityManager.GetComponentData<TargetedResolveConfig>(chain);
            config.ChainDelay = 0.1f;
            _entityManager.SetComponentData(chain, config);

            Tick();
            Assert.That(DrainHits(), Has.Length.EqualTo(1));

            Tick(0.05f);
            Assert.That(DrainHits(), Is.Empty);
            Assert.That(_entityManager.IsComponentEnabled<Active>(chain), Is.True);
        }

        [Test]
        public void Lifetime_EndsWithTheWalkNotWithATimer()
        {
            // The whole point of the collapsed shape: an instance is alive exactly as long as it
            // still has chains to use, plus the single update that renders its last link.
            AddCircleTarget(new float2(1f, 0f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 2f, chainCount: 1);
            _entityManager.SetComponentData(chain, new CombatLifetimeComponent { Remaining = 999f });

            Tick();

            // Alive for exactly one update after the last link, with the mirror on the target so
            // render prep draws the sprite there instead of on the spawn origin.
            Assert.That(_entityManager.IsComponentEnabled<Active>(chain), Is.True);
            CombatKinematicsComponent mirror =
                _entityManager.GetComponentData<CombatKinematicsComponent>(chain);
            Assert.That(mirror.Position.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(mirror.Position.y, Is.EqualTo(0f).Within(0.0001f));

            Tick();

            Assert.That(_entityManager.IsComponentEnabled<Active>(chain), Is.False);
        }

        [Test]
        public void Lifetime_EndsImmediatelyWhenALinkFindsNothing()
        {
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 2f, chainCount: 4);
            _entityManager.SetComponentData(chain, new CombatLifetimeComponent { Remaining = 999f });

            Tick();

            Assert.That(DrainHits(), Is.Empty);
            Assert.That(_entityManager.IsComponentEnabled<Active>(chain), Is.False);
        }

        [Test]
        public void Resolve_PublishesTargetedLinkCounterToStableDisplay()
        {
            AddCircleTarget(new float2(1f, 0f), 0.25f);
            AddChain(instanceIndex: 0, chainDistance: 2f, chainCount: 1);

            Tick();
            _world.GetExistingSystemManaged<CombatStatsGatherSystem>().Update();

            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatStatsSingleton>());
            CombatStatsSingleton stats = query.GetSingleton<CombatStatsSingleton>();
            CombatStatsDisplaySingleton display = _entityManager.GetComponentData<CombatStatsDisplaySingleton>(
                query.GetSingletonEntity());
            Assert.That(stats.TargetedLinksResolved, Is.EqualTo(1));
            Assert.That(display.TargetedLinksResolved, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_UpdatesRenderMirrorAndPreservesPoseWhenNoLinkLands()
        {
            AddCircleTarget(new float2(3f, 4f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 6f, chainCount: 2);
            TargetedResolveConfig config = _entityManager.GetComponentData<TargetedResolveConfig>(chain);
            config.ChainDelay = 0.1f;
            _entityManager.SetComponentData(chain, config);

            Tick();

            CombatKinematicsComponent afterLink =
                _entityManager.GetComponentData<CombatKinematicsComponent>(chain);
            Assert.That(afterLink.Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(afterLink.Position.y, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(afterLink.Velocity.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(afterLink.Velocity.y, Is.EqualTo(4f).Within(0.0001f));

            CombatRenderComponent rendered = CombatRenderMatrixUtility.ElementFor(
                afterLink,
                new CombatRenderAuthoring { BaseScale = new float2(2f, 3f), BaseCos = 1f },
                new CombatRenderComponent { RenderTypeId = 1, AlignToVelocity = 1 });
            Assert.That(rendered.Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(rendered.Position.y, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(rendered.Rotation.x, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(rendered.Rotation.y, Is.EqualTo(-2.4f).Within(0.0001f));
            Assert.That(rendered.Rotation.z, Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(rendered.Rotation.w, Is.EqualTo(1.8f).Within(0.0001f));

            CombatRenderComponent vfxOnly = CombatRenderMatrixUtility.ElementFor(
                afterLink,
                new CombatRenderAuthoring { BaseScale = new float2(2f, 3f), BaseCos = 1f },
                new CombatRenderComponent { AlignToVelocity = 1 });
            Assert.That(vfxOnly.Rotation, Is.EqualTo(default(float4)));

            Tick(0.05f);

            CombatKinematicsComponent withoutLink =
                _entityManager.GetComponentData<CombatKinematicsComponent>(chain);
            Assert.That(withoutLink.Position, Is.EqualTo(afterLink.Position));
            Assert.That(withoutLink.Velocity, Is.EqualTo(afterLink.Velocity));
        }

        [Test]
        public void Resolve_EmitsLineSegmentForEachLandedLink()
        {
            AddCircleTarget(new float2(1f, 0f), 0.25f);
            AddCircleTarget(new float2(1f, 2f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 3f, chainCount: 2);
            TargetedChainComponent seeded = _entityManager.GetComponentData<TargetedChainComponent>(chain);
            seeded.LinkTarget = new float2(9f, 9f);
            _entityManager.SetComponentData(chain, seeded);
            SetLinkVfx(chain, VfxDataShapeTable.EncodeId(VfxDataShape.LineSegment, 1), 0.75f);

            Tick();

            LineSegmentVfxSpawn[] segments = DrainLineSegments();
            Assert.That(segments, Has.Length.EqualTo(2));
            Assert.That(ContainsSegment(segments, float2.zero, new float2(1f, 0f), 0.75f), Is.True);
            Assert.That(ContainsSegment(segments, new float2(1f, 0f), new float2(1f, 2f), 0.75f), Is.True);
        }

        [Test]
        public void AcquisitionAndResolve_UseSameNearestHostileRule()
        {
            AddCircleTarget(new float2(0.5f, 0f), 0.25f, CombatFaction.Player);
            Entity nearestHostile = AddCircleTarget(new float2(1f, 0f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 3f, chainCount: 1);

            Tick();

            CombatHitEvent[] hits = DrainHits();
            Assert.That(hits, Has.Length.EqualTo(1));
            Assert.That(hits[0].Target, Is.EqualTo(nearestHostile));

            TargetSpatialHashSingleton hash = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetSpatialHashSingleton>()).GetSingleton<TargetSpatialHashSingleton>();
            hash.BuildHandle.Complete();
            TargetedAcquisition.Snapshot snapshot = new(
                hash.TargetEntities.AsArray(),
                hash.TargetPositions.AsArray(),
                hash.TargetShapes.AsArray(),
                hash.TargetFactions.AsArray(),
                hash.AoeOccupiedCells);
            Assert.That(
                TargetedAcquisition.TryNearestHostile(
                    snapshot,
                    float2.zero,
                    3f,
                    CombatFaction.Player,
                    out Entity acquired,
                    out _),
                Is.True);
            Assert.That(acquired, Is.EqualTo(hits[0].Target));
            Assert.That(hits[0].Source, Is.EqualTo(chain));
        }

        [Test]
        public void Resolve_DropsLinkVfxIdWithNonLineSegmentShape()
        {
            AddCircleTarget(new float2(1f, 0f), 0.25f);
            Entity chain = AddChain(instanceIndex: 0, chainDistance: 2f, chainCount: 1);
            SetLinkVfx(chain, VfxDataShapeTable.EncodeId(VfxDataShape.Circular, 1), 1f);

            Tick();

            Assert.That(DrainLineSegments(), Is.Empty);
        }

        private Entity AddCircleTarget(
            float2 position,
            float radius,
            CombatFaction faction = CombatFaction.Mob)
        {
            CombatCollisionMath.ComputeWorldBounds(
                position,
                radius,
                float2.zero,
                0f,
                CombatShapeType.Circle,
                out float2 boundsMin,
                out float2 boundsMax);
            Entity entity = _entityManager.CreateEntity(
                typeof(TargetProxyTag),
                typeof(TargetPosition),
                typeof(TargetCollisionShape),
                typeof(TargetFaction));
            _entityManager.SetComponentData(entity, new TargetPosition { Value = position });
            _entityManager.SetComponentData(entity, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            });
            _entityManager.SetComponentData(entity, new TargetFaction { Value = faction });
            return entity;
        }

        private Entity AddChain(
            int instanceIndex,
            float chainDistance,
            int chainCount,
            CombatFaction faction = CombatFaction.Player,
            bool armed = false)
        {
            Entity entity = _entityManager.CreateEntity(
                typeof(TargetedTag),
                typeof(TargetedIdentityComponent),
                typeof(TargetedChainComponent),
                typeof(TargetedResolveConfig),
                typeof(CombatHitPayload),
                typeof(TargetedVfxIds),
                typeof(TargetedVfxSizeComponent),
                typeof(VfxTimingData),
                typeof(CombatKinematicsComponent),
                typeof(CombatLifetimeComponent),
                typeof(CombatArmingComponent),
                typeof(Active),
                typeof(ArmingTag));
            _entityManager.SetComponentData(entity, new TargetedIdentityComponent
            {
                Faction = faction,
                InstanceIndex = instanceIndex
            });
            _entityManager.SetComponentData(entity, new TargetedResolveConfig
            {
                ChainDistance = chainDistance,
                ChainDamageFalloff = 1f,
                ChainCount = chainCount
            });
            _entityManager.SetComponentEnabled<ArmingTag>(entity, armed);
            return entity;
        }

        private void Tick(float deltaTime = 0.01f)
        {
            _elapsedTime += deltaTime;
            _world.SetTime(new TimeData(_elapsedTime, deltaTime));
            _simulation.Update();
        }

        private CombatHitEvent[] DrainHits()
        {
            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatHitDispatchSingleton>());
            CombatHitDispatchSingleton lane = query.GetSingleton<CombatHitDispatchSingleton>();
            lane.ProducerHandle.Complete();
            using NativeArray<CombatHitEvent> hits = lane.HitQueue.ToArray(Allocator.Temp);
            lane.HitQueue.Clear();
            return hits.ToArray();
        }

        private void SetLinkVfx(Entity chain, int vfxId, float width)
        {
            TargetedVfxIds ids = _entityManager.GetComponentData<TargetedVfxIds>(chain);
            ids.LinkId = vfxId;
            _entityManager.SetComponentData(chain, ids);
            TargetedVfxSizeComponent size = _entityManager.GetComponentData<TargetedVfxSizeComponent>(chain);
            size.LinkWidth = width;
            _entityManager.SetComponentData(chain, size);
        }

        private LineSegmentVfxSpawn[] DrainLineSegments()
        {
            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatAoeVfxDispatchSingleton>());
            CombatAoeVfxDispatchSingleton lane = query.GetSingleton<CombatAoeVfxDispatchSingleton>();
            lane.ProducerHandle.Complete();
            using NativeArray<LineSegmentVfxSpawn> segments =
                lane.PendingLineSegmentSpawns.ToArray(Allocator.Temp);
            lane.PendingLineSegmentSpawns.Clear();
            return segments.ToArray();
        }

        private static bool ContainsSegment(
            LineSegmentVfxSpawn[] segments,
            float2 startPosition,
            float2 endPosition,
            float width)
        {
            for (int i = 0; i < segments.Length; i++)
            {
                LineSegmentVfxSpawn segment = segments[i];
                if (segment.StartPosition.Equals(startPosition)
                    && segment.EndPosition.Equals(endPosition)
                    && segment.Width == width)
                {
                    return true;
                }
            }

            return false;
        }

        private static Entity[] TargetsFor(CombatHitEvent[] hits)
        {
            Entity[] targets = new Entity[hits.Length];
            for (int i = 0; i < hits.Length; i++)
            {
                targets[i] = hits[i].Target;
            }

            return targets;
        }

        private static Entity TargetFor(CombatHitEvent[] hits, Entity source)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].Source == source)
                {
                    return hits[i].Target;
                }
            }

            return Entity.Null;
        }
    }
}
