using NUnit.Framework;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.PlayMode
{
    public sealed class AoeSimulationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private double elapsedTime;
        private int nextAoeId;
        private int nextTargetId = 5000;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("AoeSimulationTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeSimulationSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<AoeSpawnSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeLifetimeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeCollisionSystem>());
            simGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(AoeScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<AoeSpawnRequestElement>(scopeEntity);
            entityManager.AddBuffer<AoeHitElement>(scopeEntity);
            entityManager.AddBuffer<AoeRecycleElement>(scopeEntity);
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
            SpawnCircle(float2.zero, 1f, 1, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(1));

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void LingeringHitsImmediatelyThenRepeatsAfterCooldown()
        {
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 1, 2f, lifetime: 10f, tickInterval: 0.02f);

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
            SpawnCircle(float2.zero, 1f, 1, 2f, lifetime: 10f, tickInterval: 100f);

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
            SpawnCircle(float2.zero, 1f, 1, 2f, lifetime: 0.001f, tickInterval: 1f);

            Tick(0.01f);
            Tick(0.01f);

            Assert.That(ActiveAoeCount(), Is.EqualTo(0));
            Assert.That(TotalAoeCount(), Is.EqualTo(1));
        }

        [Test]
        public void PulseDoesNotHitTargetOutsideRadius()
        {
            AddTarget(new float2(3f, 0f), 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 1, 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void TargetMaskFiltersHits()
        {
            AddTarget(float2.zero, 0.25f, targetMask: 2);
            SpawnCircle(float2.zero, 1f, targetMask: 4, damage: 2f);

            Tick(0.01f);
            Assert.That(ReadHitCount(), Is.EqualTo(0));
        }

        [Test]
        public void PulseEntityIsReusedOnRespawn()
        {
            AddTarget(float2.zero, 0.25f, 1);
            SpawnCircle(float2.zero, 1f, 1, 1f);
            Tick(0.01f);
            Entity first = FirstAoeEntity();

            SpawnCircle(float2.zero, 1f, 1, 1f);
            Tick(0.01f);
            Entity reused = FirstAoeEntity();

            Assert.That(reused, Is.EqualTo(first));
            Assert.That(TotalAoeCount(), Is.EqualTo(1));
        }

        [Test]
        public void CommonCombatEntityWithoutAoeTagIsIgnored()
        {
            Entity alien = entityManager.CreateEntity(
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatHitComponent),
                typeof(AoeActiveTag));
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

        private void Tick(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private void SpawnCircle(float2 position, float radius, int targetMask, float damage,
            float lifetime = 0f, float tickInterval = 0f)
        {
            float2 min = position - radius;
            float2 max = position + radius;
            entityManager.GetBuffer<AoeSpawnRequestElement>(scopeEntity).Add(new AoeSpawnRequestElement
            {
                AoeId = ++nextAoeId,
                TypeId = 1,
                TargetMask = targetMask,
                Lifetime = lifetime,
                RepeatHitCooldownSeconds = tickInterval,
                DamageAmount = damage,
                Radius = radius,
                Position = position,
                HalfExtents = float2.zero,
                BoundsMin = min,
                BoundsMax = max,
                ShapeType = CombatShapeType.Circle
            });
        }

        private void AddTarget(float2 position, float radius, int targetMask)
        {
            AddTargetById(position, radius, targetMask, ++nextTargetId);
        }

        private void AddTargetById(float2 position, float radius, int targetMask, int targetId)
        {
            float2 min = position - radius;
            float2 max = position + radius;
            entityManager.GetBuffer<CombatTargetElement>(scopeEntity).Add(new CombatTargetElement
            {
                TargetId = targetId,
                TargetMask = targetMask,
                Position = position,
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                HalfExtents = float2.zero,
                BoundsMin = min,
                BoundsMax = max
            });
        }

        private void ReplaceTarget(int targetId, float2 position, float radius, int targetMask)
        {
            entityManager.GetBuffer<CombatTargetElement>(scopeEntity).Clear();
            AddTargetById(position, radius, targetMask, targetId);
        }

        private int ReadHitCount() => entityManager.GetBuffer<AoeHitElement>(scopeEntity).Length;

        private int ActiveAoeCount()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeActiveTag>());
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
    }
}
