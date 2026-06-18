using NUnit.Framework;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.PlayMode
{
    public sealed class ProjectileSpawnPipelineTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ProjectileSpawnPipelineTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatLifetimeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileMovementSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<TimedProjectileSpawnSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<BasicProjectileSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ChildSpawnerProjectileSpawnApplySystem>());
            simGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<CombatTargetElement>(scopeEntity);
            entityManager.AddBuffer<CombatDamageElement>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
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
        public void FanOut_Count3SpreadDegrees30_Produces3DistinctProjectilesWithSpreadVelocities()
        {
            EnqueueEvent(MakeEvent(count: 3, spreadDegrees: 30f));

            Tick(0.01f);

            Assert.That(ActiveProjectileCount(), Is.EqualTo(3));

            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);

            var ids = new int[3];
            var velocities = new float2[3];
            for (int i = 0; i < 3; i++)
            {
                ids[i] = entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]).ProjectileId;
                velocities[i] = entityManager.GetComponentData<CombatKinematicsComponent>(entities[i]).Velocity;
            }

            for (int i = 0; i < ids.Length; i++)
            for (int j = i + 1; j < ids.Length; j++)
                Assert.That(ids[i], Is.Not.EqualTo(ids[j]), "ProjectileIds must be distinct.");

            bool anyDifferentVelocity = false;
            for (int i = 1; i < velocities.Length; i++)
            {
                if (math.lengthsq(velocities[i] - velocities[0]) > 0.0001f)
                {
                    anyDifferentVelocity = true;
                    break;
                }
            }
            Assert.That(anyDifferentVelocity, Is.True, "Spread projectiles must have distinct velocities.");
        }

        [Test]
        public void SingleShot_Count1_ProducesOneProjectileWithExactVelocity()
        {
            const float speed = 5f;
            var dir = new float2(1f, 0f);
            EnqueueEvent(MakeEvent(count: 1, baseDirection: dir, speed: speed));

            Tick(0.01f);

            Assert.That(ActiveProjectileCount(), Is.EqualTo(1));
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entities[0]);

            Assert.That(kinematics.Velocity.x, Is.EqualTo(dir.x * speed).Within(0.001f));
            Assert.That(kinematics.Velocity.y, Is.EqualTo(dir.y * speed).Within(0.001f));
        }

        [Test]
        public void ChildSpawn_ChildSpawnerProjectile_ProducesChildWithHasChildSpawnerZero()
        {
            CreateChildSpawnerEntity();

            Tick(0.1f);

            // parent + at least one child
            Assert.That(TotalProjectileCount(), Is.GreaterThanOrEqualTo(2));

            // child has no child-spawner tag (HasChildSpawner=0 archetype)
            using EntityQuery childQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.Exclude<ProjectileChildSpawnerTag>());
            Assert.That(childQuery.CalculateEntityCount(), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NextTick_SpawnedProjectileDoesNotMoveInSameTick()
        {
            var spawnPos = new float2(5f, 5f);
            EnqueueEvent(MakeEvent(count: 1, position: spawnPos, baseDirection: new float2(1f, 0f), speed: 10f, lifetime: 10f));

            // Tick 1: movement runs before apply; entity created at spawnPos, not yet moved.
            Tick(0.1f);

            Assert.That(ActiveProjectileCount(), Is.EqualTo(1));
            float2 pos1 = ReadFirstActiveProjectilePosition();
            Assert.That(pos1.x, Is.EqualTo(spawnPos.x).Within(0.001f));
            Assert.That(pos1.y, Is.EqualTo(spawnPos.y).Within(0.001f));

            // Tick 2: movement processes the entity.
            Tick(0.1f);

            float2 pos2 = ReadFirstActiveProjectilePosition();
            Assert.That(pos2.x, Is.GreaterThan(spawnPos.x));
        }

        [Test]
        public void Reuse_ExpiredProjectileIsReusedOnRespawn()
        {
            EnqueueEvent(MakeEvent(count: 1, lifetime: 0.001f));
            // Two ticks guarantee expiry regardless of lifetime-vs-apply system ordering.
            Tick(0.01f);
            Tick(0.01f);

            Assert.That(ActiveProjectileCount(), Is.EqualTo(0));
            Assert.That(TotalProjectileCount(), Is.EqualTo(1));
            Entity first = FirstProjectileEntity();

            EnqueueEvent(MakeEvent(count: 1, lifetime: 10f));
            Tick(0.01f);
            Entity reused = FirstProjectileEntity();

            Assert.That(reused, Is.EqualTo(first));
            Assert.That(TotalProjectileCount(), Is.EqualTo(1));
        }

        private void Tick(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private void EnqueueEvent(ProjectileSpawnEvent evt)
        {
            entityManager.GetBuffer<ProjectileSpawnEvent>(scopeEntity).Add(evt);
        }

        private static ProjectileSpawnEvent MakeEvent(
            int count = 1,
            float spreadDegrees = 0f,
            float2 position = default,
            float2 baseDirection = default,
            float speed = 5f,
            float lifetime = 10f)
        {
            if (math.lengthsq(baseDirection) < 0.0001f)
                baseDirection = new float2(1f, 0f);
            return new ProjectileSpawnEvent
            {
                Faction = CombatFaction.Player,
                TypeId = 1,
                BaseProjectileId = 1,
                HasChildSpawner = 0,
                Position = position,
                BaseDirection = baseDirection,
                Speed = speed,
                Count = count,
                SpreadDegrees = spreadDegrees,
                Lifetime = lifetime,
                Radius = 0.25f,
                HalfExtents = float2.zero,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                })
            };
        }

        private void CreateChildSpawnerEntity()
        {
            Entity entity = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderElement),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement),
                typeof(ProjectileChildSpawnerTag),
                typeof(ProjectileChildSpawnerComponent),
                typeof(ProjectileChildSpawnStateComponent));

            entityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = 9999,
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
                Radius = 0.25f
            });
            entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = 100f });
            entityManager.SetComponentEnabled<CombatLifetimeComponent>(entity, true);
            entityManager.SetComponentData(entity, new ProjectileChildSpawnerComponent
            {
                SpawnerId = 9999,
                TypeId = 1,
                ChildCountPerTick = 1,
                SpawnPatternType = ProjectileChildSpawnPatternType.Forward,
                IntervalSeconds = 1f,
                Speed = 5f,
                Lifetime = 5f,
                Radius = 0.2f,
                HalfExtents = float2.zero,
                ShapeType = CombatShapeType.Circle,
                DamageAmount = 1f,
                DirectDamageEnabled = true
            });
            entityManager.SetComponentData(entity, new ProjectileChildSpawnStateComponent
            {
                ChildSpawnCooldownRemaining = 0f,
                ChildSpawnTickIndex = 0
            });
            entityManager.SetComponentEnabled<Active>(entity, true);
            entityManager.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, true);
            entityManager.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
            entityManager.SetComponentEnabled<ProjectileTrackingComponent>(entity, false);
        }

        private int ActiveProjectileCount()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>());
            return q.CalculateEntityCount();
        }

        private int TotalProjectileCount()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileTag>());
            return q.CalculateEntityCount();
        }

        private Entity FirstProjectileEntity()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileTag>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No projectile entities found.");
            return entities[0];
        }

        private float2 ReadFirstActiveProjectilePosition()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No active projectile found.");
            return entityManager.GetComponentData<CombatKinematicsComponent>(entities[0]).Position;
        }
    }
}
