using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
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
        private ImpactAoeSpawnExpansionSystem impactAoeExpansion;
        private LingeringAoeSpawnExpansionSystem lingeringAoeExpansion;
        private ExternalSpawnGateSystem externalSpawnGate;
        private TargetProxyCreateApplySystem targetProxyCreateApply;
        private TargetProxyUpdateApplySystem targetProxyUpdateApply;
        private Entity scopeEntity;
        private SpawnTemplateRegistryState templateRegistryState;
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
            impactAoeExpansion = testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnExpansionSystem>();
            lingeringAoeExpansion = testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnExpansionSystem>();
            externalSpawnGate = testWorld.GetOrCreateSystemManaged<ExternalSpawnGateSystem>();
            targetProxyCreateApply = testWorld.GetOrCreateSystemManaged<TargetProxyCreateApplySystem>();
            targetProxyUpdateApply = testWorld.GetOrCreateSystemManaged<TargetProxyUpdateApplySystem>();
            simGroup.AddSystemToUpdateList(targetProxyCreateApply);
            simGroup.AddSystemToUpdateList(targetProxyUpdateApply);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileContactGateSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<TargetSpatialHashSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ProjectileDiscreteCollisionSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatApplyFinalizeSingleSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<ResourceRegenSystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<StatusProcessSystem>());
            simGroup.AddSystemToUpdateList(projectileExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ProjectileDiscreteSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(impactAoeExpansion);
            simGroup.AddSystemToUpdateList(lingeringAoeExpansion);
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<ImpactAoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<LingeringAoeSpawnApplySystem>());
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystem<AoePulseVfxSystem>());
            // Producers now write the VFX lane unconditionally, so its owning system must exist
            // (to create the lane singleton) and tick (to drain it). It no-ops without a VfxRoot.
            simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<CombatAoeVfxDispatchSystem>());
            simGroup.SortSystems();
            testWorld.GetOrCreateSystemManaged<CombatApplyBridge>();

            scopeEntity = entityManager.CreateEntity(typeof(CombatScope));
            templateRegistryState = SpawnTemplateRegistryTestState.Add(entityManager, scopeEntity);
            entityManager.AddBuffer<ExternalSpawnRequest>(scopeEntity);
            entityManager.AddBuffer<ProjectileSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<ImpactAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<LingeringAoeSpawnEvent>(scopeEntity);
            entityManager.AddBuffer<TargetProxyCreateEvent>(scopeEntity);
            entityManager.AddBuffer<TargetProxyUpdateEvent>(scopeEntity);
            entityManager.AddBuffer<TargetProxyDeleteEvent>(scopeEntity);

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

        [Test]
        public void CombatTargetProxySeedsManaFromCombatTarget()
        {
            var target = new TestCombatTarget(++nextTargetId, float2.zero, 1f, 50f, 12f, 3f);
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Player), Is.True);
            targetProxyCreateApply.Update();
            Entity proxy = target.CombatTargetProxy;

            Mana mana = entityManager.GetComponentData<Mana>(proxy);
            Assert.That(mana.Max, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(mana.Current, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(mana.RegenPerSecond, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void CombatTargetProxyPushResourceMaxesPreservesEcsCurrent()
        {
            var target = new TestCombatTarget(++nextTargetId, float2.zero, 1f, 50f, 12f, 3f);
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Player), Is.True);
            targetProxyCreateApply.Update();
            Entity proxy = target.CombatTargetProxy;
            entityManager.SetComponentData(proxy, new Mana { Current = 7f, Max = 50f, RegenPerSecond = 3f });

            target.SetManaValues(80f, 5f);
            Assert.That(CombatTargetProxy.PushResourceMaxes(entityManager, proxy, target), Is.True);
            targetProxyUpdateApply.Update();

            Mana mana = entityManager.GetComponentData<Mana>(proxy);
            Assert.That(mana.Current, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(mana.Max, Is.EqualTo(80f).Within(0.0001f));
            Assert.That(mana.RegenPerSecond, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void ResourceRegenRaisesManaAndClampsAtMax()
        {
            var target = new TestCombatTarget(++nextTargetId, float2.zero, 1f, 10f, 5f, 4f);
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Player), Is.True);
            targetProxyCreateApply.Update();
            Entity proxy = target.CombatTargetProxy;

            TickSimulationOnly(1f);
            Assert.That(entityManager.GetComponentData<Mana>(proxy).Current, Is.EqualTo(9f).Within(0.0001f));

            TickSimulationOnly(1f);
            Assert.That(entityManager.GetComponentData<Mana>(proxy).Current, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void ResourceRegenDoesNotReviveDepletedHealth()
        {
            var target = new TestCombatTarget(++nextTargetId, float2.zero, 1f, healthCurrent: 0f, healthRegenPerSecond: 4f);
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Player), Is.True);
            targetProxyCreateApply.Update();
            Entity proxy = target.CombatTargetProxy;

            TickSimulationOnly(1f);
            Assert.That(entityManager.GetComponentData<Health>(proxy).Current, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ResourceRegenRunsAfterCombatDamage()
        {
            var target = new TestCombatTarget(++nextTargetId, float2.zero, 0.25f, healthCurrent: 5f, healthRegenPerSecond: 2f);
            Assert.That(CombatTargetProxy.Create(entityManager, target, CombatFaction.Player), Is.True);
            targetProxyCreateApply.Update();
            Entity proxy = target.CombatTargetProxy;
            CreateProjectile(pierceRemaining: 0);

            TickSimulationOnly(0.5f);

            Assert.That(entityManager.GetComponentData<Health>(proxy).Current, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void ExternalSpawnGateSpendsOnlyTheRequestCastersMana()
        {
            Entity firstCaster = entityManager.CreateEntity(typeof(Mana));
            Entity secondCaster = entityManager.CreateEntity(typeof(Mana));
            entityManager.SetComponentData(firstCaster, new Mana { Current = 10f, Max = 10f });
            entityManager.SetComponentData(secondCaster, new Mana { Current = 3f, Max = 3f });
            DynamicBuffer<ExternalSpawnRequest> requests = entityManager.GetBuffer<ExternalSpawnRequest>(scopeEntity);
            requests.Add(ExternalProjectileRequest(firstCaster, 4f, 100));
            requests.Add(ExternalProjectileRequest(secondCaster, 2f, 101));

            externalSpawnGate.Update();

            Assert.That(entityManager.GetComponentData<Mana>(firstCaster).Current, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(entityManager.GetComponentData<Mana>(secondCaster).Current, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(entityManager.GetBuffer<ProjectileSpawnEvent>(scopeEntity).Length, Is.EqualTo(2));
            Assert.That(ReadSpawnRejections(), Is.Empty);
        }

        [Test]
        public void ExternalSpawnGateRejectsWithoutSpendingAndAcceptsMissingMana()
        {
            Entity caster = entityManager.CreateEntity(typeof(Mana));
            Entity missingManaCaster = entityManager.CreateEntity();
            entityManager.SetComponentData(caster, new Mana { Current = 1f, Max = 10f });
            DynamicBuffer<ExternalSpawnRequest> requests = entityManager.GetBuffer<ExternalSpawnRequest>(scopeEntity);
            requests.Add(ExternalProjectileRequest(caster, 2f, 200));
            requests.Add(ExternalProjectileRequest(missingManaCaster, 9f, 201));

            externalSpawnGate.Update();

            Assert.That(entityManager.GetComponentData<Mana>(caster).Current, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(entityManager.GetBuffer<ProjectileSpawnEvent>(scopeEntity).Length, Is.EqualTo(1));
            SpawnRejectedEvent[] rejections = ReadSpawnRejections();
            Assert.That(rejections, Has.Length.EqualTo(1));
            Assert.That(rejections[0].Caster, Is.EqualTo(caster));
            Assert.That(rejections[0].CastToken, Is.EqualTo(200));
        }

        [TearDown]
        public void TearDown()
        {
            SpawnTemplateRegistryTestState.Dispose(ref templateRegistryState);
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
            Assert.That(entityManager.GetComponentData<Health>(target).Current, Is.EqualTo(TestTargetHealth - 1f).Within(0.0001f));
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
                "Player projectile must not hit a Player target �?same-faction skip.");
        }

        [Test]
        public void SelectedFactionSameFactionTargetIsHit()
        {
            AddTarget(float2.zero, 0.25f, TargetFaction.AllowedFrom(CombatFaction.Player, CombatFaction.Player));
            Entity projectile = CreateProjectile(pierceRemaining: 0);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedHitCount(), Is.EqualTo(1),
                "AllowedFactionOnly must accept an attacker faction equal to the target's own faction.");
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.False);
        }

        [Test]
        public void SelectedFactionUnselectedAttackerIsNotHit()
        {
            AddTarget(float2.zero, 0.25f, TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Mob));
            Entity projectile = CreateProjectile(pierceRemaining: 0);

            TickSimulationOnly(0.01f);

            Assert.That(ReadFinalizedHitCount(), Is.EqualTo(0),
                "Player projectile must not hit a target whose AllowedFactionOnly policy excludes Player.");
            Assert.That(entityManager.IsComponentEnabled<Active>(projectile), Is.True);
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
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.ImpactAoe, TemplateKey = aoeKey });

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
        public void ImpactProjectileBurstUsesIntervalSideSpray()
        {
            const int ChildTypeId = 8;
            const float Speed = 5f;
            var childTemplate = new ProjectileSpawnCommand
            {
                TypeId = ChildTypeId,
                Count = 4,
                SpreadDegrees = 0f,
                SpawnPatternType = ProjectileChildSpawnPatternType.SideSpray,
                Speed = Speed,
                BaseDirection = new float2(1f, 0f),
                Radius = 0.5f,
                ShapeType = CombatShapeType.Circle
            };
            var childKey = SpawnTemplateHash.Of(in childTemplate);
            RegisterProjectileTemplate(childKey, childTemplate);

            // The parent hits to its right. With a zero side spread, the child burst must
            // travel straight up/down from the impact, rather than forward/backward.
            AddTarget(new float2(1f, 0f), 0.25f);
            CreateProjectile(
                pierceRemaining: 0,
                onHitSpawn: new OnHitSpawnRef { Kind = IntervalChildKind.Projectile, TemplateKey = childKey });

            TickSimulationOnly(0.01f);

            float2[] velocities = ProjectileVelocitiesByTypeId(ChildTypeId);
            Assert.That(velocities.Length, Is.EqualTo(4));
            int upward = 0;
            int downward = 0;
            for (int i = 0; i < velocities.Length; i++)
            {
                Assert.That(math.abs(velocities[i].x), Is.LessThan(0.0001f));
                Assert.That(math.abs(math.abs(velocities[i].y) - Speed), Is.LessThan(0.0001f));
                if (velocities[i].y > 0f)
                    upward++;
                else
                    downward++;
            }

            Assert.That(upward, Is.EqualTo(2));
            Assert.That(downward, Is.EqualTo(2));
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

        private ExternalSpawnRequest ExternalProjectileRequest(Entity caster, float manaCost, int castToken)
        {
            return new ExternalSpawnRequest
            {
                Kind = IntervalChildKind.Projectile,
                TemplateKey = new Hash128(1u, 2u, 3u, (uint)castToken),
                Caster = caster,
                ManaCost = manaCost,
                Position = float2.zero,
                AimDirection = new float2(1f, 0f),
                Faction = CombatFaction.Player,
                SourceId = castToken,
                JitterSeed = (uint)castToken,
                CastToken = castToken
            };
        }

        private SpawnRejectedEvent[] ReadSpawnRejections()
        {
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SpawnRejectedSingleton>());
            SpawnRejectedSingleton lane = query.GetSingleton<SpawnRejectedSingleton>();
            var copy = new SpawnRejectedEvent[lane.Events.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = lane.Events[i];
            }

            return copy;
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
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(CombatHitPayload),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
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
                RenderTypeId = 1
            });
            entityManager.SetComponentData(entity, new CombatRenderAuthoring
            {
                VisualScale = new float2(1f, 1f)
            });
            entityManager.SetComponentData(entity, new CombatRenderKindId { Value = 1 });
            entityManager.SetComponentData(entity, new CombatLifetimeComponent { Remaining = 10f });
            entityManager.SetComponentData(entity, new ProjectileHitComponent
            {
                PierceRemaining = pierceRemaining,
                OnHitSpawn = onHitSpawn
            });
            entityManager.SetComponentData(entity, new CombatHitPayload
            {
                DamageAmount = 1f,
                CritMultiplier = 1f,
                DirectDamageEnabled = true,
                StackEffect = stackEffect
            });
            entityManager.SetComponentEnabled<ArmingTag>(entity, false);

            return entity;
        }

        private Entity AddTarget(float2 position, float radius, CombatFaction faction = CombatFaction.Mob)
        {
            var target = new TestCombatTarget(++nextTargetId, position, radius);
            Assert.That(CombatTargetProxy.Create(entityManager, target, faction), Is.True);
            targetProxyCreateApply.Update();
            return target.CombatTargetProxy;
        }

        private Entity AddTarget(float2 position, float radius, TargetFaction policy)
        {
            var target = new TestCombatTarget(++nextTargetId, position, radius);
            Assert.That(CombatTargetProxy.Create(entityManager, target, policy), Is.True);
            targetProxyCreateApply.Update();
            return target.CombatTargetProxy;
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
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatApplyResultSingleton>());
            if (query.IsEmptyIgnoreFilter)
            {
                return global::System.Array.Empty<CombatTickResult>();
            }

            CombatApplyResultSingleton lane = query.GetSingleton<CombatApplyResultSingleton>();
            lane.ProducerHandle.Complete();
            NativeList<CombatTickResult> results = lane.Results;
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

            private float maxMana;
            private float currentMana;
            private float manaRegenPerSecond;
            private readonly float maxHealth;
            private readonly float currentHealth;
            private readonly float healthRegenPerSecond;

            public TestCombatTarget(
                int targetId,
                float2 position,
                float radius,
                float maxMana = 0f,
                float currentMana = 0f,
                float manaRegenPerSecond = 0f,
                float maxHealth = TestTargetHealth,
                float healthCurrent = TestTargetHealth,
                float healthRegenPerSecond = 0f)
            {
                TargetId = targetId;
                this.position = position;
                this.radius = radius;
                this.maxMana = maxMana;
                this.currentMana = currentMana;
                this.manaRegenPerSecond = manaRegenPerSecond;
                this.maxHealth = maxHealth;
                this.currentHealth = healthCurrent;
                this.healthRegenPerSecond = healthRegenPerSecond;
            }

            public int TargetId { get; }
            public Entity CombatTargetProxy { get; set; }
            public Vector2 CombatTargetPosition => new(position.x, position.y);
            public float CombatTargetRadius => radius;
            public Vector2 CombatTargetHalfExtents => Vector2.zero;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => ~0;
            public float CombatMaxHealth => maxHealth;
            public float CombatCurrentHealth => currentHealth;
            public float CombatHealthRegenPerSecond => healthRegenPerSecond;
            public float CombatMaxMana => maxMana;
            public float CombatCurrentMana => currentMana;
            public float CombatManaRegenPerSecond => manaRegenPerSecond;
            public void SetManaValues(float max, float regenPerSecond)
            {
                maxMana = max;
                manaRegenPerSecond = regenPerSecond;
            }
            public bool IsCombatTargetActive => true;
            public void ReceiveHit(in CombatHitData hit)
            {
            }
        }
    }
}
