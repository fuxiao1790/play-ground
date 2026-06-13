using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

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

            scopeEntity = entityManager.CreateEntity(typeof(ProjectileScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitPayloadElement>(scopeEntity);
            entityManager.AddBuffer<CombatHitEffectElement>(scopeEntity);
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
            AddTarget(targetId: 100, position: new float2(0f, 50f), radius: 0.25f, targetMask: 1);
            AddTarget(targetId: 101, position: new float2(10f, 0f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(90f),
                trackedTargetId: 100,
                trackedTargetIndex: 0,
                trackedTargetPosition: new float2(0f, 50f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(100));
        }

        [Test]
        public void KeepsCurrentTargetWhileStillInRange()
        {
            AddTarget(targetId: 100, position: new float2(-5f, 0f), radius: 0.25f, targetMask: 1);
            AddTarget(targetId: 101, position: new float2(20f, 0f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(10f, 0f),
                turnSpeedRadians: math.radians(90f),
                trackedTargetId: 100,
                trackedTargetIndex: 0,
                trackedTargetPosition: new float2(-5f, 0f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(100));
        }

        [Test]
        public void AcquiresForwardTargetInsteadOfNearestSideTarget()
        {
            AddTarget(targetId: 100, position: new float2(0f, 10f), radius: 0.25f, targetMask: 1);
            AddTarget(targetId: 101, position: new float2(40f, 0f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(10f, 0f),
                turnSpeedRadians: math.radians(180f));

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(101));
        }

        [Test]
        public void ReacquiresNearbyTargetWithManyFarTargets()
        {
            for (int i = 0; i < 500; i++)
            {
                AddTarget(
                    targetId: 1000 + i,
                    position: new float2(1000f + i * 2f, 1000f),
                    radius: 0.25f,
                    targetMask: 1);
            }

            AddTarget(targetId: 200, position: new float2(0f, 40f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(180f),
                trackingRange: 60f);

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(200));
        }

        [Test]
        public void AcquiresImmediatelyWhenNoTargetDespiteCooldown()
        {
            AddTarget(targetId: 300, position: new float2(0f, 40f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(180f),
                trackingRange: 60f,
                queryCooldownRemaining: 5f,
                queryIntervalSeconds: 5f);

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(300));
        }

        [Test]
        public void ReacquiresImmediatelyWhenTrackedTargetIsMissingDespiteCooldown()
        {
            AddTarget(targetId: 401, position: new float2(0f, 40f), radius: 0.25f, targetMask: 1);
            SpawnTrackedProjectile(
                position: float2.zero,
                velocity: new float2(0f, 10f),
                turnSpeedRadians: math.radians(180f),
                trackingRange: 60f,
                trackedTargetId: 400,
                trackedTargetIndex: -1,
                trackedTargetPosition: new float2(0f, -40f),
                queryCooldownRemaining: 5f,
                queryIntervalSeconds: 5f);

            Tick(0.1f);

            ProjectileTrackingComponent tracking =
                entityManager.GetComponentData<ProjectileTrackingComponent>(projectileEntity);
            Assert.That(tracking.TrackedTargetId, Is.EqualTo(401));
        }

        [Test]
        public void AcquisitionSpreadsIdenticalProjectilesAcrossEqualTargets()
        {
            AddTarget(targetId: 500, position: new float2(-5f, 40f), radius: 0.25f, targetMask: 1);
            AddTarget(targetId: 501, position: new float2(5f, 40f), radius: 0.25f, targetMask: 1);
            var acquiredTargets = new HashSet<int>();
            var projectiles = new Entity[12];

            for (int i = 0; i < projectiles.Length; i++)
            {
                projectiles[i] = SpawnTrackedProjectile(
                    position: float2.zero,
                    velocity: new float2(0f, 10f),
                    turnSpeedRadians: math.radians(180f),
                    trackingRange: 60f,
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
            float trackingRange = 100f,
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
                typeof(ProjectileLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(ProjectileActiveTag));
            entityManager.SetComponentData(projectileEntity, new ProjectileIdentityComponent
            {
                Scope = scopeEntity,
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
            entityManager.SetComponentData(projectileEntity, new ProjectileLifetimeComponent
            {
                RemainingLifetime = 10f
            });
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
                TrackingRangeSquared = trackingRange * trackingRange,
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

        private void AddTarget(int targetId, float2 position, float radius, int targetMask)
        {
            entityManager.GetBuffer<CombatTargetElement>(scopeEntity).Add(new CombatTargetElement
            {
                TargetId = targetId,
                TargetMask = targetMask,
                Position = position,
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                BoundsMin = position - radius,
                BoundsMax = position + radius
            });
        }
    }
}
