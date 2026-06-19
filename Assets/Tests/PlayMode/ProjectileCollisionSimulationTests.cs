using NUnit.Framework;
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
    public sealed class ProjectileCollisionSimulationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private double elapsedTime;
        private int nextProjectileId;
        private int nextTargetId = 7000;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ProjectileCollisionSimulationTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<DamageFinalizeSystem>());
            simGroup.SortSystems();
            testWorld.GetOrCreateSystemManaged<DamageDispatchBridge>();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<AoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
        }

        [Test]
        public void PierceRemainingZeroStillHitsOnceThenDespawns()
        {
            AddTarget(float2.zero, 0.25f);
            Entity projectile = CreateProjectile(pierceRemaining: 0);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedDamageEvents(), Has.Length.EqualTo(1));
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
            Assert.That(entityManager.GetComponentData<ProjectileHitComponent>(projectile).PierceRemaining, Is.EqualTo(-1));
        }

        [Test]
        public void PierceRemainingNHitsNPlusOneTargetsThenDespawns()
        {
            AddTarget(new float2(-0.1f, 0f), 0.25f);
            AddTarget(float2.zero, 0.25f);
            AddTarget(new float2(0.1f, 0f), 0.25f);
            Entity projectile = CreateProjectile(pierceRemaining: 2);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedDamageEvents(), Has.Length.EqualTo(3));
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
            Assert.That(entityManager.GetComponentData<ProjectileHitComponent>(projectile).PierceRemaining, Is.EqualTo(-1));
        }

        [Test]
        public void ExhaustedProjectileEarlyOutDoesNotHit()
        {
            AddTarget(float2.zero, 0.25f);
            Entity projectile = CreateProjectile(pierceRemaining: -1);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedDamageEvents(), Is.Empty);
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
        }

        private void TickSimulationOnly(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private Entity CreateProjectile(int pierceRemaining)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement));

            float radius = 1f;
            entityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = ++nextProjectileId,
                TypeId = 1
            });
            entityManager.SetComponentData(entity, new CombatKinematicsComponent
            {
                Position = float2.zero,
                Velocity = new float2(1f, 0f)
            });
            entityManager.SetComponentData(entity, new CombatCollisionComponent
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                HalfExtents = float2.zero,
                BoundsMin = new float2(-radius, -radius),
                BoundsMax = new float2(radius, radius)
            });
            entityManager.SetComponentData(entity, new CombatRenderComponent
            {
                VisualScale = new float2(1f, 1f)
            });
            entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentData(entity, new ProjectileHitComponent
            {
                PierceRemaining = pierceRemaining,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                })
            });

            return entity;
        }

        private void AddTarget(float2 position, float radius)
        {
            var target = new TestCombatTarget(++nextTargetId, position, radius);
            CombatTargetProxy.Create(entityManager, target, CombatFaction.Player);
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
            private readonly float2 position;
            private readonly float radius;

            public TestCombatTarget(int targetId, float2 position, float radius)
            {
                TargetId = targetId;
                this.position = position;
                this.radius = radius;
            }

            public int TargetId { get; }
            public Entity CombatTargetProxy { get; set; }
            public Vector2 CombatTargetPosition => new(position.x, position.y);
            public float CombatTargetRadius => radius;
            public Vector2 CombatTargetHalfExtents => Vector2.zero;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => ~0;
            public bool IsCombatTargetActive => true;
            public void ReceiveHit(in CombatHitData hit)
            {
            }
        }
    }
}
