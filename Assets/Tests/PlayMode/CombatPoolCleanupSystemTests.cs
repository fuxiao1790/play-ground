using NUnit.Framework;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
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
        private Entity clockEntity;
        private NativeHashMap<Hash128, ProjectileSpawnCommand> projectileTemplateMap;
        private Entity scopeEntity;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CombatPoolCleanupSystemTest");
            entityManager = testWorld.EntityManager;
            cleanupSystem = testWorld.GetOrCreateSystemManaged<CombatPoolCleanupSystem>();
            clockEntity = entityManager.CreateEntity(typeof(CombatFrameClock));
            SetClock(smoothedMs: 0f);
            SetConfig(Config(retentionTarget: 3, poolRatioMultiplier: 1f, perPoolDeleteCap: 4, maxDeletesPerFrame: 64));
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
        public void DrainsDisabledProjectilePoolWhenIdleUntilFloor()
        {
            CreateDisabledProjectiles(count: 10);

            RunCleanup();

            Assert.That(DisabledProjectileCount(), Is.EqualTo(6), "First frame is capped by PerPoolDeleteCap.");

            RunCleanup();
            RunCleanup();

            Assert.That(DisabledProjectileCount(), Is.EqualTo(3), "Cleanup must converge to the retention floor, not zero.");
        }

        [Test]
        public void SuppressedWhenSmoothedFrameGateFails()
        {
            CreateDisabledProjectiles(count: 10);
            SetConfig(Config(budgetMs: 1f, retentionTarget: 1, poolRatioMultiplier: 1f, perPoolDeleteCap: 10, maxDeletesPerFrame: 10));
            SetClock(smoothedMs: 2f);

            RunCleanup();

            Assert.That(DisabledProjectileCount(), Is.EqualTo(10));
        }

        [Test]
        public void SuppressedWhenCurrentFrameElapsedGateFails()
        {
            CreateDisabledProjectiles(count: 10);
            SetConfig(Config(budgetMs: 1f, retentionTarget: 1, poolRatioMultiplier: 1f, perPoolDeleteCap: 10, maxDeletesPerFrame: 10));
            SetClock(smoothedMs: 0f, frameStartOffsetMs: -10f);

            RunCleanup();

            Assert.That(DisabledProjectileCount(), Is.EqualTo(10));
        }

        [Test]
        public void PerFrameGlobalCapBoundsDeletesAcrossPools()
        {
            SetConfig(Config(retentionTarget: 1, poolRatioMultiplier: 0f, perPoolDeleteCap: 10, maxDeletesPerFrame: 7));
            CreateDisabledProjectiles(count: 10);
            CreateDisabledImpactAoes(count: 10);

            int before = TotalPooledCount();
            RunCleanup();
            int deleted = before - TotalPooledCount();

            Assert.That(deleted, Is.GreaterThan(0));
            Assert.That(deleted, Is.LessThanOrEqualTo(7));
        }

        [Test]
        public void RetentionAndRatioFloorAreRespectedPerPool()
        {
            SetConfig(Config(retentionTarget: 3, poolRatioMultiplier: 4f, perPoolDeleteCap: 64, maxDeletesPerFrame: 64));
            CreateActiveProjectiles(count: 5);
            CreateDisabledProjectiles(count: 4);

            RunCleanup();

            Assert.That(ProjectilePoolCount(), Is.EqualTo(9), "Ratio gate should block trim while active count is high.");

            CreateActiveImpactAoes(count: 2);
            CreateDisabledImpactAoes(count: 20);

            RunCleanup();

            Assert.That(ActiveImpactAoeCount(), Is.EqualTo(2));
            Assert.That(DisabledImpactAoeCount(), Is.EqualTo(8), "Floor is max(retention, ceil(active * ratio)).");
        }

        [Test]
        public void ReuseClaimsDisabledSlotsBeforeCleanupDeletesExcess()
        {
            SetConfig(Config(retentionTarget: 1, poolRatioMultiplier: 1f, perPoolDeleteCap: 64, maxDeletesPerFrame: 64));
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

            Assert.That(ProjectilePoolCount(), Is.EqualTo(6), "Three reused active slots plus the retention/ratio floor remain.");
        }

        [Test]
        public void ActiveProjectileContinuesToSimulateAfterDisabledPoolTrim()
        {
            SetConfig(Config(retentionTarget: 1, poolRatioMultiplier: 0f, perPoolDeleteCap: 64, maxDeletesPerFrame: 64));
            Entity active = CreateMovableProjectile();
            CreateDisabledProjectiles(count: 10);

            RunCleanup();
            TickMovement(0.25f);

            Assert.That(entityManager.Exists(active), Is.True);
            Assert.That(entityManager.IsComponentEnabled<Active>(active), Is.True);
            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(active);
            Assert.That(kinematics.Position.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(DisabledProjectileCount(), Is.EqualTo(1));
        }

        private void RunCleanup()
        {
            cleanupSystem.Update();
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
                    IsRenderable = 1,
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
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderBatchId),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            entityManager.SetComponentData(entity, new CombatRenderBatchId { Value = 1 });
            entityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = entity.Index,
                TypeId = 1
            });
            entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentEnabled<Active>(entity, active);
            entityManager.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, active);
            entityManager.SetComponentEnabled<CombatRenderActiveTag>(entity, active);
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
                typeof(AoeAreaComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderBatchId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag));

            entityManager.SetComponentData(entity, new CombatRenderBatchId { Value = 2 });
            entityManager.SetComponentEnabled<Active>(entity, active);
            entityManager.SetComponentEnabled<AoeCollisionActiveTag>(entity, active);
            entityManager.SetComponentEnabled<CombatRenderActiveTag>(entity, active);
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

        private int TotalPooledCount()
        {
            return ProjectilePoolCount() + ImpactAoePoolCount();
        }

        private int ProjectilePoolCount()
        {
            return CountPooled(ProjectileQuery(), activeOnly: false, disabledOnly: false);
        }

        private int ImpactAoePoolCount()
        {
            return CountPooled(ImpactAoeQuery(), activeOnly: false, disabledOnly: false);
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
                .WithNone<CombatLifetimeComponent>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(entityManager);
        }

        private void SetClock(float smoothedMs, float frameStartOffsetMs = 0f)
        {
            entityManager.SetComponentData(clockEntity, new CombatFrameClock
            {
                FrameStartTime = UnityEngine.Time.realtimeSinceStartupAsDouble + (frameStartOffsetMs / 1000.0),
                SmoothedFrameMs = smoothedMs
            });
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

        private static CombatPoolCleanupConfig Config(
            float budgetMs = 100000f,
            int retentionTarget = 3,
            float poolRatioMultiplier = 1f,
            int perPoolDeleteCap = 4,
            int maxDeletesPerFrame = 64,
            float sliceMs = 0f)
        {
            return new CombatPoolCleanupConfig
            {
                BudgetMs = budgetMs,
                EmaAlpha = 0.1f,
                RetentionTarget = retentionTarget,
                PoolRatioMultiplier = poolRatioMultiplier,
                PerPoolDeleteCap = perPoolDeleteCap,
                MaxDeletesPerFrame = maxDeletesPerFrame,
                SliceMs = sliceMs
            };
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
