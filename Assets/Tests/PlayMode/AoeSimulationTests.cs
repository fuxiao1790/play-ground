using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
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
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<AoeSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<AoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatLifetimeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoePulseVfxSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoeCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<DamageFinalizeSystem>());
            simGroup.SortSystems();

            presentationGroup = testWorld.GetOrCreateSystemManaged<PresentationSystemGroup>();
            presentationGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<DamageDispatchBridge>());
            presentationGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<AoeSpawnEvent>(scopeEntity);
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

            DamageReplayEvent[] finalized = ReadFinalizedDamageEvents();
            Assert.That(finalized, Has.Length.EqualTo(1));
            Assert.That(finalized[0].TargetProxy, Is.EqualTo(proxy));

            presentationGroup.Update();
            Assert.That(TotalHitCount(), Is.EqualTo(1));
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

        private void Tick(float dt)
        {
            int hitsBefore = TotalHitCount();
            TickSimulationOnly(dt);
            presentationGroup.Update();
            lastHitCount = TotalHitCount() - hitsBefore;
        }

        private void TickSimulationOnly(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private void SpawnCircle(float2 position, float radius, float damage,
            float lifetime = 0f, float tickInterval = 0f)
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
                    DirectDamageEnabled = damage > 0f
                },
                Radius = radius,
                Position = position,
                HalfExtents = float2.zero,
                ShapeType = CombatShapeType.Circle
            });
        }

        private void AddTarget(float2 position, float radius, int targetMask)
        {
            AddTargetById(position, radius, targetMask, ++nextTargetId);
        }

        private void AddTargetById(float2 position, float radius, int targetMask, int targetId)
        {
            if (!targetsById.TryGetValue(targetId, out TestCombatTarget target))
            {
                target = new TestCombatTarget(targetId);
                targetsById.Add(targetId, target);
            }

            target.Position = position;
            target.Radius = radius;
            target.Mask = targetMask;
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

        private Entity FirstTargetProxy()
        {
            foreach (TestCombatTarget target in targetsById.Values)
            {
                return target.Proxy;
            }

            Assert.Fail("No target proxy found.");
            return Entity.Null;
        }

        private DamageReplayEvent[] ReadFinalizedDamageEvents()
        {
            DamageDispatchBridge bridge = testWorld.GetExistingSystemManaged<DamageDispatchBridge>();
            const global::System.Reflection.BindingFlags Flags =
                global::System.Reflection.BindingFlags.Instance |
                global::System.Reflection.BindingFlags.NonPublic;
            var countField = typeof(DamageDispatchBridge).GetField("FinalizedDamageCount", Flags);
            var eventsField = typeof(DamageDispatchBridge).GetField("FinalizedDamageEvents", Flags);
            int count = (int)countField.GetValue(bridge);
            var events = (NativeArray<DamageReplayEvent>)eventsField.GetValue(bridge);
            var result = new DamageReplayEvent[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = events[i];
            }

            return result;
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
            public bool IsCombatTargetActive => Active;
            public void ReceiveHit(in CombatHitData hit)
            {
                HitCount++;
                if (DeactivateOnHit)
                {
                    Active = false;
                }
            }
        }
    }
}
