using NUnit.Framework;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedLifetimeAndCleanupEditModeTests
    {
        private World _world;
        private EntityManager _entityManager;
        private SimulationSystemGroup _simulation;
        private double _elapsedTime;

        [SetUp]
        public void SetUp()
        {
            _world = new World("TargetedLifetimeAndCleanupEditModeTest");
            _entityManager = _world.EntityManager;
            _simulation = _world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            _world.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>();
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystem<CombatArmingSystem>());
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystem<CombatLifetimeSystem>());
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
        public void Lifetime_PausesWhileTargetedEntityIsArming()
        {
            Entity entity = CreateTargeted(lifetime: 0.5f, armSeconds: 1f);

            Tick(0.25f);

            Assert.That(_entityManager.GetComponentData<CombatLifetimeComponent>(entity).Remaining, Is.EqualTo(0.5f));
            Assert.That(_entityManager.IsComponentEnabled<Active>(entity), Is.True);
            Assert.That(_entityManager.IsComponentEnabled<ArmingTag>(entity), Is.True);
        }

        [Test]
        public void Lifetime_ExpiresTargetedEntityAndUsesDedicatedVisualSize()
        {
            Entity entity = CreateTargeted(lifetime: 0.1f, armSeconds: 0f);
            _entityManager.SetComponentData(entity, new CombatKinematicsComponent
            {
                Position = new float2(3f, -2f)
            });
            _entityManager.SetComponentData(entity, new TargetedVfxIds
            {
                ExpireId = VfxDataShapeTable.EncodeId(VfxDataShape.Circular, 1)
            });
            _entityManager.SetComponentData(entity, new TargetedVfxSizeComponent { EffectSize = 2.5f });

            Tick(0.1f);

            Assert.That(_entityManager.IsComponentEnabled<Active>(entity), Is.False);
            using EntityQuery vfxQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatAoeVfxDispatchSingleton>());
            CombatAoeVfxDispatchSingleton vfx = vfxQuery.GetSingleton<CombatAoeVfxDispatchSingleton>();
            vfx.ProducerHandle.Complete();
            Assert.That(vfx.PendingCircularSpawns.TryDequeue(out CircularVfxSpawnRequest request), Is.True);
            Assert.That(request.Position, Is.EqualTo(new float2(3f, -2f)));
            Assert.That(request.AreaSize, Is.EqualTo(2.5f));
        }

        [Test]
        public void Cleanup_TrimsTheTargetedPool()
        {
            Entity first = CreateTargeted(lifetime: 1f, armSeconds: 0f);
            Entity second = CreateTargeted(lifetime: 1f, armSeconds: 0f);
            _entityManager.SetComponentEnabled<Active>(first, false);
            _entityManager.SetComponentEnabled<Active>(second, false);

            CombatPoolCleanupSystem cleanup = _world.GetOrCreateSystemManaged<CombatPoolCleanupSystem>();
            Entity config = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatPoolCleanupConfig>()).GetSingletonEntity();
            _entityManager.SetComponentData(config, new CombatPoolCleanupConfig
            {
                ChunkActiveThresholdPercent = 100f,
                DespawnOverSpawnMargin = 1f,
                RateSmoothingTime = 0.5f
            });

            cleanup.Update();

            Assert.That(_entityManager.Exists(first), Is.False);
            Assert.That(_entityManager.Exists(second), Is.False);
        }

        private Entity CreateTargeted(float lifetime, float armSeconds)
        {
            Entity entity = _entityManager.CreateEntity(
                typeof(TargetedTag), typeof(Active), typeof(ArmingTag), typeof(CombatArmingComponent),
                typeof(CombatLifetimeComponent), typeof(TargetedVfxIds),
                typeof(TargetedVfxSizeComponent), typeof(VfxTimingData), typeof(CombatKinematicsComponent));
            _entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = lifetime });
            _entityManager.SetComponentData(entity, new CombatArmingComponent { Remaining = armSeconds });
            _entityManager.SetComponentEnabled<Active>(entity, true);
            _entityManager.SetComponentEnabled<ArmingTag>(entity, armSeconds > 0f);
            return entity;
        }

        private void Tick(float deltaTime)
        {
            _elapsedTime += deltaTime;
            _world.SetTime(new TimeData(_elapsedTime, deltaTime));
            _simulation.Update();
        }
    }
}
