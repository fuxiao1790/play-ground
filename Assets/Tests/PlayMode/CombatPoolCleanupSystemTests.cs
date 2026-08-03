using NUnit.Framework;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.Tests.PlayMode
{
    public sealed class CombatPoolCleanupSystemTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private CombatPoolCleanupSystem cleanupSystem;
        private NativeHashMap<Hash128, ProjectileSpawnCommand> projectileTemplateMap;
        private Entity scopeEntity;
        private Entity statsEntity;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CombatPoolCleanupSystemTest");
            entityManager = testWorld.EntityManager;
            cleanupSystem = testWorld.GetOrCreateSystemManaged<CombatPoolCleanupSystem>();
            SetConfig(new CombatPoolCleanupConfig { ChunkActiveThresholdPercent = 40f });
        }

        [TearDown]
        public void TearDown()
        {
            if (projectileTemplateMap.IsCreated)
            {
                projectileTemplateMap.Dispose();
            }

            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
        }

        [Test]
        public void TrimsDisabledEntitiesInSparseChunk()
        {
            CreateActiveProjectiles(count: 3);
            CreateDisabledProjectiles(count: 8);

            RunCleanup();

            Assert.That(DisabledProjectileCount(), Is.EqualTo(0), "Sparse chunk sheds all its disabled entities.");
            Assert.That(ActiveProjectileEntities().Length, Is.EqualTo(3), "Active entities are never destroyed.");
        }

        [Test]
        public void RetainsDisabledEntitiesInBusyChunk()
        {
            CreateActiveProjectiles(count: 5);
            CreateDisabledProjectiles(count: 8);

            RunCleanup();

            Assert.That(ActiveProjectileEntities().Length, Is.EqualTo(5));
            Assert.That(DisabledProjectileCount(), Is.EqualTo(8),
                "Chunk at/above the active threshold keeps its disabled reuse buffer.");
        }

        [Test]
        public void FullyIdlePoolDrainsToZero()
        {
            CreateDisabledProjectiles(count: 10);

            RunCleanup();

            Assert.That(ProjectilePoolCount(), Is.EqualTo(0), "A pool with no active entities drains completely.");
        }

        [Test]
        public void TrimsEverySparsePoolInOnePass()
        {
            CreateActiveProjectiles(count: 2);
            CreateDisabledProjectiles(count: 6);
            CreateDisabledSweptProjectiles(count: 6);
            CreateActiveImpactAoes(count: 2);
            CreateDisabledImpactAoes(count: 6);

            RunCleanup();

            Assert.That(DisabledProjectileCount(), Is.EqualTo(0));
            Assert.That(DisabledSweptProjectileCount(), Is.EqualTo(0));
            Assert.That(DisabledImpactAoeCount(), Is.EqualTo(0));
            Assert.That(ActiveProjectileEntities().Length, Is.EqualTo(2));
            Assert.That(ActiveImpactAoeCount(), Is.EqualTo(2));
        }

        [Test]
        public void CalmGateSkipsTrimWhileSpawnsOutpaceDespawns()
        {
            SetCalmGateConfig();
            CreateActiveProjectiles(count: 3);
            CreateDisabledProjectiles(count: 8);

            int activeLoad = 0;
            for (int i = 0; i < 10; i++)
            {
                activeLoad += 100;
                SetStats(activeProjectiles: activeLoad, spawnedViaReuse: 100);
                TickCleanup(1f / 60f);
            }

            Assert.That(DisabledProjectileCount(), Is.EqualTo(8),
                "A climbing scene (spawns ahead of despawns) never trims.");
        }

        [Test]
        public void CalmGateSkipsTrimAtBusyEquilibrium()
        {
            SetCalmGateConfig();
            CreateActiveProjectiles(count: 3);
            CreateDisabledProjectiles(count: 8);

            for (int i = 0; i < 30; i++)
            {
                SetStats(activeProjectiles: 1000, spawnedViaReuse: 100);
                TickCleanup(1f / 60f);
            }

            Assert.That(DisabledProjectileCount(), Is.EqualTo(8),
                "Busy equilibrium (spawn rate ~= despawn rate) keeps the gate closed.");
        }

        [Test]
        public void CalmGateOpensDuringWindDownAndTrims()
        {
            SetCalmGateConfig();
            CreateActiveProjectiles(count: 3);
            CreateDisabledProjectiles(count: 8);

            int activeLoad = 1000;
            SetStats(activeProjectiles: activeLoad, spawnedViaReuse: 0);
            TickCleanup(1f / 60f);
            for (int i = 0; i < 5; i++)
            {
                activeLoad -= 100;
                SetStats(activeProjectiles: activeLoad, spawnedViaReuse: 0);
                TickCleanup(1f / 60f);
            }

            Assert.That(DisabledProjectileCount(), Is.EqualTo(0),
                "Despawns pulling ahead of spawns opens the gate while the scene is still winding down.");
            Assert.That(ActiveProjectileEntities().Length, Is.EqualTo(3));
        }

        [Test]
        public void ReuseClaimsDisabledSlotsBeforeCleanupDeletesExcess()
        {
            Entity[] originalSlots = CreateDisabledProjectiles(count: 10);
            SetupProjectileSpawnPipeline();
            EnqueueProjectileSpawn(count: 3);

            TickSpawnApplyThenCleanup(0.01f);

            Entity[] activeEntities = ActiveProjectileEntities();
            Assert.That(activeEntities.Length, Is.EqualTo(3));
            for (int i = 0; i < activeEntities.Length; i++)
            {
                Assert.That(Contains(originalSlots, activeEntities[i]), Is.True, "Spawn must reuse a disabled slot before cleanup trims.");
            }

            Assert.That(ProjectilePoolCount(), Is.EqualTo(3),
                "Three reused active slots survive; the remaining disabled slots are trimmed.");
        }

        [Test]
        public void ActiveProjectileContinuesToSimulateAfterDisabledPoolTrim()
        {
            Entity active = CreateMovableProjectile();
            CreateDisabledProjectiles(count: 10);

            RunCleanup();
            TickMovement(0.25f);

            Assert.That(entityManager.Exists(active), Is.True);
            Assert.That(entityManager.IsComponentEnabled<Active>(active), Is.True);
            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(active);
            Assert.That(kinematics.Position.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(DisabledProjectileCount(), Is.EqualTo(0));
        }

        private void RunCleanup()
        {
            cleanupSystem.Update();
        }

        private void TickCleanup(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            cleanupSystem.Update();
        }

        private void SetCalmGateConfig()
        {
            SetConfig(new CombatPoolCleanupConfig
            {
                ChunkActiveThresholdPercent = 40f,
                DespawnOverSpawnMargin = 1.25f,
                RateSmoothingTime = 0.5f
            });
        }

        private void SetStats(int activeProjectiles, int spawnedViaReuse)
        {
            if (statsEntity == Entity.Null)
            {
                statsEntity = entityManager.CreateEntity(typeof(CombatStatsSingleton));
            }

            entityManager.SetComponentData(statsEntity, new CombatStatsSingleton
            {
                ActiveProjectiles = activeProjectiles,
                EntitiesSpawnedViaReuse = spawnedViaReuse
            });
        }

        private void TickMovement(float dt)
        {
            SimulationSystemGroup simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileMovementSystem>());
            simGroup.SortSystems();
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private void TickSpawnApplyThenCleanup(float dt)
        {
            ProjectileSpawnExpansionSystem expansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            ProjectileSpawnApplySystem apply = testWorld.GetOrCreateSystemManaged<ProjectileSpawnApplySystem>();
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            expansion.Update();
            apply.Update();
            RunCleanup();
        }

        private void SetupProjectileSpawnPipeline()
        {
            testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();
            testWorld.GetOrCreateSystemManaged<ProjectileSpawnApplySystem>();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            projectileTemplateMap = new NativeHashMap<Hash128, ProjectileSpawnCommand>(8, Allocator.Persistent);
            entityManager.AddComponentData(scopeEntity, new ProjectileSpawnTemplate { Map = projectileTemplateMap });
        }

        private void EnqueueProjectileSpawn(int count)
        {
            var template = new ProjectileSpawnCommand
            {
                TypeId = 1,
                RenderTypeId = 42,
                Count = count,
                Speed = 0f,
                Lifetime = 10f,
                Radius = 0.25f,
                ShapeType = CombatShapeType.Circle,
                BaseDirection = new float2(1f, 0f),
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                }),
                Render = new CombatRenderComponent
                {
                    RenderTypeId = 42
                },
                Authoring = new CombatRenderAuthoring
                {
                    VisualScale = new float2(1f, 1f)
                }
            };
            Hash128 key = SpawnTemplateHash.Of(in template);
            projectileTemplateMap.TryAdd(key, template);
            entityManager.GetBuffer<ProjectileSpawnEvent>(scopeEntity).Add(new ProjectileSpawnEvent
            {
                Kind = IntervalChildKind.Projectile,
                TemplateKey = key,
                AimDirection = new float2(1f, 0f),
                Faction = CombatFaction.Player,
                SourceId = 99
            });
        }

        private Entity[] CreateDisabledProjectiles(int count)
        {
            var entities = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                entities[i] = CreateProjectile(active: false);
            }

            return entities;
        }

        private void CreateDisabledSweptProjectiles(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Entity entity = entityManager.CreateEntity(
                    typeof(ProjectileTag),
                    typeof(SweptProjectileTag),
                    typeof(ProjectileSweepComponent),
                    typeof(Active));
                entityManager.SetComponentEnabled<Active>(entity, false);
            }
        }

        private void CreateActiveProjectiles(int count)
        {
            for (int i = 0; i < count; i++)
            {
                CreateProjectile(active: true);
            }
        }

        private void CreateDisabledImpactAoes(int count)
        {
            for (int i = 0; i < count; i++)
            {
                CreateImpactAoe(active: false);
            }
        }

        private void CreateActiveImpactAoes(int count)
        {
            for (int i = 0; i < count; i++)
            {
                CreateImpactAoe(active: true);
            }
        }

        private Entity CreateMovableProjectile()
        {
            Entity entity = CreateProjectile(active: true);
            entityManager.SetComponentData(entity, new CombatKinematicsComponent
            {
                Position = float2.zero,
                Velocity = new float2(2f, 0f)
            });
            entityManager.SetComponentData(entity, new CombatCollisionComponent
            {
                ShapeType = CombatShapeType.Circle,
                Radius = 0.25f
            });
            return entity;
        }

        private Entity CreateProjectile(bool active)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(CombatHitPayload),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            entityManager.SetComponentData(entity, new CombatRenderKindId { Value = 1 });
            entityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = entity.Index,
                TypeId = 1
            });
            entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentEnabled<Active>(entity, active);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, active);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            entityManager.SetComponentEnabled<ProjectileTrackingComponent>(entity, false);
            entityManager.SetComponentEnabled<TimedSpawnComponent>(entity, false);
            return entity;
        }

        private Entity CreateImpactAoe(bool active)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(CombatHitPayload),
                typeof(AoeAreaComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent));

            entityManager.SetComponentData(entity, new CombatRenderKindId { Value = 2 });
            entityManager.SetComponentEnabled<Active>(entity, active);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, active);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            return entity;
        }

        private Entity[] ActiveProjectileEntities()
        {
            using EntityQuery query = ProjectileQuery();
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            var active = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.IsComponentEnabled<Active>(entities[i]))
                {
                    active.Add(entities[i]);
                }
            }

            Entity[] copy = active.AsArray().ToArray();
            active.Dispose();
            return copy;
        }

        private int ProjectilePoolCount()
        {
            return CountPooled(ProjectileQuery(), activeOnly: false, disabledOnly: false);
        }

        private int ActiveImpactAoeCount()
        {
            return CountPooled(ImpactAoeQuery(), activeOnly: true, disabledOnly: false);
        }

        private int DisabledImpactAoeCount()
        {
            return CountPooled(ImpactAoeQuery(), activeOnly: false, disabledOnly: true);
        }

        private int DisabledProjectileCount()
        {
            return CountPooled(ProjectileQuery(), activeOnly: false, disabledOnly: true);
        }

        private int DisabledSweptProjectileCount()
        {
            return CountPooled(SweptProjectileQuery(), activeOnly: false, disabledOnly: true);
        }

        private int CountPooled(EntityQuery query, bool activeOnly, bool disabledOnly)
        {
            using (query)
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                int count = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    bool active = entityManager.IsComponentEnabled<Active>(entities[i]);
                    if (activeOnly && !active)
                    {
                        continue;
                    }

                    if (disabledOnly && active)
                    {
                        continue;
                    }

                    count++;
                }

                return count;
            }
        }

        private EntityQuery ProjectileQuery()
        {
            return new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<Active>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(entityManager);
        }

        private EntityQuery ImpactAoeQuery()
        {
            return new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<Active>()
                .WithNone<LingeringAoeTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(entityManager);
        }

        private EntityQuery SweptProjectileQuery()
        {
            return new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<SweptProjectileTag>()
                .WithAll<ProjectileSweepComponent>()
                .WithAll<Active>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(entityManager);
        }

        private void SetConfig(CombatPoolCleanupConfig config)
        {
            Entity configEntity = SystemAPIQuerySingleton<CombatPoolCleanupConfig>();
            entityManager.SetComponentData(configEntity, config);
        }

        private Entity SystemAPIQuerySingleton<T>() where T : unmanaged, IComponentData
        {
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.EqualTo(1));
            return entities[0];
        }

        private static bool Contains(Entity[] entities, Entity target)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
