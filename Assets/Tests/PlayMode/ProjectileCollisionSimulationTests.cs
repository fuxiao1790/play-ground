using NUnit.Framework;
using PlayGround.Common;
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
        private ProjectileSpawnExpansionSystem projectileExpansion;
        private Entity scopeEntity;
        private double elapsedTime;
        private int nextProjectileId;
        private int nextTargetId = 7000;
        private const float TestTargetHealth = 10f;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ProjectileCollisionSimulationTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            projectileExpansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<StatusProcessSystem>());
            simGroup.SortSystems();
            testWorld.GetOrCreateSystemManaged<CombatApplyBridge>();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
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
            Entity target = AddTarget(float2.zero, 0.25f);
            Entity projectile = CreateProjectile(pierceRemaining: 0);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedHitCount(), Is.EqualTo(1));
            Assert.That(entityManager.GetComponentData<TargetHealth>(target).Current, Is.EqualTo(TestTargetHealth - 1f).Within(0.0001f));
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

            Assert.That(ReadFinalizedHitCount(), Is.EqualTo(3));
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
            Assert.That(entityManager.GetComponentData<ProjectileHitComponent>(projectile).PierceRemaining, Is.EqualTo(-1));
        }

        [Test]
        public void ExhaustedProjectileEarlyOutDoesNotHit()
        {
            AddTarget(float2.zero, 0.25f);
            Entity projectile = CreateProjectile(pierceRemaining: -1);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedHitCount(), Is.EqualTo(0));
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
        }

        [Test]
        public void ProjectileApplicatorProjectileDetonationQueuesNovaWithSummedContribution()
        {
            const float TotalDamage = 15f;
            const int ProjectileCount = 6;
            AddTarget(float2.zero, 0.25f);
            CreateProjectile(
                pierceRemaining: 0,
                stackEffect: ProjectileStackEffect(
                    debuffKey: 801,
                    threshold: 1,
                    lifetime: 10f,
                    damage: TotalDamage,
                    projectileCount: ProjectileCount));

            TickSimulationOnly(0.01f);

            ProjectileSpawnEvent detonation = DequeueSingleProjectileEvent();
            Assert.That(detonation.Count, Is.EqualTo(ProjectileCount));
            Assert.That(detonation.TypeId, Is.EqualTo(88));
            Assert.That(detonation.HitPayload.DamageAmount * detonation.Count, Is.EqualTo(TotalDamage).Within(0.0001f));
        }

        private void TickSimulationOnly(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private Entity CreateProjectile(int pierceRemaining, StackEffectSnapshot stackEffect = default)
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
                    DirectDamageEnabled = true,
                    StackEffect = stackEffect
                })
            });

            return entity;
        }

        private Entity AddTarget(float2 position, float radius)
        {
            var target = new TestCombatTarget(++nextTargetId, position, radius);
            return CombatTargetProxy.Create(entityManager, target, CombatFaction.Player);
        }

        private static StackEffectSnapshot ProjectileStackEffect(
            int debuffKey,
            int threshold,
            float lifetime,
            float damage,
            int projectileCount)
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
                Detonation = new DetonationSnapshot
                {
                    Kind = StackDetonationKind.Projectile,
                    Faction = CombatFaction.Player,
                    TargetMask = ~0,
                    TypeId = 88,
                    ProjectileBurst = new AoeProjectileBurstSnapshot(
                        88,
                        ~0,
                        1,
                        60f,
                        7f,
                        3f,
                        0.25f,
                        Vector2.zero,
                        0f,
                        CombatShapeType.Circle,
                        new DamageSnapshot(1f),
                        true,
                        pierceCount: 1,
                        repeatHitCooldownSeconds: 0.1f)
                }
            };
        }

        private ProjectileSpawnEvent DequeueSingleProjectileEvent()
        {
            NativeQueue<ProjectileSpawnEvent> queue = ProjectileEventQueue();
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out ProjectileSpawnEvent evt), Is.True);
            return evt;
        }

        private NativeQueue<ProjectileSpawnEvent> ProjectileEventQueue()
        {
            const global::System.Reflection.BindingFlags Flags =
                global::System.Reflection.BindingFlags.Instance |
                global::System.Reflection.BindingFlags.NonPublic;
            var field = typeof(ProjectileSpawnExpansionSystem).GetField("EventQueue", Flags);
            Assert.That(field, Is.Not.Null);
            return (NativeQueue<ProjectileSpawnEvent>)field.GetValue(projectileExpansion);
        }

        private int ReadFinalizedHitCount()
        {
            CombatTickResult[] results = ReadFinalizedCombatResults();
            int count = 0;
            for (int i = 0; i < results.Length; i++)
            {
                count += results[i].HitCount;
            }

            return count;
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
            public float CombatMaxHealth => TestTargetHealth;
            public bool IsCombatTargetActive => true;
            public void ReceiveHit(in CombatHitData hit)
            {
            }
        }
    }
}
