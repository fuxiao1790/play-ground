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
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.PlayMode
{
    public sealed class SweptProjectileSimulationTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private NativeHashMap<Unity.Entities.Hash128, AoeSpawnCommand> aoeTemplateMap;
        private double elapsedTime;
        private int nextProjectileId;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SweptProjectileSimulationTests");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();

            // These producers own the singleton lanes consumed by swept collision.
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<TargetSpatialHashSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileTrackingSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<SweptProjectileOriginSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileMovementSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<SweptProjectileCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnApplySystem>());

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
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
            if (aoeTemplateMap.IsCreated)
                aoeTemplateMap.Dispose();
            if (testWorld.IsCreated)
                testWorld.Dispose();
        }

        [Test]
        public void Tunneling_LongStepHitsAndSnapsAtImpact()
        {
            Entity target = AddTarget(new float2(95f, 0f), radius: 0.25f);
            Entity projectile = CreateSweptProjectile(new float2(0f, 0f), new float2(100f, 0f), pierce: 0);

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
            CreateSweptProjectile(float2.zero, new float2(100f, 0f), pierce: 0);

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

            CreateSweptProjectile(float2.zero, new float2(10f, 0f), pierce: 0);
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

            CreateSweptProjectile(float2.zero, new float2(10f, 0f), pierce: 1);
            Tick(1f);

            Assert.That(entityManager.GetComponentData<Health>(first).Current, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<Health>(second).Current, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<Health>(third).Current, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void SweptLaneHasNoTrackingAndMovesExactlyOnce()
        {
            AddTarget(new float2(0f, 8f), 0.25f);
            Entity projectile = CreateSweptProjectile(float2.zero, new float2(4f, 0f), pierce: 1);
            float2 velocityBefore = entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Velocity;

            Tick(0.5f);

            Assert.That(entityManager.HasComponent<ProjectileTrackingComponent>(projectile), Is.False);
            Assert.That(entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Velocity,
                Is.EqualTo(velocityBefore));
            Assert.That(entityManager.GetComponentData<CombatKinematicsComponent>(projectile).Position,
                Is.EqualTo(velocityBefore * 0.5f));
            Assert.That(entityManager.GetComponentData<ProjectileSweepComponent>(projectile).Origin,
                Is.EqualTo(float2.zero));

            using EntityQuery invalid = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SweptProjectileTag>(),
                ComponentType.ReadOnly<ProjectileTrackingComponent>());
            Assert.That(invalid.CalculateEntityCount(), Is.Zero);
        }

        [Test]
        public void ContactGatePreventsRepeatHitAcrossMultipleSweepFrames()
        {
            Entity target = AddTarget(float2.zero, radius: 2f);
            CreateSweptProjectile(new float2(-1f, 0f), new float2(1f, 0f), pierce: 1, repeatHitCooldown: 5f);

            Tick(0.5f);
            Tick(0.5f);

            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(9f).Within(0.0001f));
        }

        [Test]
        public void SweptImpactAoeSpawnsAtImpactInsteadOfFrameEnd()
        {
            var template = new AoeSpawnCommand
            {
                TypeId = 55,
                Radius = 0.25f,
                ShapeType = CombatShapeType.Circle,
                EchoCount = 1,
                HitPayload = new CombatHitPayload { DamageAmount = 1f, DirectDamageEnabled = true }
            };
            Unity.Entities.Hash128 key = SpawnTemplateHash.Of(in template);
            aoeTemplateMap.TryAdd(key, template);
            AddTarget(new float2(8f, 0f), 0.25f);
            CreateSweptProjectile(
                float2.zero,
                new float2(20f, 0f),
                pierce: 0,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.ImpactAoe, TemplateKey = key });

            Tick(1f);

            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>());
            using NativeArray<Entity> aoes = query.ToEntityArray(Allocator.Temp);
            Assert.That(aoes.Length, Is.EqualTo(1));
            float2 position = entityManager.GetComponentData<CombatKinematicsComponent>(aoes[0]).Position;
            Assert.That(math.distance(position, new float2(8f, 0f)), Is.LessThan(0.001f));
            Assert.That(math.distance(position, new float2(20f, 0f)), Is.GreaterThan(1f));
        }

        private Entity CreateSweptProjectile(
            float2 position,
            float2 velocity,
            int pierce,
            float repeatHitCooldown = 0f,
            OnHitSpawnRef onHitSpawn = default)
        {
            Entity projectile = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(SweptProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(ProjectileSweepComponent),
                typeof(CombatCollisionComponent),
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
            entityManager.SetComponentData(projectile, new ProjectileSweepComponent { Origin = position });
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
                RepeatHitCooldownSeconds = repeatHitCooldown,
                OnHitSpawn = onHitSpawn
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

        private Entity AddTarget(float2 position, float radius)
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
                typeof(TargetStackEntry));
            entityManager.SetComponentData(target, new TargetPosition { Value = position });
            entityManager.SetComponentData(target, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            });
            entityManager.SetComponentData(target, new TargetFaction { Value = CombatFaction.Mob });
            entityManager.SetComponentData(target, new Health { Current = 10f, Max = 10f });
            entityManager.SetComponentData(target, new Mana { Current = 0f, Max = 0f });
            entityManager.AddBuffer<TargetStackEntry>(target);
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
