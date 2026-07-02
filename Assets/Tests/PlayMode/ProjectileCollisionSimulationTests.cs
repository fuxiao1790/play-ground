using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;
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
        private AoeSpawnExpansionSystem aoeExpansion;
        private Entity scopeEntity;
        private Entity projectileTemplateEntity;
        private Entity aoeTemplateEntity;
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
            aoeExpansion = testWorld.GetOrCreateSystemManaged<AoeSpawnExpansionSystem>();
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<StatusProcessSystem>());
            simGroup.AddSystemToUpdateList(projectileExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(aoeExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoePulseVfxSystem>());
            simGroup.SortSystems();
            testWorld.GetOrCreateSystemManaged<CombatApplyBridge>();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<AoeSpawnEvent>(scopeEntity);

            projectileTemplateEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(projectileTemplateEntity, new ProjectileSpawnTemplate
            {
                Map = new NativeHashMap<Hash128, ProjectileSpawnCommand>(16, Allocator.Persistent)
            });

            aoeTemplateEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(aoeTemplateEntity, new AoeSpawnTemplate
            {
                Map = new NativeHashMap<Hash128, AoeSpawnCommand>(16, Allocator.Persistent)
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld.IsCreated)
            {
                if (entityManager.Exists(projectileTemplateEntity))
                {
                    ProjectileSpawnTemplate t = entityManager.GetComponentData<ProjectileSpawnTemplate>(projectileTemplateEntity);
                    if (t.Map.IsCreated) t.Map.Dispose();
                }
                if (entityManager.Exists(aoeTemplateEntity))
                {
                    AoeSpawnTemplate t = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
                    if (t.Map.IsCreated) t.Map.Dispose();
                }
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
        public void SameFactionTargetIsNotHit()
        {
            AddTarget(float2.zero, 0.25f, CombatFaction.Player);
            Entity projectile = CreateProjectile(pierceRemaining: 0);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedHitCount(), Is.EqualTo(0));
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.True,
                "Player projectile must not hit a Player target — same-faction skip.");
        }

        [Test]
        public void ProjectileApplicatorProjectileDetonationQueuesNovaWithSummedContribution()
        {
            const float TotalDamage = 15f;
            const int ProjectileCount = 6;
            var detonationKey = new Hash128(0xBEEFu, 0xCAFEu, 0u, 0u);
            RegisterProjectileTemplate(detonationKey, new ProjectileSpawnCommand
            {
                TypeId = 42,
                Count = ProjectileCount,
                Speed = 5f
            });

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

            Assert.That(ProjectileCountByTypeId(42), Is.EqualTo(ProjectileCount));
        }

        [Test]
        public void StackDetonationNovaIsGatedFromReHittingDetonationTarget()
        {
            const int NovaTypeId = 55;
            const int NovaCount = 4;
            var detonationKey = new Hash128(0xBEEFu, 0xCAFEu, 0u, 0u);
            RegisterProjectileTemplate(detonationKey, new ProjectileSpawnCommand
            {
                TypeId = NovaTypeId,
                Count = NovaCount,
                Speed = 0f,
                SpawnPatternType = ProjectileChildSpawnPatternType.Radial,
                BaseDirection = new float2(1f, 0f),
                Radius = 1f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 1f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                })
            });

            AddTarget(float2.zero, 1f);
            CreateProjectile(
                pierceRemaining: 0,
                stackEffect: ProjectileStackEffect(
                    debuffKey: 900,
                    threshold: 1,
                    lifetime: 10f,
                    damage: 5f,
                    projectileCount: NovaCount));

            // Tick 1: applicator hits the target once, reaches threshold, and the nova
            // detonates and spawns on top of the target.
            TickSimulationOnly(0.001f);
            int hitsAfterTick1 = ReadFinalizedHitCount();
            int novaCount = ProjectileCountByTypeId(NovaTypeId);

            // Tick 2: the nova projectiles overlap the detonation target but must be gated
            // from instantly re-hitting it.
            TickSimulationOnly(0.001f);
            int hitsAfterTick2 = ReadFinalizedHitCount();

            Assert.That(novaCount, Is.EqualTo(NovaCount), "Detonation nova spawns on the target.");
            Assert.That(hitsAfterTick1, Is.EqualTo(1), "Applicator hits the target once.");
            Assert.That(hitsAfterTick2, Is.EqualTo(0),
                "Nova is gated from instantly re-hitting the detonation target it spawned on.");
        }

        [Test]
        public void ProjectileStackDetonationFansOutAsRadialNova()
        {
            const int NovaTypeId = 56;
            const int NovaCount = 4;
            var detonationKey = new Hash128(0xBEEFu, 0xCAFEu, 0u, 0u);
            RegisterProjectileTemplate(detonationKey, new ProjectileSpawnCommand
            {
                TypeId = NovaTypeId,
                Count = NovaCount,
                Speed = 5f,
                SpawnPatternType = ProjectileChildSpawnPatternType.Radial,
                Radius = 0.1f,
                ShapeType = CombatShapeType.Circle
            });

            AddTarget(float2.zero, 1f);
            CreateProjectile(
                pierceRemaining: 0,
                stackEffect: ProjectileStackEffect(
                    debuffKey: 910,
                    threshold: 1,
                    lifetime: 10f,
                    damage: 5f,
                    projectileCount: NovaCount));

            TickSimulationOnly(0.001f);

            float2[] velocities = ProjectileVelocitiesByTypeId(NovaTypeId);
            Assert.That(velocities.Length, Is.EqualTo(NovaCount), "Detonation spawns the full nova.");

            // Radial nova: directions must cover opposing sides on both axes, proving the
            // projectiles fan around the full circle instead of clustering in a forward cone.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (float2 v in velocities)
            {
                float2 d = math.normalizesafe(v, new float2(1f, 0f));
                minX = math.min(minX, d.x); maxX = math.max(maxX, d.x);
                minY = math.min(minY, d.y); maxY = math.max(maxY, d.y);
            }

            Assert.That(maxX, Is.GreaterThan(0.5f), "some projectile travels +x");
            Assert.That(minX, Is.LessThan(-0.5f), "some projectile travels -x");
            Assert.That(maxY, Is.GreaterThan(0.5f), "some projectile travels +y");
            Assert.That(minY, Is.LessThan(-0.5f), "some projectile travels -y");
        }

        [Test]
        public void ProjectileImpactAoeMaterializesFromRegistry()
        {
            const int AoeTypeId = 77;
            var aoeTemplate = new AoeSpawnCommand
            {
                TypeId = AoeTypeId,
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new CombatHitPayload { DamageAmount = 1f, DirectDamageEnabled = true },
                EchoCount = 1
            };
            var aoeKey = SpawnTemplateHash.Of(in aoeTemplate);
            RegisterAoeTemplate(aoeKey, aoeTemplate);

            AddTarget(float2.zero, 0.25f);
            CreateProjectile(
                pierceRemaining: 0,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Aoe, TemplateKey = aoeKey });

            TickSimulationOnly(0.01f);

            Assert.That(AoeEntityCount(), Is.EqualTo(1));
            Assert.That(AoeTypeIdOf(FirstAoeEntity()), Is.EqualTo(AoeTypeId));
        }

        [Test]
        public void ProjectileImpactProjectileMaterializesFromRegistry()
        {
            const int ChildTypeId = 5;
            var childTemplate = new ProjectileSpawnCommand
            {
                TypeId = ChildTypeId,
                Count = 1,
                PierceRemaining = 99,
                Speed = 5f,
                BaseDirection = new float2(1f, 0f),
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle
            };
            var childKey = SpawnTemplateHash.Of(in childTemplate);
            RegisterProjectileTemplate(childKey, childTemplate);

            AddTarget(float2.zero, 0.25f);
            CreateProjectile(
                pierceRemaining: 0,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Projectile, TemplateKey = childKey });

            TickSimulationOnly(0.01f);

            // Original projectile is disabled (pierce exhausted); child spawned from registry template.
            Assert.That(ProjectileCountByTypeId(ChildTypeId), Is.EqualTo(1));
        }

        [Test]
        public void ImpactSpawnContactGateSeedPreventsChildFromHittingSpawnTarget()
        {
            const int ChildTypeId = 9;
            var childTemplate = new ProjectileSpawnCommand
            {
                TypeId = ChildTypeId,
                Count = 1,
                PierceRemaining = 5,
                Speed = 5f,
                BaseDirection = new float2(1f, 0f),
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 2f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                })
            };
            var childKey = SpawnTemplateHash.Of(in childTemplate);
            RegisterProjectileTemplate(childKey, childTemplate);

            AddTarget(float2.zero, 0.25f);
            CreateProjectile(
                pierceRemaining: 0,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Projectile, TemplateKey = childKey });

            // Tick 1: parent hits target; child materializes with contact-gate seed blocking the same target.
            TickSimulationOnly(0.01f);
            int hitsAfterTick1 = ReadFinalizedHitCount();

            // Tick 2: child projectile exists but cannot re-hit the seeded target this tick.
            TickSimulationOnly(0.01f);
            int hitsAfterTick2 = ReadFinalizedHitCount();

            Assert.That(hitsAfterTick1, Is.EqualTo(1), "Parent hits target once.");
            Assert.That(hitsAfterTick2, Is.EqualTo(0), "Child is gated from immediately re-hitting the spawn target.");
        }

        private void TickSimulationOnly(float dt)
        {
            elapsedTime += dt;
            testWorld.SetTime(new TimeData(elapsedTime, dt));
            simGroup.Update();
        }

        private Entity CreateProjectile(
            int pierceRemaining,
            StackEffectSnapshot stackEffect = default,
            OnHitSpawnRef onHitSpawn = default)
        {
            Entity entity = entityManager.CreateEntity(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderBatchId),
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
            entityManager.SetComponentData(entity, new CombatRenderBatchId { Value = 1 });
            entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentData(entity, new ProjectileHitComponent
            {
                PierceRemaining = pierceRemaining,
                HitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = 1f,
                        CritMultiplier = 1f,
                        DirectDamageEnabled = true,
                        StackEffect = stackEffect
                    },
                    onHitSpawn: onHitSpawn)
            });

            return entity;
        }

        private Entity AddTarget(float2 position, float radius, CombatFaction faction = CombatFaction.Mob)
        {
            var target = new TestCombatTarget(++nextTargetId, position, radius);
            return CombatTargetProxy.Create(entityManager, target, faction);
        }

        // ---- Registry helpers ----

        private void RegisterProjectileTemplate(Unity.Entities.Hash128 key, ProjectileSpawnCommand template)
        {
            ProjectileSpawnTemplate registry = entityManager.GetComponentData<ProjectileSpawnTemplate>(projectileTemplateEntity);
            registry.Map.TryAdd(key, template);
        }

        private void RegisterAoeTemplate(Unity.Entities.Hash128 key, AoeSpawnCommand template)
        {
            AoeSpawnTemplate registry = entityManager.GetComponentData<AoeSpawnTemplate>(aoeTemplateEntity);
            registry.Map.TryAdd(key, template);
        }

        // ---- Query helpers ----

        private int AoeEntityCount()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeTag>());
            return q.CalculateEntityCount();
        }

        private Entity FirstAoeEntity()
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeTag>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
            return entities.Length > 0 ? entities[0] : Entity.Null;
        }

        private int AoeTypeIdOf(Entity entity)
        {
            if (entity == Entity.Null || !entityManager.HasComponent<AoeIdentityComponent>(entity))
                return -1;
            return entityManager.GetComponentData<AoeIdentityComponent>(entity).TypeId;
        }

        private int ProjectileCountByTypeId(int typeId)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<ProjectileIdentityComponent> identities =
                q.ToComponentDataArray<ProjectileIdentityComponent>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].TypeId == typeId)
                    count++;
            }
            return count;
        }

        private float2[] ProjectileVelocitiesByTypeId(int typeId)
        {
            using EntityQuery q = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>());
            using NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]).TypeId == typeId)
                    count++;
            }

            var velocities = new float2[count];
            int next = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]).TypeId == typeId)
                    velocities[next++] = entityManager.GetComponentData<CombatKinematicsComponent>(entities[i]).Velocity;
            }
            return velocities;
        }

        // ---- Stack-effect factory ----

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
                DetonationKind = StackDetonationKind.Projectile,
                DetonationKey = new Hash128(0xBEEFu, 0xCAFEu, 0u, 0u)
            };
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
