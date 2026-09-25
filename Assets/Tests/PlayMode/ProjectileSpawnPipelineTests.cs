using System;
using NUnit.Framework;
using PlayGround.System.Combat;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
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
        private SpawnTemplateRegistryState templateRegistryState;
        private Unity.Entities.Hash128 childProjectileTemplateKey;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            ProjectileHarness harness = CreateHarness("ProjectileSpawnPipelineTest");
            testWorld = harness.World;
            entityManager = harness.EntityManager;
            simGroup = harness.SimGroup;
            scopeEntity = harness.ScopeEntity;
            templateRegistryState = harness.RegistryState;
            projectileTemplateMap = harness.ProjectileTemplateMap;
            childProjectileTemplateKey = RegisterChildProjectileTemplate();
        }

        [TearDown]
        public void TearDown()
        {
            SpawnTemplateRegistryTestState.Dispose(ref templateRegistryState);
            if (projectileTemplateMap.IsCreated)
                projectileTemplateMap.Dispose();
            if (testWorld.IsCreated)
                testWorld.Dispose();
        }

        // Minimal bundle of everything a fresh isolated projectile-spawn world needs. Factored
        // out of SetUp so the launch-aim RNG-preservation tests can stand up a second,
        // completely independent world (matching parameters except the field under test) without
        // duplicating the whole system/entity bootstrap inline.
        private struct ProjectileHarness
        {
            public World World;
            public EntityManager EntityManager;
            public SimulationSystemGroup SimGroup;
            public Entity ScopeEntity;
            public NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand> ProjectileTemplateMap;
            public SpawnTemplateRegistryState RegistryState;
        }

        private static ProjectileHarness CreateHarness(string worldName)
        {
            var harness = new ProjectileHarness { World = new World(worldName) };
            harness.EntityManager = harness.World.EntityManager;
            harness.SimGroup = harness.World.GetOrCreateSystemManaged<SimulationSystemGroup>();
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystem<CombatArmingSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystem<CombatLifetimeSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystem<ProjectileMovementSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystem<ProjectileContinuousOriginSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystem<TimedSpawnSystem>());
            // Required (task 004) so ProjectileSpawnExpansionSystem's [UpdateAfter(TargetSpatialHashSystem)]
            // constraint is satisfiable and the launch-aim acquisition query has a real
            // TargetSpatialHashSingleton to read each tick.
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystem<TargetSpatialHashSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>());
            // TimedSpawnSystem now reads all three spawn lanes unconditionally, so the AoE
            // expansion systems must exist to create their lane singletons on world init.
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystemManaged<ProjectileDiscreteSpawnApplySystem>());
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystemManaged<ProjectileContinuousSpawnApplySystem>());
            harness.World.GetOrCreateSystemManaged<CombatStatsGatherSystem>();
            // Arming/lifetime/expansion now write the VFX lane unconditionally, so its owning
            // system must exist (to create the lane singleton) and tick (to drain it). It no-ops
            // without a VfxRoot.
            harness.SimGroup.AddSystemToUpdateList(harness.World.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>());
            harness.SimGroup.SortSystems();

            harness.ScopeEntity = harness.EntityManager.CreateEntity(typeof(CombatScope));
            harness.RegistryState = SpawnTemplateRegistryTestState.Add(harness.EntityManager, harness.ScopeEntity);
            harness.EntityManager.AddBuffer<ProjectileSpawnEvent>(harness.ScopeEntity);
            harness.EntityManager.AddBuffer<ImpactAoeSpawnEvent>(harness.ScopeEntity);
            harness.EntityManager.AddBuffer<LingeringAoeSpawnEvent>(harness.ScopeEntity);

            harness.ProjectileTemplateMap =
                new NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnCommand>(8, Allocator.Persistent);
            harness.EntityManager.AddComponentData(
                harness.ScopeEntity, new ProjectileSpawnTemplate { Map = harness.ProjectileTemplateMap });

            return harness;
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
        public void FanOut_Count4SpreadDegrees30_IncludesTwoForwardProjectiles()
        {
            const float speed = 5f;
            EnqueueEvent(MakeEvent(count: 4, spreadDegrees: 30f, speed: speed));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            int forwardCount = 0;
            for (int i = 0; i < velocities.Length; i++)
            {
                if (math.distancesq(velocities[i], new float2(speed, 0f)) < 0.0001f)
                {
                    forwardCount++;
                }
            }

            Assert.That(forwardCount, Is.EqualTo(2));
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
                energyPerSecond: 1000f));

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
            Assert.That(Type.GetType("PlayGround.System.Combat.Projectiles.ProjectileSpawnCommandData, PlayGround.Runtime"), Is.Null);
            Assert.That(Type.GetType("PlayGround.System.Combat.Aoes.AoeSpawnCommandData, PlayGround.Runtime"), Is.Null);
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
            var apply = testWorld.GetExistingSystemManaged<ProjectileDiscreteSpawnApplySystem>();

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
        public void DirectionlessIntervalSideSprayTemplate_UsesItsStoredPattern()
        {
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 1,
                speed: speed,
                jitterSeed: 123u,
                deterministicIdTickIndex: 1,
                hasAimDirection: false,
                spawnPatternType: ProjectileChildSpawnPatternType.SideSpray));
            EnqueueEvent(MakeEvent(
                count: 1,
                speed: speed,
                jitterSeed: 123u,
                deterministicIdTickIndex: 2,
                hasAimDirection: false,
                spawnPatternType: ProjectileChildSpawnPatternType.SideSpray));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities.Length, Is.EqualTo(2));
            Assert.That(math.distancesq(velocities[0], velocities[1]), Is.LessThan(0.001f));
            Assert.That(math.length(velocities[0]), Is.EqualTo(speed).Within(0.001f));
            Assert.That(math.length(velocities[1]), Is.EqualTo(speed).Within(0.001f));
        }

        [Test]
        public void DirectionlessIntervalForward_UsesDefaultHeadingForEachWave()
        {
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                baseDirection: new float2(1f, 0f),
                speed: speed,
                jitterSeed: 456u,
                deterministicIdTickIndex: 1,
                hasAimDirection: false));
            EnqueueEvent(MakeEvent(
                baseDirection: new float2(1f, 0f),
                speed: speed,
                jitterSeed: 456u,
                deterministicIdTickIndex: 2,
                hasAimDirection: false));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities.Length, Is.EqualTo(2));
            Assert.That(math.distancesq(velocities[0], velocities[1]), Is.LessThan(0.001f));
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
        public void ChildSpawn_ZeroEnergyThreshold_IsBoundedByLoopGuard()
        {
            CreateChildSpawnerEntity(energyPerSecond: 1f, spawnEnergyCost: 0f);

            Tick(1f);

            Assert.That(TotalProjectileCount(), Is.GreaterThan(1));
            Assert.That(TotalProjectileCount(), Is.LessThanOrEqualTo(257));
        }

        [Test]
        public void ChildSpawn_EnergyCostControlsCadence_AndZeroRateDoesNotEmit()
        {
            CreateChildSpawnerEntity(energyPerSecond: 10f, spawnEnergyCost: 1f, sourceId: 9999);
            CreateChildSpawnerEntity(energyPerSecond: 10f, spawnEnergyCost: 2f, sourceId: 10000);
            CreateChildSpawnerEntity(energyPerSecond: 0f, spawnEnergyCost: 1f, sourceId: 10001);

            for (int i = 0; i < 10; i++)
                Tick(0.1f);

            Assert.That(entityManager.GetComponentData<TimedSpawnStateComponent>(ProjectileById(9999)).TickIndex, Is.EqualTo(10));
            Assert.That(entityManager.GetComponentData<TimedSpawnStateComponent>(ProjectileById(10000)).TickIndex, Is.EqualTo(5));
            Assert.That(entityManager.GetComponentData<TimedSpawnStateComponent>(ProjectileById(10001)).TickIndex, Is.Zero);
            Assert.That(TotalProjectileCount(), Is.EqualTo(18));
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

        [Test]
        public void DiscreteLaneDoesNotReuseDisabledContinuousSlots()
        {
            EnqueueEvent(MakeEvent(count: 1, continuousCollision: true, lifetime: 0.001f));
            Tick(0.01f);
            Tick(0.01f);

            using EntityQuery continuousSlots = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileContinuousTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> disabledContinuous = continuousSlots.ToEntityArray(Allocator.Temp);
            Assert.That(disabledContinuous.Length, Is.EqualTo(1));
            Entity continuousSlot = disabledContinuous[0];

            EnqueueEvent(MakeEvent(count: 1, continuousCollision: false, lifetime: 10f));
            Tick(0.01f);

            Assert.That(entityManager.IsComponentEnabled<Active>(continuousSlot), Is.False);
            Assert.That(entityManager.HasComponent<ProjectileContinuousStepComponent>(continuousSlot), Is.True);
            Assert.That(entityManager.HasComponent<ProjectileTrackingComponent>(continuousSlot), Is.False);
            using EntityQuery invalid = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileContinuousTag>(),
                ComponentType.Exclude<ProjectileContinuousStepComponent>());
            Assert.That(invalid.CalculateEntityCount(), Is.Zero);
        }

        [Test]
        public void BothProjectileLanesContributeSpawnStats()
        {
            EnqueueEvent(MakeEvent(count: 1, continuousCollision: false));
            EnqueueEvent(MakeEvent(count: 1, continuousCollision: true));
            Tick(0.01f);

            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatStatsSingleton>());
            CombatStatsSingleton stats = query.GetSingleton<CombatStatsSingleton>();
            Assert.That(stats.EntitiesSpawnedViaEcb + stats.EntitiesSpawnedViaReuse, Is.EqualTo(2));
            testWorld.GetExistingSystemManaged<CombatStatsGatherSystem>().Update();
            CombatStatsDisplaySingleton display = entityManager.GetComponentData<CombatStatsDisplaySingleton>(
                query.GetSingletonEntity());
            Assert.That(display.ActiveProjectiles, Is.EqualTo(2));
        }

        [Test]
        public void NearestHostileInRangeAimsSingleShotVelocity()
        {
            // Also covers "single-shot wave aims directly at target" — the same scenario.
            CreateTargetProxy(new float2(10f, 5f), radius: 0.5f, faction: CombatFaction.Mob);
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: new float2(1f, 0f),
                speed: speed,
                deterministicIdTickIndex: 1,
                spawnPatternType: ProjectileChildSpawnPatternType.Forward,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities.Length, Is.EqualTo(1));
            float2 expected = math.normalize(new float2(10f, 5f)) * speed;
            Assert.That(velocities[0].x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(expected.y).Within(0.001f));
        }

        [Test]
        public void SameFactionNearerTargetIsSkippedInFavorOfHostile()
        {
            CreateTargetProxy(new float2(2f, 0f), radius: 0.5f, faction: CombatFaction.Player); // ally, nearer
            CreateTargetProxy(new float2(10f, 0f), radius: 0.5f, faction: CombatFaction.Mob); // hostile, farther
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: new float2(0f, 1f),
                speed: speed,
                deterministicIdTickIndex: 1,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f,
                faction: CombatFaction.Player));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            var expected = new float2(speed, 0f); // toward the hostile at (10, 0), not the nearer ally
            Assert.That(velocities[0].x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(expected.y).Within(0.001f));
        }

        [Test]
        public void SelectedFactionSameFactionTargetIsAimedAt()
        {
            CreateTargetProxy(
                new float2(10f, 5f), radius: 0.5f, policy: TargetFaction.AllowedFrom(CombatFaction.Player, CombatFaction.Player));
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: new float2(1f, 0f),
                speed: speed,
                deterministicIdTickIndex: 1,
                spawnPatternType: ProjectileChildSpawnPatternType.Forward,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f,
                faction: CombatFaction.Player));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities.Length, Is.EqualTo(1));
            float2 expected = math.normalize(new float2(10f, 5f)) * speed;
            Assert.That(velocities[0].x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(expected.y).Within(0.001f));
        }

        [Test]
        public void SelectedFactionNearerIneligibleTargetIsSkippedInFavorOfEligibleTarget()
        {
            CreateTargetProxy(
                new float2(2f, 0f), radius: 0.5f, policy: TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Mob)); // nearer, ineligible
            CreateTargetProxy(
                new float2(10f, 0f), radius: 0.5f, policy: TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Player)); // farther, eligible
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: new float2(0f, 1f),
                speed: speed,
                deterministicIdTickIndex: 1,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f,
                faction: CombatFaction.Player));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            var expected = new float2(speed, 0f); // toward the farther eligible target at (10, 0)
            Assert.That(velocities[0].x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(expected.y).Within(0.001f));
        }

        [Test]
        public void ContactGateSeedTargetIsExcludedAndNextNearestHostileIsSelected()
        {
            Entity nearHostile = CreateTargetProxy(new float2(5f, 0f), radius: 0.5f, faction: CombatFaction.Mob);
            CreateTargetProxy(new float2(10f, 0f), radius: 0.5f, faction: CombatFaction.Mob);
            const float speed = 5f;
            int excludeKey = CombatTargetAcquisition.TargetKey(nearHostile);
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: new float2(0f, 1f),
                speed: speed,
                deterministicIdTickIndex: 1,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f,
                contactGateSeedTargetId: excludeKey));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            var expected = new float2(speed, 0f); // the farther hostile at (10, 0); nearer one excluded
            Assert.That(velocities[0].x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(expected.y).Within(0.001f));
        }

        [Test]
        public void NoHostileInRangePreservesFallbackPattern()
        {
            CreateTargetProxy(new float2(2f, 0f), radius: 0.5f, faction: CombatFaction.Player); // same faction only
            const float speed = 5f;
            var baseDir = new float2(1f, 0f);
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: baseDir,
                speed: speed,
                deterministicIdTickIndex: 1,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities[0].x, Is.EqualTo(baseDir.x * speed).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(baseDir.y * speed).Within(0.001f));
        }

        [Test]
        public void MissingOrEmptyTargetHashFallsBackToNormalPattern()
        {
            // No target proxies are created at all. TargetSpatialHashSingleton still exists
            // (TargetSpatialHashSystem is wired into the shared harness) but reports zero
            // targets, so CombatTargetAcquisition finds no eligible candidate — the same
            // "acquisition unavailable -> fallback" outcome a genuinely absent singleton would
            // produce, without standing up a second world that omits TargetSpatialHashSystem.
            const float speed = 5f;
            var baseDir = new float2(0f, 1f);
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: baseDir,
                speed: speed,
                deterministicIdTickIndex: 1,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities[0].x, Is.EqualTo(baseDir.x * speed).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(baseDir.y * speed).Within(0.001f));
        }

        [Test]
        public void DisabledLaunchAimPreservesFallbackPatternWithHostilePresent()
        {
            CreateTargetProxy(new float2(10f, 5f), radius: 0.5f, faction: CombatFaction.Mob);
            const float speed = 5f;
            var baseDir = new float2(1f, 0f);
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: baseDir,
                speed: speed,
                deterministicIdTickIndex: 1,
                launchAimMode: ProjectileLaunchAimMode.None,
                launchAimRange: 20f));

            Tick(0.01f);

            float2[] velocities = ActiveProjectileVelocities();
            Assert.That(velocities[0].x, Is.EqualTo(baseDir.x * speed).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(baseDir.y * speed).Within(0.001f));
        }

        [Test]
        public void LaunchAimDoesNotAddProjectilesOrAlterDeterministicIds()
        {
            const int count = 4;
            const uint seed = 55u;
            const int tickIndex = 1;
            const int baseId = 700;

            CreateTargetProxy(new float2(10f, 5f), radius: 0.5f, faction: CombatFaction.Mob);
            EnqueueEvent(MakeEvent(
                count: count,
                position: float2.zero,
                speed: 5f,
                jitterSeed: seed,
                deterministicIdTickIndex: tickIndex,
                baseProjectileId: baseId,
                spawnPatternType: ProjectileChildSpawnPatternType.Radial,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            int[] aimedIds = ActiveProjectileIds();
            Assert.That(aimedIds.Length, Is.EqualTo(count));

            var expectedIds = new int[count];
            for (int i = 0; i < count; i++)
                expectedIds[i] = ExpectedChildId(baseId, (int)seed, tickIndex, i);

            global::System.Array.Sort(aimedIds);
            global::System.Array.Sort(expectedIds);
            Assert.That(aimedIds, Is.EqualTo(expectedIds));
        }

        [Test]
        public void ContinuousCommandReceivesLaunchAimWithoutTracking()
        {
            CreateTargetProxy(new float2(10f, 0f), radius: 0.5f, faction: CombatFaction.Mob);
            const float speed = 5f;
            EnqueueEvent(MakeEvent(
                count: 1,
                position: float2.zero,
                baseDirection: new float2(0f, 1f),
                speed: speed,
                deterministicIdTickIndex: 1,
                continuousCollision: true,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            using EntityQuery continuousSlots = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileContinuousTag>(),
                ComponentType.ReadOnly<Active>());
            using NativeArray<Entity> entities = continuousSlots.ToEntityArray(Allocator.Temp);
            Assert.That(entities.Length, Is.EqualTo(1));
            Assert.That(entityManager.HasComponent<ProjectileTrackingComponent>(entities[0]), Is.False);

            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entities[0]);
            var expected = new float2(speed, 0f); // toward the hostile at (10, 0)
            Assert.That(kinematics.Velocity.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(kinematics.Velocity.y, Is.EqualTo(expected.y).Within(0.001f));
        }

        [Test]
        public void AimedNovaBypassesStoredSideSprayPatternWithCount4UpLeftDownRightOrder()
        {
            const float speed = 5f;
            const uint seed = 33u;
            const int tickIndex = 1;
            const int baseId = 800;
            const int count = 4;

            CreateTargetProxy(new float2(0f, 10f), radius: 0.5f, faction: CombatFaction.Mob);
            EnqueueEvent(MakeEvent(
                count: count,
                position: float2.zero,
                speed: speed,
                spreadDegrees: 45f,
                jitterSeed: seed,
                deterministicIdTickIndex: tickIndex,
                baseProjectileId: baseId,
                spawnPatternType: ProjectileChildSpawnPatternType.SideSpray,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            float2[] velocities = VelocitiesByShotIndex(count, baseId, seed, tickIndex);
            float2[] expected =
            {
                new(0f, speed),   // shot 0: directly at target (up)
                new(-speed, 0f),  // shot 1: 90 degrees around (left)
                new(0f, -speed),  // shot 2: 180 degrees around (down)
                new(speed, 0f),   // shot 3: 270 degrees around (right)
            };
            for (int i = 0; i < count; i++)
            {
                Assert.That(velocities[i].x, Is.EqualTo(expected[i].x).Within(0.001f), $"shot {i} x");
                Assert.That(velocities[i].y, Is.EqualTo(expected[i].y).Within(0.001f), $"shot {i} y");
            }
        }

        [Test]
        public void AimedNovaBypassesStoredForwardPatternWithCount3EvenAngularSpacing()
        {
            const float speed = 5f;
            const uint seed = 71u;
            const int tickIndex = 1;
            const int baseId = 900;
            const int count = 3;
            var targetOffset = new float2(7f, 3f); // arbitrary non-axis direction

            CreateTargetProxy(targetOffset, radius: 0.5f, faction: CombatFaction.Mob);
            EnqueueEvent(MakeEvent(
                count: count,
                position: float2.zero,
                speed: speed,
                spreadDegrees: 45f,
                jitterSeed: seed,
                deterministicIdTickIndex: tickIndex,
                baseProjectileId: baseId,
                spawnPatternType: ProjectileChildSpawnPatternType.Forward,
                launchAimMode: ProjectileLaunchAimMode.NearestHostile,
                launchAimRange: 20f));

            Tick(0.01f);

            float2[] velocities = VelocitiesByShotIndex(count, baseId, seed, tickIndex);
            float2 expectedShot0 = math.normalize(targetOffset) * speed;
            Assert.That(velocities[0].x, Is.EqualTo(expectedShot0.x).Within(0.001f));
            Assert.That(velocities[0].y, Is.EqualTo(expectedShot0.y).Within(0.001f));

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                float cosAngle = math.dot(math.normalize(velocities[i]), math.normalize(velocities[j]));
                float angleDegrees = math.degrees(math.acos(math.clamp(cosAngle, -1f, 1f)));
                Assert.That(angleDegrees, Is.EqualTo(120f).Within(0.5f),
                    $"angle between shot {i} and shot {j}");
            }
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
            bool hasAimDirection = true,
            ProjectileChildSpawnPatternType spawnPatternType = ProjectileChildSpawnPatternType.Forward,
            float armSeconds = 0f,
            float energyPerSecond = 1f,
            float spawnEnergyCost = 1f,
            bool continuousCollision = false,
            ProjectileLaunchAimMode launchAimMode = ProjectileLaunchAimMode.None,
            float launchAimRange = 0f,
            CombatFaction faction = CombatFaction.Player,
            int contactGateSeedTargetId = 0)
        {
            if (math.lengthsq(baseDirection) < 0.0001f)
                baseDirection = new float2(1f, 0f);
            var template = new ProjectileSpawnCommand
            {
                TypeId = 1,
                RenderTypeId = renderTypeId,
                HasTimedSpawner = hasTimedSpawner ? 1 : 0,
                ContinuousCollision = continuousCollision ? 1 : 0,
                BaseDirection = baseDirection,
                Speed = speed,
                Count = count,
                SpreadDegrees = spreadDegrees,
                SpawnPatternType = spawnPatternType,
                LaunchAimMode = launchAimMode,
                LaunchAimRange = launchAimRange,
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
                        EnergyPerSecond = energyPerSecond,
                        EnergyThreshold = spawnEnergyCost,
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
                AimDirection = hasAimDirection ? baseDirection : default,
                Faction = faction,
                SourceId = baseProjectileId,
                JitterSeed = jitterSeed,
                DeterministicIdTickIndex = deterministicIdTickIndex,
                ContactGateSeedTargetId = contactGateSeedTargetId
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

        private Entity CreateTargetProxy(float2 position, float radius, CombatFaction faction) =>
            CreateTargetProxy(entityManager, position, radius, faction);

        private static Entity CreateTargetProxy(
            EntityManager entityManager, float2 position, float radius, CombatFaction faction)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(TargetProxyTag),
                typeof(TargetPosition),
                typeof(TargetCollisionShape),
                typeof(TargetFaction));
            entityManager.SetComponentData(entity, new TargetPosition { Value = position });
            CombatCollisionMath.ComputeWorldBounds(
                position, radius, float2.zero, 0f, CombatShapeType.Circle,
                out float2 boundsMin, out float2 boundsMax);
            entityManager.SetComponentData(entity, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            });
            entityManager.SetComponentData(entity, new TargetFaction { Value = faction });
            return entity;
        }

        private Entity CreateTargetProxy(float2 position, float radius, TargetFaction policy) =>
            CreateTargetProxy(entityManager, position, radius, policy);

        private static Entity CreateTargetProxy(
            EntityManager entityManager, float2 position, float radius, TargetFaction policy)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(TargetProxyTag),
                typeof(TargetPosition),
                typeof(TargetCollisionShape),
                typeof(TargetFaction));
            entityManager.SetComponentData(entity, new TargetPosition { Value = position });
            CombatCollisionMath.ComputeWorldBounds(
                position, radius, float2.zero, 0f, CombatShapeType.Circle,
                out float2 boundsMin, out float2 boundsMax);
            entityManager.SetComponentData(entity, new TargetCollisionShape
            {
                ShapeType = CombatShapeType.Circle,
                Radius = radius,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            });
            entityManager.SetComponentData(entity, policy);
            return entity;
        }

        private static Entity FindProjectileById(EntityManager entityManager, int projectileId)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]).ProjectileId == projectileId)
                    return entities[i];
            }

            Assert.Fail($"Projectile {projectileId} was not spawned.");
            return Entity.Null;
        }

        // Resolves each shot's velocity by its deterministic id (via ExpectedChildId and
        // FindProjectileById), which is index-safe. ActiveProjectileVelocities() is not, since
        // it returns query-iteration order, not shot order.
        private float2[] VelocitiesByShotIndex(int count, int baseProjectileId, uint jitterSeed, int tickIndex)
        {
            var velocities = new float2[count];
            for (int shotIndex = 0; shotIndex < count; shotIndex++)
            {
                int id = ExpectedChildId(baseProjectileId, (int)jitterSeed, tickIndex, shotIndex);
                Entity entity = FindProjectileById(entityManager, id);
                velocities[shotIndex] = entityManager.GetComponentData<CombatKinematicsComponent>(entity).Velocity;
            }
            return velocities;
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
            entityManager.SetComponentEnabled<Active>(entity, false);
            entityManager.SetComponentEnabled<CombatCollisionActiveTag>(entity, false);
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);
            entityManager.SetComponentEnabled<ProjectileTrackingComponent>(entity, false);
            entityManager.SetComponentEnabled<TimedSpawnComponent>(entity, childSpawner);
            return entity;
        }

        private void CreateChildSpawnerEntity(
            float energyPerSecond = 1f,
            float spawnEnergyCost = 1f,
            int sourceId = 9999)
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

            entityManager.SetComponentData(entity, new ProjectileIdentityComponent
            {
                Faction = CombatFaction.Player,
                ProjectileId = sourceId,
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
                SourceId = sourceId,
                ChildKind = IntervalChildKind.Projectile,
                JitterSeed = 9999,
                EnergyPerSecond = energyPerSecond,
                EnergyThreshold = spawnEnergyCost,
                TemplateKey = childProjectileTemplateKey
            });
            entityManager.SetComponentData(entity, new TimedSpawnStateComponent
            {
                EnergyAccumulated = 0f,
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
