using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using UnityEditor;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.Tests.PlayMode
{
    public sealed class TargetedSkillPlayModeTests
    {
        private static int nextTargetId = 50_000;
        private static readonly List<int> hitOrder = new();

        [UnityTest]
        public IEnumerator BasicCast_DamagesTargetWithoutCreatingProjectileOrAoe()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            int projectileCountBefore = Count<ProjectileTag>(entityManager);
            int aoeCountBefore = Count<AoeTag>(entityManager);

            Spawn(root, damage: 5f, acquireRadius: 2f, chainRadius: 2f, maxTargets: 1);
            yield return null;
            yield return null;

            Assert.That(target.TotalDamage, Is.EqualTo(5f).Within(0.001f));
            Assert.That(Count<ProjectileTag>(entityManager), Is.EqualTo(projectileCountBefore));
            Assert.That(Count<AoeTag>(entityManager), Is.EqualTo(aoeCountBefore));
            yield return Cleanup(root, target);
        }

        [UnityTest]
        public IEnumerator ChainAcrossThreeTargets_AppliesPerLinkFalloff()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            TargetProbe third = CreateTarget(new Vector2(2.75f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);
            root.TargetRegistry.Register(third);

            Spawn(root, damage: 8f, acquireRadius: 2f, chainRadius: 1.5f, maxTargets: 3, falloff: 0.5f);
            yield return null;
            yield return null;

            Assert.That(first.TotalDamage, Is.EqualTo(8f).Within(0.001f));
            Assert.That(second.TotalDamage, Is.EqualTo(4f).Within(0.001f));
            Assert.That(third.TotalDamage, Is.EqualTo(2f).Within(0.001f));
            yield return Cleanup(root, first, second, third);
        }

        [UnityTest]
        public IEnumerator StaggeredChain_AppliesEachLinkOnSeparateFrame()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            TargetProbe third = CreateTarget(new Vector2(2.75f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);
            root.TargetRegistry.Register(third);

            Spawn(
                root,
                damage: 8f,
                acquireRadius: 2f,
                chainRadius: 1.5f,
                maxTargets: 3,
                falloff: 0.5f,
                chainDelay: 0.2f);

            yield return WaitUntilDamaged(first);

            Assert.That(first.TotalDamage, Is.EqualTo(8f).Within(0.001f));
            Assert.That(second.TotalDamage, Is.Zero);
            Assert.That(third.TotalDamage, Is.Zero);

            yield return WaitUntilDamaged(second);

            Assert.That(second.TotalDamage, Is.EqualTo(4f).Within(0.001f));
            Assert.That(third.TotalDamage, Is.Zero);

            yield return WaitUntilDamaged(third);

            Assert.That(third.TotalDamage, Is.EqualTo(2f).Within(0.001f));

            yield return Cleanup(root, first, second, third);
        }

        [UnityTest]
        public IEnumerator IntervalChain_TicksAndDoesNotReopenOnSameTarget()
        {
            hitOrder.Clear();
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);

            Spawn(
                root,
                damage: 5f,
                acquireRadius: 2f,
                chainRadius: 2f,
                maxTargets: 1,
                lifetimeSeconds: 0.5f,
                tickInterval: 0.1f,
                kind: IntervalChildKind.LingeringTargeted);
            yield return WaitUntilHitCount(3);

            Assert.That(hitOrder.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(hitOrder[0], Is.Not.EqualTo(hitOrder[1]));
            Assert.That(hitOrder[1], Is.Not.EqualTo(hitOrder[2]));
            Assert.That(first.TotalDamage, Is.GreaterThan(0f));
            Assert.That(second.TotalDamage, Is.GreaterThan(0f));

            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            yield return WaitUntilNoActiveTargeted(entityManager);
            Assert.That(ActiveTargetedCount(entityManager), Is.Zero);

            yield return Cleanup(root, first, second);
        }

        [UnityTest]
        public IEnumerator ManaGate_RejectsTargetedCastAndSignalsCastToken()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe caster = CreateTarget(Vector2.zero, CombatFaction.Player, mana: 2f);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(caster);
            root.TargetRegistry.Register(target);
            yield return WaitUntilProxy(caster);

            Spawn(
                root,
                damage: 5f,
                acquireRadius: 2f,
                chainRadius: 2f,
                maxTargets: 1,
                caster: caster.CombatTargetProxy,
                manaCost: 3f,
                castToken: 17);
            yield return WaitUntilRejected(caster);

            Assert.That(caster.RejectedCastToken, Is.EqualTo(17));
            Assert.That(caster.CombatCurrentMana, Is.EqualTo(2f));
            Assert.That(ActiveTargetedCount(World.DefaultGameObjectInjectionWorld.EntityManager), Is.Zero);

            yield return Cleanup(root, caster, target);
        }

        [UnityTest]
        public IEnumerator OnImpactTargeted_FiresOneFrameAfterProjectileDamage()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);
            yield return WaitUntilProxy(target);

            TargetedSpawnCommand targeted = new()
            {
                Count = 1,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 5f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                },
                Resolve = new TargetedResolveConfig
                {
                    AcquireRadius = 2f,
                    ChainRadius = 2f,
                    ChainDamageFalloff = 1f,
                    MaxTargets = 1
                },
                VfxSize = new TargetedVfxSizeComponent { EffectSize = 1f, LinkWidth = 1f }
            };
            Hash128 targetedKey = root.RegisterSpawnTemplate(in targeted);
            ProjectileSpawnCommand projectile = new()
            {
                Count = 1,
                Lifetime = 1f,
                Radius = 0.25f,
                ShapeType = CombatShapeType.Circle,
                PierceRemaining = 0,
                HitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = 1f,
                        CritMultiplier = 1f,
                        DirectDamageEnabled = true
                    },
                    new OnHitSpawnRef
                    {
                        Kind = IntervalChildKind.Targeted,
                        TemplateKey = targetedKey
                    })
            };
            Hash128 projectileKey = root.RegisterSpawnTemplate(in projectile);
            root.SpawnRegisteredProjectile(
                projectileKey,
                target.CombatTargetPosition,
                Vector2.right,
                count: 1,
                faction: CombatFaction.Player);

            yield return WaitUntilDamaged(target);

            Assert.That(target.TotalDamage, Is.EqualTo(1f).Within(0.001f));
            yield return null;
            Assert.That(target.TotalDamage, Is.EqualTo(6f).Within(0.001f));

            yield return Cleanup(root, target);
        }

        [UnityTest]
        public IEnumerator LingeringAoeEnergyInterval_SpawnsTargetedChild()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);
            yield return WaitUntilProxy(target);

            TargetedSpawnCommand targeted = new()
            {
                Count = 1,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 5f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                },
                Resolve = new TargetedResolveConfig
                {
                    AcquireRadius = 2f,
                    ChainRadius = 2f,
                    ChainDamageFalloff = 1f,
                    MaxTargets = 1
                },
                VfxSize = new TargetedVfxSizeComponent { EffectSize = 1f, LinkWidth = 1f }
            };
            Hash128 targetedKey = root.RegisterSpawnTemplate(in targeted);
            AoeSpawnCommand source = new()
            {
                EchoCount = 1,
                Lifetime = 0.5f,
                Radius = 0.25f,
                ShapeType = CombatShapeType.Circle,
                HasTimedSpawner = 1,
                TimedSpawn = new TimedSpawnComponent
                {
                    ChildKind = IntervalChildKind.Targeted,
                    TemplateKey = targetedKey,
                    EnergyPerSecond = 100f,
                    EnergyThreshold = 1f
                }
            };
            Hash128 sourceKey = root.RegisterSpawnTemplate(in source);
            root.SpawnRegisteredAoe(
                sourceKey,
                Vector2.zero,
                count: 1,
                faction: CombatFaction.Player,
                kind: IntervalChildKind.LingeringAoe);

            yield return WaitUntilDamaged(target);

            Assert.That(target.TotalDamage, Is.EqualTo(5f).Within(0.001f));
            yield return Cleanup(root, target);
        }

        [UnityTest]
        public IEnumerator StackApplicator_ChainDetonatesEveryLinkedMobAtThreshold()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            TargetProbe third = CreateTarget(new Vector2(2.75f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);
            root.TargetRegistry.Register(third);

            AoeSpawnCommand detonation = new()
            {
                EchoCount = 1,
                Radius = 0.25f,
                AreaSize = 1f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 7f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                }
            };
            Hash128 detonationKey = root.RegisterSpawnTemplate(in detonation);
            TargetedSpawnCommand applicator = new()
            {
                Count = 1,
                HitPayload = new CombatHitPayload
                {
                    CritMultiplier = 1f,
                    StackEffect = new StackEffectSnapshot
                    {
                        DebuffKey = 91,
                        Threshold = 1,
                        StacksPerHit = 1,
                        Lifetime = 1f,
                        Faction = CombatFaction.Player,
                        DetonationKind = StackDetonationKind.ImpactAoe,
                        DetonationKey = detonationKey
                    }
                },
                Resolve = new TargetedResolveConfig
                {
                    AcquireRadius = 2f,
                    ChainRadius = 1.5f,
                    ChainDamageFalloff = 1f,
                    MaxTargets = 3
                },
                VfxSize = new TargetedVfxSizeComponent { EffectSize = 1f, LinkWidth = 1f }
            };
            Hash128 applicatorKey = root.RegisterSpawnTemplate(in applicator);
            root.SpawnRegisteredTargeted(
                applicatorKey,
                Vector2.zero,
                Vector2.zero,
                count: 1,
                faction: CombatFaction.Player,
                kind: IntervalChildKind.Targeted);

            yield return WaitUntilDamaged(first);
            yield return WaitUntilDamaged(second);
            yield return WaitUntilDamaged(third);

            float firstDamage = first.TotalDamage;
            float secondDamage = second.TotalDamage;
            float thirdDamage = third.TotalDamage;
            yield return Cleanup(root, first, second, third);

            Assert.That(firstDamage, Is.EqualTo(7f).Within(0.001f));
            Assert.That(secondDamage, Is.EqualTo(7f).Within(0.001f));
            Assert.That(thirdDamage, Is.EqualTo(7f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator LinkVfx_QueuesOneLineSegmentForEachResolvedLink()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            TargetProbe third = CreateTarget(new Vector2(2.75f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);
            root.TargetRegistry.Register(third);

            World world = World.DefaultGameObjectInjectionWorld;
            CombatAoeVfxDispatchSystem dispatch =
                world.GetExistingSystemManaged<CombatAoeVfxDispatchSystem>();
            Assert.That(dispatch, Is.Not.Null);
            int before = PendingLineSegmentCount(world.EntityManager);
            dispatch.Enabled = false;

            Spawn(
                root,
                damage: 1f,
                acquireRadius: 2f,
                chainRadius: 1.5f,
                maxTargets: 3,
                linkVfxId: VfxDataShapeTable.EncodeId(VfxDataShape.LineSegment, 1));
            yield return WaitUntilDamaged(third);

            int emitted = PendingLineSegmentCount(world.EntityManager) - before;
            dispatch.Enabled = true;

            Assert.That(emitted, Is.EqualTo(3));
            yield return Cleanup(root, first, second, third);
        }

        [UnityTest]
        public IEnumerator MobCast_TargetsPlayerAndNeverOtherMobs()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe nearbyMob = CreateTarget(new Vector2(0.5f, 0f), CombatFaction.Mob);
            TargetProbe player = CreateTarget(new Vector2(1f, 0f), CombatFaction.Player);
            root.TargetRegistry.Register(nearbyMob);
            root.TargetRegistry.Register(player);

            Spawn(
                root,
                damage: 5f,
                acquireRadius: 2f,
                chainRadius: 2f,
                maxTargets: 1,
                faction: CombatFaction.Mob);
            yield return WaitUntilDamaged(player);

            Assert.That(player.TotalDamage, Is.EqualTo(5f).Within(0.001f));
            Assert.That(nearbyMob.TotalDamage, Is.Zero);

            yield return Cleanup(root, nearbyMob, player);
        }

        [UnityTest]
        public IEnumerator PoolReuse_RemainsBoundedAcrossTwoHundredCastsPerVariant()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);

            for (int i = 0; i < 200; i++)
                Spawn(root, 0f, 2f, 2f, maxTargets: 1);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            yield return WaitUntilNoActiveTargeted(entityManager);
            int singleHitSlots = SingleHitTargetedCount(entityManager);

            for (int i = 0; i < 200; i++)
                Spawn(root, 0f, 2f, 2f, maxTargets: 1);
            yield return WaitUntilNoActiveTargeted(entityManager);
            Assert.That(SingleHitTargetedCount(entityManager), Is.EqualTo(singleHitSlots));

            for (int i = 0; i < 200; i++)
            {
                Spawn(
                    root,
                    0f,
                    2f,
                    2f,
                    maxTargets: 1,
                    lifetimeSeconds: 0.05f,
                    tickInterval: 0.1f,
                    kind: IntervalChildKind.LingeringTargeted);
            }
            yield return WaitUntilNoActiveTargeted(entityManager);
            int lingeringSlots = LingeringTargetedCount(entityManager);

            for (int i = 0; i < 200; i++)
            {
                Spawn(
                    root,
                    0f,
                    2f,
                    2f,
                    maxTargets: 1,
                    lifetimeSeconds: 0.05f,
                    tickInterval: 0.1f,
                    kind: IntervalChildKind.LingeringTargeted);
            }
            yield return WaitUntilNoActiveTargeted(entityManager);
            Assert.That(LingeringTargetedCount(entityManager), Is.EqualTo(lingeringSlots));

            yield return Cleanup(root, target);
        }

        [UnityTest]
        public IEnumerator MixedScenePoolCleanup_TrimsProjectileAoeAndTargetedPoolsAfterWindDown()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            int projectilesBefore = Count<ProjectileTag>(entityManager);
            int aoesBefore = Count<AoeTag>(entityManager);
            int targetedBefore = Count<TargetedTag>(entityManager);
            Entity cleanupConfigEntity = SingletonEntity<CombatPoolCleanupConfig>(entityManager);
            CombatPoolCleanupConfig cleanupConfig =
                entityManager.GetComponentData<CombatPoolCleanupConfig>(cleanupConfigEntity);
            entityManager.SetComponentData(cleanupConfigEntity, new CombatPoolCleanupConfig
            {
                ChunkActiveThresholdPercent = cleanupConfig.ChunkActiveThresholdPercent,
                DespawnOverSpawnMargin = 0f,
                RateSmoothingTime = 0.01f
            });

            ProjectileSpawnCommand projectile = new()
            {
                Count = 1,
                Speed = 0f,
                Lifetime = 0.1f,
                Radius = 0.1f,
                ShapeType = CombatShapeType.Circle,
                BaseDirection = new float2(1f, 0f),
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    CritMultiplier = 1f
                })
            };
            Hash128 projectileKey = root.RegisterSpawnTemplate(in projectile);
            AoeSpawnCommand aoe = new()
            {
                EchoCount = 1,
                Lifetime = 0.1f,
                Radius = 0.1f,
                ShapeType = CombatShapeType.Circle
            };
            Hash128 aoeKey = root.RegisterSpawnTemplate(in aoe);

            for (int i = 0; i < 20; i++)
            {
                root.SpawnRegisteredProjectile(
                    projectileKey,
                    Vector2.zero,
                    Vector2.right,
                    count: 1,
                    faction: CombatFaction.Player);
                root.SpawnRegisteredAoe(
                    aoeKey,
                    Vector2.zero,
                    count: 1,
                    faction: CombatFaction.Player,
                    kind: IntervalChildKind.ImpactAoe);
                Spawn(
                    root,
                    damage: 0f,
                    acquireRadius: 2f,
                    chainRadius: 2f,
                    maxTargets: 1,
                    lifetimeSeconds: 0.1f,
                    tickInterval: 1f,
                    kind: IntervalChildKind.LingeringTargeted);
            }

            yield return null;
            int projectilesDuring = Count<ProjectileTag>(entityManager);
            int aoesDuring = Count<AoeTag>(entityManager);
            int targetedDuring = Count<TargetedTag>(entityManager);
            int totalDuring = projectilesDuring + aoesDuring + targetedDuring;

            yield return WaitUntilNoActiveCombat(entityManager);
            for (int frame = 0; frame < 180; frame++)
                yield return null;

            int totalAfterWindDown = Count<ProjectileTag>(entityManager)
                + Count<AoeTag>(entityManager)
                + Count<TargetedTag>(entityManager);

            yield return Cleanup(root, target);
            entityManager.SetComponentData(cleanupConfigEntity, cleanupConfig);

            Assert.That(projectilesDuring, Is.GreaterThan(projectilesBefore));
            Assert.That(aoesDuring, Is.GreaterThan(aoesBefore));
            Assert.That(targetedDuring, Is.GreaterThan(targetedBefore));
            Assert.That(totalAfterWindDown, Is.LessThan(totalDuring));
        }

        private static CombatRoot CreateRoot(out GameObject rootObject)
        {
            rootObject = new GameObject("Targeted Combat Root");
            rootObject.SetActive(false);
            CombatRoot root = rootObject.AddComponent<CombatRoot>();
            rootObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = rootObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Material/EcsAtlasIndirectSprite.mat");
            FieldInfo rendererField = typeof(CombatRoot).GetField(
                "combatSpriteRenderer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rendererField, Is.Not.Null);
            rendererField.SetValue(root, renderer);
            root.ConfigureAtlas(CombatAtlasTestFixture.Atlas);
            rootObject.SetActive(true);
            return root;
        }

        private static void Spawn(
            CombatRoot root,
            float damage,
            float acquireRadius,
            float chainRadius,
            int maxTargets,
            float falloff = 1f,
            float chainDelay = 0f,
            float lifetimeSeconds = 0f,
            float tickInterval = 0f,
            IntervalChildKind kind = IntervalChildKind.Targeted,
            Entity caster = default,
            float manaCost = 0f,
            int castToken = 0,
            CombatFaction faction = CombatFaction.Player,
            int linkVfxId = 0)
        {
            TargetedSpawnCommand template = new()
            {
                Count = 1,
                LifetimeSeconds = lifetimeSeconds > 0f
                    ? lifetimeSeconds
                    : chainDelay > 0f
                    ? maxTargets * chainDelay + (1f / 60f)
                    : 0f,
                TickIntervalSeconds = tickInterval,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = damage,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                },
                Resolve = new TargetedResolveConfig
                {
                    AcquireRadius = acquireRadius,
                    ChainRadius = chainRadius,
                    ChainDamageFalloff = falloff,
                    ChainDelaySeconds = chainDelay,
                    MaxTargets = maxTargets
                },
                VfxIds = new TargetedVfxIds { LinkId = linkVfxId },
                VfxSize = new TargetedVfxSizeComponent { EffectSize = 1f, LinkWidth = 1f }
            };
            Hash128 key = root.RegisterSpawnTemplate(in template);
            root.SpawnRegisteredTargeted(
                key,
                Vector2.zero,
                Vector2.zero,
                count: 1,
                faction: faction,
                kind: kind,
                caster: caster,
                manaCost: manaCost,
                castToken: castToken);
        }

        private static TargetProbe CreateTarget(
            Vector2 position,
            CombatFaction faction = CombatFaction.Mob,
            float mana = 0f)
        {
            GameObject gameObject = new("Targeted Probe");
            gameObject.transform.position = position;
            TargetProbe target = gameObject.AddComponent<TargetProbe>();
            target.Configure(++nextTargetId, faction, radius: 0.25f, mana: mana);
            return target;
        }

        private static int Count<T>(EntityManager entityManager) where T : unmanaged, IComponentData
        {
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return query.CalculateEntityCount();
        }

        private static IEnumerator WaitUntilDamaged(TargetProbe target)
        {
            for (int frame = 0; frame < 120 && target.TotalDamage <= 0f; frame++)
                yield return null;
        }

        private static IEnumerator WaitUntilHitCount(int count)
        {
            for (int frame = 0; frame < 120 && hitOrder.Count < count; frame++)
                yield return null;
        }

        private static IEnumerator WaitUntilNoActiveTargeted(EntityManager entityManager)
        {
            // Commands are applied later in the simulation update. Always cross one frame
            // before looking for active instances, otherwise a newly queued batch can be
            // mistaken for an already-finished one.
            yield return null;

            for (int frame = 0; frame < 120 && ActiveTargetedCount(entityManager) > 0; frame++)
                yield return null;
        }

        private static IEnumerator WaitUntilNoActiveCombat(EntityManager entityManager)
        {
            yield return null;
            for (int frame = 0; frame < 120 && Count<Active>(entityManager) > 0; frame++)
                yield return null;
        }

        private static IEnumerator WaitUntilProxy(TargetProbe target)
        {
            for (int frame = 0; frame < 120 && target.CombatTargetProxy == Entity.Null; frame++)
                yield return null;
        }

        private static IEnumerator WaitUntilRejected(TargetProbe target)
        {
            for (int frame = 0; frame < 120 && target.RejectedCastToken == 0; frame++)
                yield return null;
        }

        private static int ActiveTargetedCount(EntityManager entityManager)
        {
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.ReadOnly<Active>());
            return query.CalculateEntityCount();
        }

        private static int SingleHitTargetedCount(EntityManager entityManager)
        {
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.Exclude<LingeringTargetedTag>());
            return query.CalculateEntityCount();
        }

        private static int LingeringTargetedCount(EntityManager entityManager)
        {
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TargetedTag>(),
                ComponentType.ReadOnly<LingeringTargetedTag>());
            return query.CalculateEntityCount();
        }

        private static int PendingLineSegmentCount(EntityManager entityManager)
        {
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatAoeVfxDispatchSingleton>());
            CombatAoeVfxDispatchSingleton lane =
                entityManager.GetComponentData<CombatAoeVfxDispatchSingleton>(query.GetSingletonEntity());
            lane.ProducerHandle.Complete();
            return lane.PendingLineSegmentSpawns.Count;
        }

        private static Entity SingletonEntity<T>(EntityManager entityManager)
            where T : unmanaged, IComponentData
        {
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            return query.GetSingletonEntity();
        }

        private static IEnumerator Cleanup(CombatRoot root, params TargetProbe[] targets)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                root.TargetRegistry.Unregister(targets[i]);
                CombatTargetProxy.Delete(targets[i]);
            }

            yield return null;

            for (int i = 0; i < targets.Length; i++)
                Object.Destroy(targets[i].gameObject);
            Object.Destroy(root.gameObject);
            yield return null;
        }

        private sealed class TargetProbe : MonoBehaviour, ICombatTarget
        {
            private int targetId;
            private CombatFaction faction;
            private float radius;
            private float mana;
            private Entity combatTargetProxy;

            public int TargetId => targetId;
            public CombatFaction CombatFaction => faction;
            public Entity CombatTargetProxy
            {
                get => combatTargetProxy;
                set => combatTargetProxy = value;
            }
            public Vector2 CombatTargetPosition => transform.position;
            public float CombatTargetRadius => radius;
            public Vector2 CombatTargetHalfExtents => Vector2.one * radius;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => 1;
            public float CombatMaxMana => mana;
            public float CombatCurrentMana => mana;
            public bool IsCombatTargetActive => true;
            public float TotalDamage { get; private set; }
            public int RejectedCastToken { get; private set; }

            public void Configure(int id, CombatFaction targetFaction, float radius, float mana = 0f)
            {
                targetId = id;
                faction = targetFaction;
                this.radius = radius;
                this.mana = mana;
            }

            public void ReceiveHit(in CombatHitData hit)
            {
                TotalDamage += hit.Damage.Amount;
                hitOrder.Add(targetId);
            }

            public void ReceiveSpawnRejected(int castToken)
            {
                RejectedCastToken = castToken;
            }
        }
    }
}
