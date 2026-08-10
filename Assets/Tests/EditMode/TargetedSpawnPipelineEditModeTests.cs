using NUnit.Framework;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedSpawnPipelineEditModeTests
    {
        private World _world;
        private EntityManager _entityManager;
        private SimulationSystemGroup _simulation;
        private Entity _scope;
        private NativeHashMap<Hash128, TargetedSpawnCommand> _templates;
        private SpawnTemplateRegistryState _templateRegistryState;

        [SetUp]
        public void SetUp()
        {
            _world = new World("TargetedSpawnPipelineEditModeTest");
            _entityManager = _world.EntityManager;
            _simulation = _world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            _world.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>();
            _world.GetOrCreateSystemManaged<CombatStatsGatherSystem>();
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<TargetedSpawnExpansionSystem>());
            _simulation.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<TargetedSpawnApplySystem>());
            _simulation.SortSystems();

            _scope = _entityManager.CreateEntity(typeof(CombatScope));
            _templateRegistryState = CreateTemplateRegistryState();
            _entityManager.AddBuffer<TargetedSpawnEvent>(_scope);
            _templates = new NativeHashMap<Hash128, TargetedSpawnCommand>(16, Allocator.Persistent);
            _entityManager.AddComponentData(_scope, new TargetedSpawnTemplate { Map = _templates });
        }

        [TearDown]
        public void TearDown()
        {
            DisposeTemplateRegistryState();
            if (_templates.IsCreated)
            {
                _templates.Dispose();
            }

            if (_world.IsCreated)
            {
                _world.Dispose();
            }
        }

        [Test]
        public void Events_MaterializeIntoOneTargetedArchetype()
        {
            Enqueue(MakeEvent(RegisterTemplate(), IntervalChildKind.Targeted));
            Enqueue(MakeEvent(RegisterTemplate(count: 2), IntervalChildKind.Targeted));

            Tick();

            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.ReadOnly<Active>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(3));
        }

        [Test]
        public void FanOut_UsesSameOriginAndFreshChainStateWithSequentialIds()
        {
            float2 origin = new(4f, -2f);
            float2 anchor = new(9f, 3f);
            Hash128 key = RegisterTemplate(count: 3);
            Enqueue(MakeEvent(key, IntervalChildKind.Targeted, origin, anchor, sourceId: 77));

            Tick();

            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.EqualTo(3));
            var seen = new bool[3];
            foreach (Entity entity in entities)
            {
                TargetedIdentityComponent identity = _entityManager.GetComponentData<TargetedIdentityComponent>(entity);
                CombatKinematicsComponent kinematics = _entityManager.GetComponentData<CombatKinematicsComponent>(entity);
                TargetedChainComponent chain = _entityManager.GetComponentData<TargetedChainComponent>(entity);
                Assert.That(identity.TargetedId, Is.InRange(77, 79));
                seen[identity.TargetedId - 77] = true;
                Assert.That(identity.InstanceIndex, Is.InRange(0, 2));
                Assert.That(kinematics.Position, Is.EqualTo(anchor));
                Assert.That(chain.Origin, Is.EqualTo(origin));
                Assert.That(chain.LinkSource, Is.EqualTo(origin));
                Assert.That(chain.LinkTarget, Is.EqualTo(anchor));
                Assert.That(chain.AcquireAnchor, Is.EqualTo(anchor));
                Assert.That(chain.LastTargetKey, Is.Zero);
                Assert.That(chain.LinkIndex, Is.Zero);
            }

            Assert.That(seen, Is.All.True);
        }

        [Test]
        public void DeterministicFanOut_IsStableAndSeparatesTickIndices()
        {
            Hash128 key = RegisterTemplate(count: 3);
            Enqueue(MakeEvent(key, IntervalChildKind.Targeted, sourceId: 101, jitterSeed: 21u, tickIndex: 2));
            Enqueue(MakeEvent(key, IntervalChildKind.Targeted, sourceId: 101, jitterSeed: 21u, tickIndex: 3));

            Tick();

            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            using var ids = new NativeHashSet<int>(entities.Length, Allocator.Temp);
            foreach (Entity entity in entities)
            {
                ids.Add(_entityManager.GetComponentData<TargetedIdentityComponent>(entity).TargetedId);
            }

            Assert.That(ids.Count, Is.EqualTo(6));
            for (int index = 0; index < 3; index++)
            {
                Assert.That(ids.Contains(ExpectedId(101, 21u, 2, index)), Is.True);
                Assert.That(ids.Contains(ExpectedId(101, 21u, 3, index)), Is.True);
            }

        }

        [Test]
        public void InvalidKindAndMissingTemplate_ProduceNoCommandsOrEntities()
        {
            Enqueue(MakeEvent(default, IntervalChildKind.Targeted));
            Enqueue(MakeEvent(RegisterTemplate(), IntervalChildKind.LingeringAoe));

            Tick();

            using EntityQuery query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<TargetedTag>());
            Assert.That(query.CalculateEntityCount(), Is.Zero);
        }

        [Test]
        public void SpawnVfx_UsesTargetedVisualSizeAndDoesNotEmitZeroIds()
        {
            float2 origin = new(-1f, 8f);
            Hash128 key = RegisterTemplate(
                count: 3,
                vfxId: VfxDataShapeTable.EncodeId(VfxDataShape.Circular, 1),
                effectSize: 2.5f);
            Enqueue(MakeEvent(key, IntervalChildKind.Targeted, origin));

            Tick();

            CombatAoeVfxDispatchSingleton vfx = VfxLane();
            vfx.ProducerHandle.Complete();
            Assert.That(vfx.PendingCircularSpawns.Count, Is.EqualTo(3));
            while (vfx.PendingCircularSpawns.TryDequeue(out CircularVfxSpawnRequest request))
            {
                Assert.That(request.Position, Is.EqualTo(origin));
                Assert.That(request.AreaSize, Is.EqualTo(2.5f));
            }

            Enqueue(MakeEvent(RegisterTemplate(), IntervalChildKind.Targeted));
            Tick();
            vfx = VfxLane();
            vfx.ProducerHandle.Complete();
            Assert.That(vfx.PendingCircularSpawns.Count, Is.Zero);

            int spawnId = VfxDataShapeTable.EncodeId(VfxDataShape.Circular, 2);
            int armingId = VfxDataShapeTable.EncodeId(VfxDataShape.Circular, 3);
            Enqueue(MakeEvent(
                RegisterTemplate(
                    vfxId: spawnId,
                    armingId: armingId,
                    armSeconds: 0.25f),
                IntervalChildKind.Targeted));
            Tick();
            vfx = VfxLane();
            vfx.ProducerHandle.Complete();
            Assert.That(vfx.PendingCircularSpawns.Count, Is.EqualTo(1));
            Assert.That(vfx.PendingCircularSpawns.TryDequeue(out CircularVfxSpawnRequest arming), Is.True);
            Assert.That(arming.VfxId, Is.EqualTo(armingId));
        }

        [Test]
        public void Reuse_TakesTheDisabledSlotBackInsteadOfCreatingANewOne()
        {
            Enqueue(MakeEvent(RegisterTemplate(), IntervalChildKind.Targeted));
            Tick();
            Entity slot = OnlyActiveTargeted();
            _entityManager.SetComponentEnabled<Active>(slot, false);

            Enqueue(MakeEvent(RegisterTemplate(), IntervalChildKind.Targeted));
            Tick();

            Assert.That(OnlyActiveTargeted(), Is.EqualTo(slot));
        }

        [Test]
        public void SpawnFrame_AcquiredChainStartsOnAnchorAndIsUnarmed()
        {
            float2 origin = new(4f, -2f);
            float2 anchor = new(9f, 3f);
            Enqueue(MakeEvent(
                RegisterTemplate(),
                IntervalChildKind.Targeted,
                origin,
                anchor,
                hasAcquiredTarget: 1));

            Tick();

            Entity chain = OnlyActiveTargeted();
            Assert.That(_entityManager.IsComponentEnabled<ArmingTag>(chain), Is.False);
            Assert.That(_entityManager.GetComponentData<CombatKinematicsComponent>(chain).Position, Is.EqualTo(anchor));
            TargetedChainComponent state = _entityManager.GetComponentData<TargetedChainComponent>(chain);
            Assert.That(state.LinkTarget, Is.EqualTo(anchor));
            Assert.That(state.LinkSource, Is.EqualTo(origin));
        }

        [Test]
        public void SpawnFrame_UnacquiredChainStartsOnRawAnchorAndStaysArmed()
        {
            float2 origin = new(4f, -2f);
            float2 anchor = new(9f, 3f);
            Enqueue(MakeEvent(
                RegisterTemplate(),
                IntervalChildKind.Targeted,
                origin,
                anchor,
                hasAcquiredTarget: 0));

            Tick();

            Entity chain = OnlyActiveTargeted();
            Assert.That(_entityManager.IsComponentEnabled<ArmingTag>(chain), Is.True);
            Assert.That(_entityManager.GetComponentData<CombatKinematicsComponent>(chain).Position, Is.EqualTo(anchor));
            Assert.That(
                _entityManager.GetComponentData<CombatArmingComponent>(chain).Remaining,
                Is.Zero);
        }

        [Test]
        public void TargetedSpawnsContributeStatsAndDisplaySnapshot()
        {
            Enqueue(MakeEvent(RegisterTemplate(count: 2), IntervalChildKind.Targeted));

            Tick();

            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatStatsSingleton>());
            CombatStatsSingleton stats = query.GetSingleton<CombatStatsSingleton>();
            Assert.That(stats.EntitiesSpawnedViaEcb + stats.EntitiesSpawnedViaReuse, Is.EqualTo(2));
            Assert.That(stats.TargetedEntitiesSpawned, Is.EqualTo(2));

            _world.GetExistingSystemManaged<CombatStatsGatherSystem>().Update();
            CombatStatsDisplaySingleton display = _entityManager.GetComponentData<CombatStatsDisplaySingleton>(
                query.GetSingletonEntity());
            Assert.That(display.TargetedEntitiesSpawned, Is.EqualTo(2));
            Assert.That(display.ActiveTargeted, Is.EqualTo(2));
        }

        private SpawnTemplateRegistryState CreateTemplateRegistryState()
        {
            SpawnTemplateRegistryState state = new()
            {
                ProjectileCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(16, Allocator.Persistent),
                AoeCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(16, Allocator.Persistent),
                TargetedCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(16, Allocator.Persistent),
                Deltas = new NativeQueue<SpawnTemplateRefDelta>(Allocator.Persistent)
            };
            _entityManager.AddComponentData(_scope, state);
            return state;
        }

        private void DisposeTemplateRegistryState()
        {
            if (_templateRegistryState.ProjectileCounts.IsCreated) _templateRegistryState.ProjectileCounts.Dispose();
            if (_templateRegistryState.AoeCounts.IsCreated) _templateRegistryState.AoeCounts.Dispose();
            if (_templateRegistryState.TargetedCounts.IsCreated) _templateRegistryState.TargetedCounts.Dispose();
            if (_templateRegistryState.Deltas.IsCreated) _templateRegistryState.Deltas.Dispose();
            _templateRegistryState = default;
        }

        private void Tick() => _simulation.Update();

        private Hash128 RegisterTemplate(
            int count = 1,
            int vfxId = 0,
            float effectSize = 1f,
            int armingId = 0,
            float armSeconds = 0f)
        {
            TargetedSpawnCommand template = new()
            {
                EchoCount = count,
                LifetimeSeconds = 1f,
                ArmSeconds = armSeconds,
                VfxIds = new TargetedVfxIds { SpawnId = vfxId, ArmingId = armingId },
                VfxSize = new TargetedVfxSizeComponent { EffectSize = effectSize, LinkWidth = 1f }
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            _templates.TryAdd(key, template);
            return key;
        }

        private void Enqueue(TargetedSpawnEvent evt) =>
            _entityManager.GetBuffer<TargetedSpawnEvent>(_scope).Add(evt);

        private static TargetedSpawnEvent MakeEvent(
            Hash128 key,
            IntervalChildKind kind,
            float2 position = default,
            float2 acquireAnchor = default,
            byte hasAcquiredTarget = 0,
            int sourceId = 1,
            uint jitterSeed = 0u,
            int tickIndex = 0) =>
            new()
            {
                Kind = kind,
                TemplateKey = key,
                Position = position,
                AcquireAnchor = acquireAnchor,
                HasAcquiredTarget = hasAcquiredTarget,
                Faction = CombatFaction.Player,
                SourceId = sourceId,
                JitterSeed = jitterSeed,
                DeterministicIdTickIndex = tickIndex
            };

        private Entity OnlyActiveTargeted()
        {
            using EntityQuery query = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.EqualTo(1));
            return entities[0];
        }

        private CombatAoeVfxDispatchSingleton VfxLane()
        {
            using EntityQuery query =
                _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatAoeVfxDispatchSingleton>());
            return query.GetSingleton<CombatAoeVfxDispatchSingleton>();
        }

        private static int ExpectedId(int sourceId, uint jitterSeed, int tickIndex, int index)
        {
            unchecked
            {
                int hash = sourceId;
                hash = (hash * 397) ^ (int)jitterSeed;
                hash = (hash * 397) ^ tickIndex;
                hash = (hash * 397) ^ index;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }
    }
}
