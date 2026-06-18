using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.Tests.PlayMode
{
    public sealed class ProjectileTrackingSimulationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private Entity projectileEntity;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ProjectileTrackingSimulationTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileSimulationSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileTrackingSystem>());
            simGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
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
        public void KeepsSameReachableTargetWhenAnotherTargetIsCloser()
        {
            int reachableTargetId = AddTarget(position: new float2(0f, 50f), radius: 0.25f, targetMask: 1);
            AddTarget(position: new float2(10f, 0f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(90f),
                trackedTargetId: reachableTargetId,
                trackedTargetIndex: 0,
                trackedTargetPosition: new float2(0f, 50f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(reachableTargetId));
        }

        [Test]
        public void KeepsCurrentTargetWhileStillInRange()
        {
            int currentTargetId = AddTarget(position: new float2(-5f, 0f), radius: 0.25f, targetMask: 1);
            AddTarget(position: new float2(20f, 0f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(10f, 0f),
                turnSpeedRadians: math.radians(90f),
                trackedTargetId: currentTargetId,
                trackedTargetIndex: 0,
                trackedTargetPosition: new float2(-5f, 0f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(currentTargetId));
        }

        [Test]
        public void AcquiresForwardTargetInsteadOfNearestSideTarget()
        {
            AddTarget(position: new float2(0f, 10f), radius: 0.25f, targetMask: 1);
            int forwardTargetId = AddTarget(position: new float2(40f, 0f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(10f, 0f),
                turnSpeedRadians: math.radians(180f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(forwardTargetId));
        }

        [Test]
        public void ReacquiresNearbyTargetWithManyFarTargets()
        {
            for (int i = 0; i < 500; i++)
            {
                AddTarget(
                    position: new float2(1000f + i * 2f, 1000f),
                    radius: 0.25f,
                    targetMask: 1);
            }

            int nearbyTargetId = AddTarget(position: new float2(0f, 40f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(180f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(nearbyTargetId));
        }

        [Test]
        public void AcquiresImmediatelyWhenNoTargetDespiteCooldown()
        {
            int targetId = AddTarget(position: new float2(0f, 40f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(180f),
                queryCooldownRemaining: 5f,
                queryIntervalSeconds: 5f);

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(targetId));
        }

        [Test]
        public void ReacquiresImmediatelyWhenTrackedTargetIsMissingDespiteCooldown()
        {
            int targetId = AddTarget(position: new float2(0f, 40f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(180f),
                trackedTargetId: 400,
                trackedTargetIndex: -1,
                trackedTargetPosition: new float2(0f, -40f),
                queryCooldownRemaining: 5f,
                queryIntervalSeconds: 5f);

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(targetId));
        }

        [Test]
        public void AcquisitionSpreadsIdenticalProjectilesAcrossEqualTargets()
        {
            AddTarget(position: new float2(-5f, 40f), radius: 0.25f, targetMask: 1);
            AddTarget(position: new float2(5f, 40f), radius: 0.25f, targetMask: 1);
            var acquiredTargets = new HashSet<int>();
            var projectiles = new Entity[12];

            for (int i = 0; i < projectiles.Length; i++)
            {
                projectiles[i] = SpawnTrackedProjectile(
                    position: float2.zero,
                    velocity: new float2(0f, 10f),
                    turnSpeedRadians: math.radians(180f),
                    projectileId: i + 1);
            }

            Tick(0.1f);

            for (int i = 0; i < projectiles.Length; i++)
            {
                ProjectileTrackingComponent tracking =
                    entityManager.GetComponentData<ProjectileTrackingComponent>(projectiles[i]);
                acquiredTargets.Add(tracking.TrackedTargetId);
            }

            Assert.That(acquiredTargets.Count, Is.GreaterThan(1));
        }

        private void Tick(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
            entityManager.CompleteAllTrackedJobs();
        }

        private Entity SpawnTrackedProjectile(
            float2 position,
            float2 velocity,
            float turnSpeedRadians,
            int trackedTargetId = 0,
            int trackedTargetIndex = -1,
            float2 trackedTargetPosition = default,
            float queryCooldownRemaining = 0f,
            float queryIntervalSeconds = 0f,
            int projectileId = 1)
        {
            projectileEntity = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(Active));
            entityManager.SetComponentData(projectileEntity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = projectileId,
                TypeId = 1
            });
            entityManager.SetComponentData(projectileEntity, new CombatKinematicsComponent
            {
                Position = position,
                Velocity = velocity
            });
            entityManager.SetComponentData(projectileEntity, new CombatCollisionComponent
            {
                ShapeType = CombatShapeType.Circle,
                Radius = 0.25f,
                BoundsMin = position - 0.25f,
                BoundsMax = position + 0.25f
            });
            entityManager.SetComponentData(projectileEntity, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentEnabled<CombatLifetimeComponent>(projectileEntity, true);
            entityManager.SetComponentData(projectileEntity, new ProjectileHitComponent
            {
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                })
            });
            entityManager.SetComponentData(projectileEntity, new ProjectileTrackingComponent
            {
                TrackingEnabled = true,
                TrackingTurnSpeedRadians = turnSpeedRadians,
                TrackingQueryCooldownRemaining = queryCooldownRemaining,
                TrackingQueryIntervalSeconds = queryIntervalSeconds,
                TrackedTargetId = trackedTargetId,
                TrackedTargetIndex = trackedTargetIndex,
                TrackedTargetPosition = trackedTargetPosition
            });
            entityManager.SetComponentEnabled<ProjectileTrackingComponent>(projectileEntity, true);
            return projectileEntity;
        }

        private int AddTarget(float2 position, float radius, int targetMask)
        {
            Entity target = entityManager.CreateEntity(
                typeof(TargetProxyTag),
                typeof(TargetPosition),
                typeof(TargetCollisionShape),
                typeof(TargetFaction),
                typeof(TargetCompanion));
            entityManager.SetComponentData(target, new TargetPosition { Value = position });
            entityManager.SetComponentData(target, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                HalfExtents = float2.zero,
                BoundsMin = position - radius,
                BoundsMax = position + radius,
                Mask = targetMask
            });
            entityManager.SetComponentData(target, new TargetFaction { Value = CombatFaction.Player });
            entityManager.SetComponentData(target, new TargetCompanion { Target = new TestTarget(targetMask) });
            return CombatTargetProxy.TargetKey(target);
        }

        private sealed class TestTarget : ICombatTarget
        {
            public TestTarget(int mask)
            {
                CombatTargetMask = mask;
            }

            public int TargetId => 0;
            public Vector2 CombatTargetPosition => Vector2.zero;
            public float CombatTargetRadius => 0.25f;
            public Vector2 CombatTargetHalfExtents => Vector2.zero;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask { get; }
            public bool IsCombatTargetActive => true;
            public void ReceiveHit(in CombatHitData hit) { }
        }
    }
}
