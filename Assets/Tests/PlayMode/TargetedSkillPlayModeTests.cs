using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills.Runtime;
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

        // Cleanup must run even when an assertion throws mid-test, otherwise a failing test's
        // CombatRoot and target proxies leak into every later test in the fixture (shared
        // World.DefaultGameObjectInjectionWorld) and cascade-fail them too. UnityTearDown always
        // runs, unlike an inline `yield return Cleanup(...)` placed after the assertions.
        private CombatRoot activeRoot;
        private readonly List<TargetProbe> activeTargets = new();

        [UnityTearDown]
        public IEnumerator TearDownActiveCombat()
        {
            if (activeRoot == null)
            {
                yield break;
            }

            yield return Cleanup(activeRoot, activeTargets.ToArray());
            activeRoot = null;
            activeTargets.Clear();
        }

        [UnityTest]
        public IEnumerator BasicCast_DamagesTargetWithoutCreatingProjectileOrAoe()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            int projectileCountBefore = Count<ProjectileTag>(entityManager);
            int aoeCountBefore = Count<AoeTag>(entityManager);

            Spawn(root, damage: 5f, chainDistance: 2f, chainCount: 1);
            yield return null;
            yield return null;

            Assert.That(target.TotalDamage, Is.EqualTo(5f).Within(0.001f));
            Assert.That(Count<ProjectileTag>(entityManager), Is.EqualTo(projectileCountBefore));
            Assert.That(Count<AoeTag>(entityManager), Is.EqualTo(aoeCountBefore));
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

            Spawn(root, damage: 8f, chainDistance: 2f, chainCount: 3, falloff: 0.5f);
            yield return null;
            yield return null;

            Assert.That(first.TotalDamage, Is.EqualTo(8f).Within(0.001f));
            Assert.That(second.TotalDamage, Is.EqualTo(4f).Within(0.001f));
            Assert.That(third.TotalDamage, Is.EqualTo(2f).Within(0.001f));
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
                chainDistance: 2f,
                chainCount: 3,
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
        }

        [UnityTest]
        public IEnumerator Chain_AlternatesBetweenTargetsAndExpiresWithItsWalk()
        {
            // Two enemies and four chains: the walk bounces A-B-A-B rather than re-zapping the
            // one it just hit, and the instance is gone the moment the last chain is spent.
            // chainDelay spreads links across separate frames: hit delivery is aggregated per
            // target per frame (CombatApplyFinalizeSingleSystem groups by target before
            // ReceiveHit fires), so two same-frame links on one target would otherwise collapse
            // into a single hitOrder entry and make the alternation unobservable here.
            hitOrder.Clear();
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);

            Spawn(root, damage: 5f, chainDistance: 2f, chainCount: 4, chainDelay: 0.1f);
            yield return WaitUntilHitCount(4);

            Assert.That(hitOrder.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(hitOrder[0], Is.Not.EqualTo(hitOrder[1]));
            Assert.That(hitOrder[1], Is.Not.EqualTo(hitOrder[2]));
            Assert.That(hitOrder[2], Is.Not.EqualTo(hitOrder[3]));
            Assert.That(first.TotalDamage, Is.GreaterThan(0f));
            Assert.That(second.TotalDamage, Is.GreaterThan(0f));

            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            yield return WaitUntilNoActiveTargeted(entityManager);
            Assert.That(ActiveTargetedCount(entityManager), Is.Zero);
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
                chainDistance: 2f,
                chainCount: 1,
                caster: caster.CombatTargetProxy,
                manaCost: 3f,
                castToken: 17);
            yield return WaitUntilRejected(caster);

            Assert.That(caster.RejectedCastToken, Is.EqualTo(17));
            Assert.That(caster.CombatCurrentMana, Is.EqualTo(2f));
            Assert.That(ActiveTargetedCount(World.DefaultGameObjectInjectionWorld.EntityManager), Is.Zero);
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
                EchoCount = 1,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 5f,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                },
                Resolve = new TargetedResolveConfig
                {
                    ChainDistance = 2f,
                    ChainDamageFalloff = 1f,
                    ChainCount = 1
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
        }

        [UnityTest]
        public IEnumerator HitEnergyApplicator_ChainActivatesEveryLinkedMobAtThreshold()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe first = CreateTarget(new Vector2(1f, 0f));
            TargetProbe second = CreateTarget(new Vector2(2f, 0f));
            TargetProbe third = CreateTarget(new Vector2(2.75f, 0f));
            root.TargetRegistry.Register(first);
            root.TargetRegistry.Register(second);
            root.TargetRegistry.Register(third);

            AoeSpawnCommand outputTemplate = new()
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
            Hash128 outputTemplateKey = root.RegisterSpawnTemplate(in outputTemplate);
            TargetedSpawnCommand applicator = new()
            {
                EchoCount = 1,
                HitPayload = new CombatHitPayload
                {
                    CritMultiplier = 1f,
                    HitEnergy = new HitEnergyPayload
                    {
                        AccumulatorId = 91,
                        EnergyRequired = 1f,
                        EnergyPerHit = 1f,
                        RetentionSeconds = 1f,
                        Spawn = new HitEnergySpawn
                        {
                            Faction = CombatFaction.Player,
                            Kind = HitEnergySpawnKind.ImpactAoe,
                            TemplateKey = outputTemplateKey
                        }
                    }
                },
                Resolve = new TargetedResolveConfig
                {
                    ChainDistance = 2f,
                    ChainDamageFalloff = 1f,
                    ChainCount = 3
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
                chainDistance: 2f,
                chainCount: 3,
                linkVfxId: VfxDataShapeTable.EncodeId(VfxDataShape.LineSegment, 1));
            yield return WaitUntilDamaged(third);

            int emitted = PendingLineSegmentCount(world.EntityManager) - before;
            dispatch.Enabled = true;

            Assert.That(emitted, Is.EqualTo(3));
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
                chainDistance: 2f,
                chainCount: 1,
                faction: CombatFaction.Mob);
            yield return WaitUntilDamaged(player);

            Assert.That(player.TotalDamage, Is.EqualTo(5f).Within(0.001f));
            Assert.That(nearbyMob.TotalDamage, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PoolReuse_RemainsBoundedAcrossTwoHundredCasts()
        {
            CombatRoot root = CreateRoot(out _);
            TargetProbe target = CreateTarget(new Vector2(1f, 0f));
            root.TargetRegistry.Register(target);

            for (int i = 0; i < 200; i++)
                Spawn(root, 0f, 2f, chainCount: 1);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            yield return WaitUntilNoActiveTargeted(entityManager);
            int slots = Count<TargetedTag>(entityManager);

            for (int i = 0; i < 200; i++)
                Spawn(root, 0f, 2f, chainCount: 1);
            yield return WaitUntilNoActiveTargeted(entityManager);

            Assert.That(Count<TargetedTag>(entityManager), Is.EqualTo(slots));
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
                    chainDistance: 2f,
                    chainCount: 1);
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

            entityManager.SetComponentData(cleanupConfigEntity, cleanupConfig);

            Assert.That(projectilesDuring, Is.GreaterThan(projectilesBefore));
            Assert.That(aoesDuring, Is.GreaterThan(aoesBefore));
            Assert.That(targetedDuring, Is.GreaterThan(targetedBefore));
            Assert.That(totalAfterWindDown, Is.LessThan(totalDuring));
        }

        private CombatRoot CreateRoot(out GameObject rootObject)
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
            activeRoot = root;
            return root;
        }

        private static void Spawn(
            CombatRoot root,
            float damage,
            float chainDistance,
            int chainCount,
            float falloff = 1f,
            float chainDelay = 0f,
            IntervalChildKind kind = IntervalChildKind.Targeted,
            Entity caster = default,
            float manaCost = 0f,
            int castToken = 0,
            CombatFaction faction = CombatFaction.Player,
            int linkVfxId = 0)
        {
            TargetedSpawnCommand template = new()
            {
                EchoCount = 1,
                // Derived exactly the way the compiler does it: a backstop behind the walk.
                LifetimeSeconds = RuntimeTargetedDefinition.LifetimeFor(chainCount, chainDelay),
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = damage,
                    CritMultiplier = 1f,
                    DirectDamageEnabled = true
                },
                Resolve = new TargetedResolveConfig
                {
                    ChainDistance = chainDistance,
                    ChainDamageFalloff = falloff,
                    ChainDelay = chainDelay,
                    ChainCount = chainCount
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

        private TargetProbe CreateTarget(
            Vector2 position,
            CombatFaction faction = CombatFaction.Mob,
            float mana = 0f)
        {
            GameObject gameObject = new("Targeted Probe");
            gameObject.transform.position = position;
            TargetProbe target = gameObject.AddComponent<TargetProbe>();
            target.Configure(++nextTargetId, faction, radius: 0.25f, mana: mana);
            activeTargets.Add(target);
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
            Entity[] proxies = new Entity[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                proxies[i] = targets[i].CombatTargetProxy;
                root.TargetRegistry.Unregister(targets[i]);
                CombatTargetProxy.Delete(targets[i]);
            }

            // Deletion is deferred (event -> PresentationSystemGroup destroy -> next hash
            // rebuild). Waiting on a fixed frame count instead of the actual entity state left a
            // window where the next test's target at the same position could still see this
            // proxy in the spatial hash, and pre-acquisition would silently pick the dying one.
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            for (int frame = 0; frame < 120 && AnyProxyExists(entityManager, proxies); frame++)
                yield return null;

            // One more frame so the spatial hash rebuilds without the just-destroyed proxies.
            yield return null;

            for (int i = 0; i < targets.Length; i++)
                Object.Destroy(targets[i].gameObject);
            Object.Destroy(root.gameObject);
            yield return null;
        }

        private static bool AnyProxyExists(EntityManager entityManager, Entity[] proxies)
        {
            for (int i = 0; i < proxies.Length; i++)
            {
                if (proxies[i] != Entity.Null && entityManager.Exists(proxies[i]))
                    return true;
            }

            return false;
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
