using NUnit.Framework;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Targets;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedRoutingEditModeTests
    {
        private World _world;
        private EntityManager _entityManager;
        private SimulationSystemGroup _simulation;
        private ExternalSpawnGateSystem _gate;
        private Entity _scope;

        [SetUp]
        public void SetUp()
        {
            _world = new World("TargetedRoutingEditModeTest");
            _entityManager = _world.EntityManager;
            _simulation = _world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            _gate = _world.GetOrCreateSystemManaged<ExternalSpawnGateSystem>();
            // TimedSpawnSystem reads every interval lane unconditionally. Create the
            // projectile/AOE lane owners even though this fixture only updates TimedSpawnSystem
            // and inspects the targeted queues directly.
            _world.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            _world.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>();
            _world.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>();
            _world.GetOrCreateSystemManaged<TargetedSpawnExpansionSystem>();
            _world.GetOrCreateSystemManaged<LingeringTargetedSpawnExpansionSystem>();
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystem<TimedSpawnSystem>());
            _simulation.SortSystems();

            _scope = _entityManager.CreateEntity(typeof(CombatScope));
            _entityManager.AddBuffer<ExternalSpawnRequest>(_scope);
            _entityManager.AddBuffer<ProjectileSpawnEvent>(_scope);
            _entityManager.AddBuffer<ImpactAoeSpawnEvent>(_scope);
            _entityManager.AddBuffer<LingeringAoeSpawnEvent>(_scope);
            _entityManager.AddBuffer<TargetedSpawnEvent>(_scope);
            _entityManager.AddBuffer<LingeringTargetedSpawnEvent>(_scope);
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
        public void TargetedTypeRegistry_AllowsVfxOnlyDefinitionsAndStoresVfxIds()
        {
            var registry = new TargetedTypeRegistry();
            var definition = new TargetedTypeDefinition();
            registry.Register(4, definition);

            Assert.That(registry.TryGetDefinition(4, out TargetedTypeDefinition stored), Is.True);
            Assert.That(stored, Is.SameAs(definition));
            Assert.That(registry.TryGetVisual(4, out _), Is.False);

            var vfxIds = new TargetedVfxIds { SpawnId = 2, LinkId = 5 };
            registry.SetVfxIds(4, vfxIds);
            Assert.That(definition.VfxIds.SpawnId, Is.EqualTo(2));
            Assert.That(definition.VfxIds.LinkId, Is.EqualTo(5));
        }

        [Test]
        public void ExternalGate_DeductsManaAndPreservesTargetedOriginAndAnchor()
        {
            Entity caster = _entityManager.CreateEntity(typeof(Mana));
            _entityManager.SetComponentData(caster, new Mana { Current = 5f, Max = 5f });
            _entityManager.GetBuffer<ExternalSpawnRequest>(_scope).Add(new ExternalSpawnRequest
            {
                Kind = IntervalChildKind.Targeted,
                TemplateKey = new Hash128(1u, 2u, 3u, 4u),
                Caster = caster,
                ManaCost = 2f,
                Position = new float2(2f, 3f),
                AcquireAnchor = new float2(7f, 11f),
                Faction = CombatFaction.Player,
                SourceId = 31,
                JitterSeed = 9u
            });

            _gate.Update();

            Assert.That(_entityManager.GetComponentData<Mana>(caster).Current, Is.EqualTo(3f));
            DynamicBuffer<TargetedSpawnEvent> events = _entityManager.GetBuffer<TargetedSpawnEvent>(_scope);
            Assert.That(events.Length, Is.EqualTo(1));
            Assert.That(events[0].Position, Is.EqualTo(new float2(2f, 3f)));
            Assert.That(events[0].AcquireAnchor, Is.EqualTo(new float2(7f, 11f)));
        }

        [Test]
        public void ExternalGate_DefaultAnchorFallsBackToPositionAndRejectsInsufficientMana()
        {
            Entity caster = _entityManager.CreateEntity(typeof(Mana));
            _entityManager.SetComponentData(caster, new Mana { Current = 1f, Max = 1f });
            _entityManager.GetBuffer<ExternalSpawnRequest>(_scope).Add(new ExternalSpawnRequest
            {
                Kind = IntervalChildKind.LingeringTargeted,
                TemplateKey = new Hash128(2u, 3u, 4u, 5u),
                Position = new float2(-3f, 8f),
                Faction = CombatFaction.Player,
                SourceId = 32
            });
            _entityManager.GetBuffer<ExternalSpawnRequest>(_scope).Add(new ExternalSpawnRequest
            {
                Kind = IntervalChildKind.Targeted,
                TemplateKey = new Hash128(3u, 4u, 5u, 6u),
                Caster = caster,
                ManaCost = 2f,
                Position = new float2(1f, 1f),
                Faction = CombatFaction.Player,
                CastToken = 77
            });

            _gate.Update();

            DynamicBuffer<LingeringTargetedSpawnEvent> events =
                _entityManager.GetBuffer<LingeringTargetedSpawnEvent>(_scope);
            Assert.That(events.Length, Is.EqualTo(1));
            Assert.That(events[0].AcquireAnchor, Is.EqualTo(events[0].Position));
            Assert.That(_entityManager.GetBuffer<TargetedSpawnEvent>(_scope).Length, Is.Zero);
            SpawnRejectedSingleton rejected = RejectionLane();
            Assert.That(rejected.Events.Length, Is.EqualTo(1));
            Assert.That(rejected.Events[0].CastToken, Is.EqualTo(77));
        }

        [TestCase(IntervalChildKind.Targeted)]
        [TestCase(IntervalChildKind.LingeringTargeted)]
        public void TimedSpawn_RoutesTargetedVariantsWithSourceAsOriginAndAnchor(IntervalChildKind kind)
        {
            Entity source = _entityManager.CreateEntity(
                typeof(Active),
                typeof(ArmingTag),
                typeof(CombatLifetimeComponent),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent),
                typeof(CombatKinematicsComponent));
            _entityManager.SetComponentEnabled<ArmingTag>(source, false);
            _entityManager.SetComponentData(source, new CombatLifetimeComponent { Remaining = 2f });
            _entityManager.SetComponentData(source, new CombatKinematicsComponent
            {
                Position = new float2(4f, -2f)
            });
            _entityManager.SetComponentData(source, new TimedSpawnComponent
            {
                ChildKind = kind,
                TemplateKey = new Hash128(5u, 6u, 7u, 8u),
                Faction = CombatFaction.Player,
                SourceId = 99,
                JitterSeed = 17,
                EnergyPerSecond = 1f,
                EnergyThreshold = 0.1f
            });

            _world.SetTime(new TimeData(0.1d, 0.1f));
            _simulation.Update();

            if (kind == IntervalChildKind.Targeted)
            {
                TargetedSpawnEventSingleton lane = _entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<TargetedSpawnEventSingleton>()).GetSingleton<TargetedSpawnEventSingleton>();
                lane.ProducerHandle.Complete();
                Assert.That(lane.EventQueue.TryDequeue(out TargetedSpawnEvent spawned), Is.True);
                Assert.That(spawned.Position, Is.EqualTo(new float2(4f, -2f)));
                Assert.That(spawned.AcquireAnchor, Is.EqualTo(spawned.Position));
            }
            else
            {
                LingeringTargetedSpawnEventSingleton lane = _entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<LingeringTargetedSpawnEventSingleton>()).GetSingleton<LingeringTargetedSpawnEventSingleton>();
                lane.ProducerHandle.Complete();
                Assert.That(lane.EventQueue.TryDequeue(out LingeringTargetedSpawnEvent spawned), Is.True);
                Assert.That(spawned.Position, Is.EqualTo(new float2(4f, -2f)));
                Assert.That(spawned.AcquireAnchor, Is.EqualTo(spawned.Position));
            }
        }

        private SpawnRejectedSingleton RejectionLane() => _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<SpawnRejectedSingleton>()).GetSingleton<SpawnRejectedSingleton>();
    }
}
