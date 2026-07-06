using System;
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
    public sealed class ProjectileSpawnPipelineTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private SimulationSystemGroup simGroup;
        private Entity scopeEntity;
        private NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand> projectileTemplateMap;
        private Unity.Entities.Hash128 childProjectileTemplateKey;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ProjectileSpawnPipelineTest");
            entityManager = testWorld.EntityManager;
            simGroup = testWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatArmingSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<CombatLifetimeSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileMovementSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<TimedSpawnSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnApplySystem>());
            simGroup.SortSystems();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ImpactAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<LingeringAoeSpawnEvent>(scopeEntity);

            projectileTemplateMap = new NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand>(8, Allocator.Persistent);
            entityManager.AddComponentData(scopeEntity, new ProjectileSpawnTemplate { Map = projectileTemplateMap });
            childProjectileTemplateKey = RegisterChildProjectileTemplate();
        }

        [TearDown]
        public void TearDown()
        {
            if (projectileTemplateMap.IsCreated)
                projectileTemplateMap.Dispose();
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
        public void ProjectileArmSecondsHoldsMovementLifetimeAndTimedSpawnUntilArmed()
        {
            const int ParentId = 9100;
            EnqueueEvent(MakeEvent(
                position: float2.zero,
                speed: 10f,
                lifetime: 5f,
                hasTimedSpawner: true,
                baseProjectileId: ParentId,
                armSeconds: 0.05f,
                timedIntervalSeconds: 0.001f));

            Tick(0.01f);

            Entity parent = ProjectileById(ParentId);
            Assert.That(entityManager.IsComponentEnabled<Active>(parent), Is.True);
            Assert.That(entityManager.IsComponentEnabled<ArmingTag>(parent), Is.True);
            Assert.That(entityManager.GetComponentData<CombatLifetimeComponent>(parent).Remaining, Is.EqualTo(5f));

            Tick(0.01f);

            CombatKinematicsComponent heldKinematics =
                entityManager.GetComponentData<CombatKinematicsComponent>(parent);
            Assert.That(heldKinematics.Position.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(heldKinematics.Position.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<CombatLifetimeComponent>(parent).Remaining, Is.EqualTo(5f));
            Assert.That(TotalProjectileCount(), Is.EqualTo(1));

            Tick(0.05f);

            CombatKinematicsComponent armedKinematics =
                entityManager.GetComponentData<CombatKinematicsComponent>(parent);
            Assert.That(entityManager.IsComponentEnabled<ArmingTag>(parent), Is.False);
            Assert.That(armedKinematics.Position.x, Is.GreaterThan(0f));
            Assert.That(entityManager.GetComponentData<CombatLifetimeComponent>(parent).Remaining, Is.LessThan(5f));
            Assert.That(TotalProjectileCount(), Is.GreaterThan(1));
        }

        [Test]
        public void SpawnEventsAreSlim_TemplateFieldsLiveOnlyInCommands()
        {
            // Thin events carry only registry link + per-instance frame — no template fields.
            Assert.That(typeof(ProjectileSpawnEvent).GetField("Count"), Is.Null);
            Assert.That(typeof(ProjectileSpawnEvent).GetField("SpreadDegrees"), Is.Null);
            Assert.That(typeof(ProjectileSpawnEvent).GetField("JitterDegrees"), Is.Null);
            Assert.That(typeof(ProjectileSpawnEvent).GetField("BaseDirection"), Is.Null);
            Assert.That(typeof(ProjectileSpawnEvent).GetField("Speed"), Is.Null);
            Assert.That(typeof(ImpactAoeSpawnEvent).GetField("TypeId"), Is.Null);
            Assert.That(typeof(ImpactAoeSpawnEvent).GetField("Lifetime"), Is.Null);
            Assert.That(typeof(ImpactAoeSpawnEvent).GetField("Count"), Is.Null);
            Assert.That(typeof(ImpactAoeSpawnEvent).GetField("HitPayload"), Is.Null);
            Assert.That(typeof(LingeringAoeSpawnEvent).GetField("TypeId"), Is.Null);
            Assert.That(typeof(LingeringAoeSpawnEvent).GetField("Lifetime"), Is.Null);
            Assert.That(typeof(LingeringAoeSpawnEvent).GetField("Count"), Is.Null);
            Assert.That(typeof(LingeringAoeSpawnEvent).GetField("HitPayload"), Is.Null);

            // Command-shaped templates carry the template fields.
            Assert.That(typeof(ProjectileSpawnCommand).GetField("Count"), Is.Not.Null);
            Assert.That(typeof(ProjectileSpawnCommand).GetField("Speed"), Is.Not.Null);
            Assert.That(typeof(AoeSpawnCommand).GetField("EchoCount"), Is.Not.Null);
            Assert.That(typeof(AoeSpawnCommand).GetField("ScatterRadius"), Is.Not.Null);
            Assert.That(typeof(AoeSpawnCommand).GetField("TypeId"), Is.Not.Null);

            // Old fat-event struct names must not exist.
            Assert.That(Type.GetType("PlayGround.System.Projectile.ProjectileSpawnCommandData, PlayGround.Runtime"), Is.Null);
            Assert.That(Type.GetType("PlayGround.System.Aoe.AoeSpawnCommandData, PlayGround.Runtime"), Is.Null);
        }

        [Test]
        public void DeterministicFanOut_UsesPreviousChildIdHash()
        {
            EnqueueEvent(MakeEvent(
                count: 3,
                baseProjectileId: 9999,
                jitterSeed: 123u,
                deterministicIdTickIndex: 2));

            Tick(0.01f);

            int[] ids = ActiveProjectileIds();
            Assert.That(ids, Does.Contain(ExpectedChildId(9999, 123, 2, 0)));
            Assert.That(ids, Does.Contain(ExpectedChildId(9999, 123, 2, 1)));
            Assert.That(ids, Does.Contain(ExpectedChildId(9999, 123, 2, 2)));
        }

        [Test]
        public void ParallelApply_ForcedOverflowKeepsDeterministicIdsAcrossRepeatedRuns()
        {
            var apply = testWorld.GetExistingSystemManaged<ProjectileSpawnApplySystem>();

            for (int run = 0; run < 3; run++)
            {
                CreateDisabledProjectileSlot(childSpawner: run % 2 == 0);
                int sourceId = 7000 + run * 100;
                EnqueueEvent(MakeEvent(
                    count: 4,
                    baseProjectileId: sourceId,
                    jitterSeed: 321u,
                    deterministicIdTickIndex: 9,
                    lifetime: 100f));

                Tick(0.01f);

                Assert.That(ReadInternalInt(apply, "LastReuseCount"), Is.EqualTo(1));
                Assert.That(ReadInternalInt(apply, "LastColdCreateCount"), Is.EqualTo(3));

                int[] ids = ActiveProjectileIds();
                for (int childIndex = 0; childIndex < 4; childIndex++)
                {
                    Assert.That(ids, Does.Contain(ExpectedChildId(sourceId, 321, 9, childIndex)));
                }
            }
        }

        [Test]
        public void RadialFanOut_Count4_ProducesFullCircleVelocities()
        {
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 4,
                speed: speed,
                jitterSeed: 5u,
                deterministicIdTickIndex: 1,
                spawnPatternType: ProjectileChildSpawnPatternType.Radial));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities.Length, Is.EqualTo(4));
            Assert.That(ContainsVelocity(velocities, new float2(speed, 0f)), Is.True);
            Assert.That(ContainsVelocity(velocities, new float2(0f, speed)), Is.True);
            Assert.That(ContainsVelocity(velocities, new float2(-speed, 0f)), Is.True);
            Assert.That(ContainsVelocity(velocities, new float2(0f, -speed)), Is.True);
        }

        [Test]
        public void ChildSpawn_TimedSpawnProjectile_ProducesChildWithHasTimedSpawnerZero()
        {
            CreateChildSpawnerEntity();

            Tick(0.1f);

            // parent + at least one child
            Assert.That(TotalProjectileCount(), Is.GreaterThanOrEqualTo(2));

            // child has timed-spawn component present but disabled.
            Assert.That(ActiveBasicProjectileCount(), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ChildSpawn_ZeroInterval_IsBoundedByLoopGuard()
        {
            CreateChildSpawnerEntity(intervalSeconds: 0f);

            Tick(1f);

            Assert.That(TotalProjectileCount(), Is.GreaterThan(1));
            Assert.That(TotalProjectileCount(), Is.LessThanOrEqualTo(257));
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

        [Test]
        public void BasicReuse_OverwritesRenderBatchId()
        {
            const int FirstRenderType = 10;
            const int SecondRenderType = 20;

            EnqueueEvent(MakeEvent(count: 1, lifetime: 0.001f, renderTypeId: FirstRenderType));
            Tick(0.01f);
            Tick(0.01f);
            Entity first = FirstProjectileEntity();
            Assert.That(entityManager.GetComponentData<CombatRenderKindId>(first).Value, Is.EqualTo(FirstRenderType));

            EnqueueEvent(MakeEvent(count: 1, lifetime: 10f, renderTypeId: SecondRenderType));
            Tick(0.01f);
            Entity reused = FirstProjectileEntity();

            Assert.That(reused, Is.EqualTo(first));
            Assert.That(entityManager.GetComponentData<CombatRenderKindId>(reused).Value, Is.EqualTo(SecondRenderType));
            Assert.That(TotalProjectileCount(), Is.EqualTo(1));
        }

        [Test]
        public void ChildSpawnerReuse_OverwritesRenderBatchId()
        {
            const int FirstRenderType = 30;
            const int SecondRenderType = 40;

            EnqueueEvent(MakeEvent(count: 1, lifetime: 0.001f, hasTimedSpawner: true, renderTypeId: FirstRenderType));
            Tick(0.01f);
            Tick(0.01f);
            Entity first = FirstProjectileEntity();
            Assert.That(entityManager.GetComponentData<CombatRenderKindId>(first).Value, Is.EqualTo(FirstRenderType));

            EnqueueEvent(MakeEvent(count: 1, lifetime: 10f, hasTimedSpawner: true, renderTypeId: SecondRenderType));
            Tick(0.01f);
            Entity reused = FirstProjectileEntity();

            Assert.That(reused, Is.EqualTo(first));
            Assert.That(entityManager.GetComponentData<CombatRenderKindId>(reused).Value, Is.EqualTo(SecondRenderType));
            Assert.That(TotalProjectileCount(), Is.EqualTo(1));
        }

        [Test]
        public void BasicApplyReusesDisabledChildSpawnerSlot()
        {
            Entity disabledChildSpawner = CreateDisabledProjectileSlot(childSpawner: true);

            EnqueueEvent(MakeEvent(count: 1, hasTimedSpawner: false));
            Tick(0.01f);

            Assert.That(entityManager.IsComponentEnabled<Active>(disabledChildSpawner), Is.True);
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(disabledChildSpawner), Is.False);
            Assert.That(ActiveBasicProjectileCount(), Is.EqualTo(1));
            Assert.That(TotalProjectileCount(), Is.EqualTo(1));
        }

        [Test]
        public void ChildSpawnerApplyReusesDisabledBasicSlot()
        {
            Entity disabledBasic = CreateDisabledProjectileSlot(childSpawner: false);

            EnqueueEvent(MakeEvent(count: 1, hasTimedSpawner: true));
            Tick(0.01f);

            Assert.That(entityManager.IsComponentEnabled<Active>(disabledBasic), Is.True);
            Assert.That(entityManager.IsComponentEnabled<TimedSpawnComponent>(disabledBasic), Is.True);
            Assert.That(ActiveChildSpawnerProjectileCount(), Is.EqualTo(1));
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

        private ProjectileSpawnEvent MakeEvent(
            int count = 1,
            float spreadDegrees = 0f,
            float2 position = default,
            float2 baseDirection = default,
            float speed = 5f,
            float lifetime = 10f,
            bool hasTimedSpawner = false,
            int renderTypeId = 1,
            int baseProjectileId = 1,
            uint jitterSeed = 0u,
            int deterministicIdTickIndex = 0,
            ProjectileChildSpawnPatternType spawnPatternType = ProjectileChildSpawnPatternType.Forward,
            float armSeconds = 0f,
            float timedIntervalSeconds = 1f)
        {
            if (math.lengthsq(baseDirection) < 0.0001f)
                baseDirection = new float2(1f, 0f);
            var template = new ProjectileSpawnCommand
            {
                TypeId = 1,
                RenderTypeId = renderTypeId,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                BaseDirection = baseDirection,
                Speed = speed,
                Count = count,
                SpreadDegrees = spreadDegrees,
                SpawnPatternType = spawnPatternType,
                Lifetime = lifetime,
                ArmSeconds = armSeconds,
                Radius = 0.25f,
                HalfExtents = float2.zero,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                }),
                TimedSpawn = hasTimedSpawner
                    ? new TimedSpawnComponent
                    {
                        ChildKind = IntervalChildKind.Projectile,
                        JitterSeed = 1,
                        IntervalSeconds = timedIntervalSeconds,
                        TemplateKey = childProjectileTemplateKey
                    }
                    : default
            };
            Unity.Entities.Hash128 key = SpawnTemplateHash.Of(in template);
            projectileTemplateMap.TryAdd(key, template);
            return new ProjectileSpawnEvent
            {
                Kind = IntervalChildKind.Projectile,
                TemplateKey = key,
                Position = position,
                AimDirection = baseDirection,
                Faction = CombatFaction.Player,
                SourceId = baseProjectileId,
                JitterSeed = jitterSeed,
                DeterministicIdTickIndex = deterministicIdTickIndex
            };
        }

        private Unity.Entities.Hash128 RegisterChildProjectileTemplate()
        {
            var template = new ProjectileSpawnCommand
            {
                TypeId = 1,
                RenderTypeId = 1,
                Count = 1,
                SpawnPatternType = ProjectileChildSpawnPatternType.Forward,
                Speed = 5f,
                Lifetime = 10f,
                Radius = 0.25f,
                HalfExtents = float2.zero,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    DirectDamageEnabled = true
                }),
                Render = new CombatRenderComponent
                {
                    RenderTypeId = 1,
                    AlignToVelocity = 1
                },
                Authoring = new CombatRenderAuthoring
                {
                    VisualScale = new float2(1f, 1f)
                }
            };
            Unity.Entities.Hash128 key = SpawnTemplateHash.Of(in template);
            projectileTemplateMap.TryAdd(key, template);
            return key;
        }

        private Entity ProjectileById(int projectileId)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                ProjectileIdentityComponent identity =
                    entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]);
                if (identity.ProjectileId == projectileId)
                {
                    return entities[i];
                }
            }

            Assert.Fail($"Projectile {projectileId} was not spawned.");
            return Entity.Null;
        }

        private Entity CreateDisabledProjectileSlot(bool childSpawner)
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
            entityManager.SetComponentEnabled<Active>(entity, false);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, false);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            entityManager.SetComponentEnabled<ProjectileTrackingComponent>(entity, false);
            entityManager.SetComponentEnabled<TimedSpawnComponent>(entity, childSpawner);
            return entity;
        }

        private void CreateChildSpawnerEntity(float intervalSeconds = 1f)
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
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            entityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = 9999,
                TypeId = 1
            });
            entityManager.SetComponentData(entity, new CombatRenderKindId { Value = 1 });
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
            entityManager.SetComponentData(entity, new TimedSpawnComponent
            {
                Faction = CombatFaction.Player,
                SourceId = 9999,
                ChildKind = IntervalChildKind.Projectile,
                JitterSeed = 9999,
                IntervalSeconds = intervalSeconds,
                TemplateKey = childProjectileTemplateKey
            });
            entityManager.SetComponentData(entity, new TimedSpawnStateComponent
            {
                CooldownRemaining = 0f,
                TickIndex = 0
            });
            entityManager.SetComponentEnabled<Active>(entity, true);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, true);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            entityManager.SetComponentEnabled<ProjectileTrackingComponent>(entity, false);
            entityManager.SetComponentEnabled<TimedSpawnComponent>(entity, true);
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

        private int ActiveBasicProjectileCount()
        {
            return ActiveProjectileCountWhereTimedSpawn(enabled: false);
        }

        private int ActiveChildSpawnerProjectileCount()
        {
            return ActiveProjectileCountWhereTimedSpawn(enabled: true);
        }

        private int ActiveProjectileCountWhereTimedSpawn(bool enabled)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<TimedSpawnComponent>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.IsComponentEnabled<TimedSpawnComponent>(entities[i]) == enabled)
                {
                    count++;
                }
            }

            return count;
        }

        private Entity FirstProjectileEntity()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileTag>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.GreaterThan(0), "No projectile entities found.");
            return entities[0];
        }

        private int[] ActiveProjectileIds()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            var ids = new int[entities.Length];
            for (int i = 0; i < entities.Length; i++)
                ids[i] = entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]).ProjectileId;
            return ids;
        }

        private float2[] ActiveProjectileVelocities()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            var velocities = new float2[entities.Length];
            for (int i = 0; i < entities.Length; i++)
                velocities[i] = entityManager.GetComponentData<CombatKinematicsComponent>(entities[i]).Velocity;
            return velocities;
        }

        private static bool ContainsVelocity(float2[] velocities, float2 expected)
        {
            for (int i = 0; i < velocities.Length; i++)
            {
                if (math.lengthsq(velocities[i] - expected) <= 0.001f)
                    return true;
            }

            return false;
        }

        private static int ExpectedChildId(int parentProjectileId, int jitterSeed, int tickIndex, int childIndex)
        {
            unchecked
            {
                int hash = parentProjectileId;
                hash = (hash * 397) ^ jitterSeed;
                hash = (hash * 397) ^ tickIndex;
                hash = (hash * 397) ^ childIndex;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }

        private static int ReadInternalInt(object target, string fieldName)
        {
            const global::System.Reflection.BindingFlags Flags =
                global::System.Reflection.BindingFlags.Instance |
                global::System.Reflection.BindingFlags.NonPublic;
            var field = target.GetType().GetField(fieldName, Flags);
            Assert.That(field, Is.Not.Null);
            return (int)field.GetValue(target);
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
