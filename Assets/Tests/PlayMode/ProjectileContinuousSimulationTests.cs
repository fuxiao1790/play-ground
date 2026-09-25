using NUnit.Framework;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.PlayMode
{
    public sealed class ProjectileContinuousSimulationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand> aoeTemplateMap;
        private SpawnTemplateRegistryState templateRegistryState;
        private double elapsedTime;
        private int nextProjectileId;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ProjectileContinuousSimulationTests");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            testWorld.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>();

            // These producers own singleton lanes consumed by continuous collision.
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<TargetSpatialHashSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileTrackingSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContinuousOriginSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileMovementSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContinuousCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnApplySystem>());

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            templateRegistryState = SpawnTemplateRegistryTestState.Add(entityManager, scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ImpactAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<LingeringAoeSpawnEvent>(scopeEntity);
            aoeTemplateMap = new NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand>(4, Allocator.Persistent);
            entityManager.AddComponentData(scopeEntity, new AoeSpawnTemplate { Map = aoeTemplateMap });
            simGroup.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            SpawnTemplateRegistryTestState.Dispose(ref templateRegistryState);
            if (aoeTemplateMap.IsCreated)
                aoeTemplateMap.Dispose();
            if (testWorld.IsCreated)
                testWorld.Dispose();
        }

        [Test]
        public void Tunneling_LongStepHitsAndSnapsAtImpact()
        {
            Entity target = AddTarget(new float2(95f, 0f), radius: 0.25f);
            Entity projectile = CreateContinuousProjectile(new float2(0f, 0f), new float2(100f, 0f), pierce: 0);

            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
            Assert.That(entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Position.x,
                Is.EqualTo(95f).Within(0.0001f), "Non-piercing sweep expires at impact, not frame end.");
        }

        [Test]
        public void EndpointOnlyHit_UsesDiscreteTestAlongsideCorridor()
        {
            Entity target = AddTarget(new float2(100.15f, 0f), radius: 0.1f);
            CreateContinuousProjectile(float2.zero, new float2(100f, 0f), pierce: 0);

            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(9f).Within(0.0001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NearestTargetWinsRegardlessOfTargetRegistrationOrder(bool farTargetFirst)
        {
            Entity near;
            Entity far;
            if (farTargetFirst)
            {
                far = AddTarget(new float2(6f, 0f), 0.25f);
                near = AddTarget(new float2(2f, 0f), 0.25f);
            }
            else
            {
                near = AddTarget(new float2(2f, 0f), 0.25f);
                far = AddTarget(new float2(6f, 0f), 0.25f);
            }

            CreateContinuousProjectile(float2.zero, new float2(10f, 0f), pierce: 0);
            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(near).Current, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<Health>(far).Current, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void PierceHitsNearestTwoTargetsOnly()
        {
            Entity first = AddTarget(new float2(2f, 0f), 0.25f);
            Entity second = AddTarget(new float2(4f, 0f), 0.25f);
            Entity third = AddTarget(new float2(6f, 0f), 0.25f);

            CreateContinuousProjectile(float2.zero, new float2(10f, 0f), pierce: 1);
            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(first).Current, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<Health>(second).Current, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<Health>(third).Current, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void ContinuousLaneHasNoTrackingAndMovesExactlyOnce()
        {
            AddTarget(new float2(0f, 8f), 0.25f);
            Entity projectile = CreateContinuousProjectile(float2.zero, new float2(4f, 0f), pierce: 1);
            float2 velocityBefore = entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Velocity;

            Tick(0.5f);

            Assert.That(entityManager.HasComponent<ProjectileTrackingComponent>(projectile), Is.False);
            Assert.That(entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Velocity,
                Is.EqualTo(velocityBefore));
            Assert.That(entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Position,
                Is.EqualTo(velocityBefore * 0.5f));
            Assert.That(entityManager.GetComponentData<ProjectileContinuousStepComponent>(projectile).Origin,
                Is.EqualTo(float2.zero));

            using EntityQuery invalid = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileContinuousTag>(),
                ComponentType.ReadOnly<ProjectileTrackingComponent>());
            Assert.That(invalid.CalculateEntityCount(), Is.Zero);
        }

        [Test]
        public void ContactGatePreventsRepeatHitAcrossMultipleStepFrames()
        {
            Entity target = AddTarget(float2.zero, radius: 2f);
            CreateContinuousProjectile(new float2(-1f, 0f), new float2(1f, 0f), pierce: 1, repeatHitCooldown: 5f);

            Tick(0.5f);
            Tick(0.5f);

            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(9f).Within(0.0001f));
        }

        [Test]
        public void SelectedFactionSameFactionTargetIsHit()
        {
            Entity target = AddTarget(
                new float2(2f, 0f),
                0.25f,
                TargetFaction.AllowedFrom(CombatFaction.Player, CombatFaction.Player));
            CreateContinuousProjectile(float2.zero, new float2(10f, 0f), pierce: 0);

            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(9f).Within(0.0001f),
                "AllowedFactionOnly must accept an attacker faction equal to the target's own faction.");
        }

        [Test]
        public void SelectedFactionUnselectedAttackerIsNotHit()
        {
            Entity target = AddTarget(
                new float2(2f, 0f),
                0.25f,
                TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Mob));
            CreateContinuousProjectile(float2.zero, new float2(10f, 0f), pierce: 0);

            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(10f).Within(0.0001f),
                "Player projectile must not hit a target whose AllowedFactionOnly policy excludes Player.");
        }

        private Entity CreateContinuousProjectile(
            float2 position,
            float2 velocity,
            int pierce,
            float repeatHitCooldown = 0f)
        {
            Entity projectile = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(ProjectileContinuousTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(ProjectileContinuousStepComponent),
                typeof(CombatCollisionComponent),
                typeof(ProjectileTrailVfxComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(CombatHitPayload),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(ProjectileContactGateElement));
            entityManager.SetComponentData(projectile, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = ++nextProjectileId,
                TypeId = 1
            });
            entityManager.SetComponentData(projectile, new CombatKinematicsComponent
            {
                Position = position,
                Velocity = velocity
            });
            entityManager.SetComponentData(projectile, new ProjectileContinuousStepComponent { Origin = position });
            entityManager.SetComponentData(projectile, new CombatCollisionComponent
            {
                ShapeType = CombatShapeType.Circle,
                Radius = 0.1f,
                BoundsMin = position - 0.1f,
                BoundsMax = position + 0.1f
            });
            entityManager.SetComponentData(projectile, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentData(projectile, new ProjectileHitComponent
            {
                PierceRemaining = pierce,
                RepeatHitCooldownSeconds = repeatHitCooldown
            });
            entityManager.SetComponentData(projectile, new CombatHitPayload
            {
                DamageAmount = 1f,
                DirectDamageEnabled = true,
                CritMultiplier = 1f
            });
            entityManager.SetComponentEnabled<ArmingTag>(projectile, false);
            return projectile;
        }

        private Entity AddTarget(float2 position, float radius) =>
            AddTarget(position, radius, TargetFaction.Hostile(CombatFaction.Mob));

        private Entity AddTarget(float2 position, float radius, TargetFaction policy)
        {
            CombatCollisionMath.ComputeWorldBounds(
                position, radius, float2.zero, 0f, CombatShapeType.Circle,
                out float2 boundsMin, out float2 boundsMax);
            Entity target = entityManager.CreateEntity(
                typeof(TargetProxyTag),
                typeof(TargetPosition),
                typeof(TargetCollisionShape),
                typeof(TargetFaction),
                typeof(Health),
                typeof(Mana),
                typeof(TargetHitEnergy));
            entityManager.SetComponentData(target, new TargetPosition { Value = position });
            entityManager.SetComponentData(target, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            });
            entityManager.SetComponentData(target, policy);
            entityManager.SetComponentData(target, new Health { Current = 10f, Max = 10f });
            entityManager.SetComponentData(target, new Mana { Current = 0f, Max = 0f });
            return target;
        }

        private void Tick(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));
            simGroup.Update();
            entityManager.CompleteAllTrackedJobs();
        }
    }
}
