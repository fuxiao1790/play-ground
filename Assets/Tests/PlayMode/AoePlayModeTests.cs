using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.Skills;
using PlayGround.Common.StatusEffects;
using PlayGround.Mob;
using PlayGround.Mob.Behaviours;
using PlayGround.Mob.Triggers;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGround.Tests.PlayMode
{
    public sealed class AoePlayModeTests
    {
        private const int DefaultTargetMask = 1;
        private static int nextTargetId = 1000;

        [UnityTest]
        public IEnumerator AoePrefabTransformScaleAffectsCollisionShape()
        {
            CreateAoeFixture(
                out GameObject rootObject,
                out CombatRoot root,
                out GameObject templateObject,
                out int typeId,
                templateScale: new Vector3(3f, 3f, 1f));
            AoeTargetProbe target = CreateTarget(new Vector2(2.5f, 0f), DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(typeId, templateObject, Vector2.zero, DefaultTargetMask, 2f));
            yield return null;

            Assert.That(target.HitCount, Is.EqualTo(1));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeVisualBakeUsesSpriteRendererTransformScale()
        {
            CreateAoeFixture(
                out GameObject rootObject,
                out CombatRoot root,
                out GameObject templateObject,
                out int typeId,
                templateScale: new Vector3(2f, 2f, 1f),
                visualScale: new Vector3(3f, 4f, 1f));
            root.Spawn(Command(typeId, templateObject, Vector2.zero, DefaultTargetMask, 2f));
            yield return null;

            Matrix4x4 matrix = FirstScopedAoeRenderMatrix(root);

            Assert.That(matrix.m00, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(matrix.m11, Is.EqualTo(8f).Within(0.0001f));
            Cleanup(rootObject, templateObject);
        }

        [UnityTest]
        public IEnumerator AoeSpawnAreaSizeScalesCollisionAndRenderBeforeEcsSimulation()
        {
            CreateAoeFixture(
                out GameObject rootObject,
                out CombatRoot root,
                out GameObject templateObject,
                out int typeId,
                visualScale: new Vector3(3f, 4f, 1f));
            AoeTargetProbe target = CreateTarget(new Vector2(1.5f, 0f), DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(new AoeSpawnCommand(
                typeId,
                Vector2.zero,
                DefaultTargetMask,
                new DamageSnapshot(2f),
                lifetimeSeconds: 0f,
                tickIntervalSeconds: 0f,
                Geometry(templateObject, 2f)));
            yield return null;

            CombatCollisionComponent collision = FirstScopedAoeCollision(root);
            Matrix4x4 matrix = FirstScopedAoeRenderMatrix(root);

            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(collision.Radius, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(matrix.m00, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(matrix.m11, Is.EqualTo(8f).Within(0.0001f));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeRootCreatesNoPerAoeLiveDamageOrColliderObjects()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            for (int i = 0; i < 3; i++)
            {
                root.Spawn(Command(typeId, templateObject, Vector2.zero, DefaultTargetMask, 1f));
                yield return null;
            }

            Assert.That(rootObject.transform.childCount, Is.EqualTo(0));
            Assert.That(rootObject.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeDamageDispatchCallsTargetDamageCallback()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(typeId, templateObject, Vector2.zero, DefaultTargetMask, 3f));
            yield return null;

            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.LastDamage.Amount, Is.EqualTo(3f));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator MobStatusTriggerSpawnsAoeThroughBoundAoeRoot()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out _);
            MobRoot mob = CreateMobTarget(Vector2.zero);
            mob.BindAoeRoot(root);
            mob.Register(root.TargetRegistry);

            AoeConfig triggerConfig = CreateMinimalAoeConfig();
            StackingTriggerDef trigger = ScriptableObject.CreateInstance<StackingTriggerDef>();
            trigger.Configure(3, triggerConfig, 6f);

            mob.StatusEffects.AddEffect(trigger, 3, trigger.DamageContributionPerStack);

            Assert.That(root.Counters.SpawnedAoes, Is.EqualTo(0));
            mob.StatusEffects.Tick(0f);
            Assert.That(root.Counters.SpawnedAoes, Is.EqualTo(1));

            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(4f));
            Object.Destroy(trigger);
            Object.Destroy(triggerConfig);
            Cleanup(rootObject, templateObject, mob.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeCountersTrackSpawnDespawnHitAndRenderBatchFields()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(typeId, templateObject, Vector2.zero, DefaultTargetMask, 2f));
            yield return null;

            AoeRuntimeCounters counters = root.Counters;
            Assert.That(counters.SpawnedAoes, Is.EqualTo(1));
            Assert.That(counters.DespawnedOrReusedAoes, Is.EqualTo(1));
            Assert.That(counters.HitEvents, Is.EqualTo(1));
            Assert.That(counters.RenderBatches, Is.GreaterThanOrEqualTo(0));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void SeparateCombatRootsShareDefaultWorldAndSingleSharedScope()
        {
            CreateProjectileRoot(out GameObject projectileObject, out CombatRoot projectileRoot);
            CreateAoeFixture(out GameObject aoeObject, out CombatRoot aoeRoot, out GameObject templateObject, out _);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity projectileScope = ScopeEntity(projectileRoot);
            Entity aoeScope = ScopeEntity(aoeRoot);

            // There is one ref-counted shared CombatScope entity for the whole
            // world; every CombatRoot binds to the same one.
            Assert.That(projectileScope, Is.EqualTo(aoeScope));
            Assert.That(entityManager.HasComponent<CombatScope>(projectileScope), Is.True);

            Object.DestroyImmediate(projectileObject);
            Assert.That(entityManager.Exists(aoeScope), Is.True,
                "Scope must stay alive while another CombatRoot still references it.");

            Cleanup(aoeObject, templateObject);
        }

        private static AoeSpawnCommand Command(
            int typeId,
            GameObject templateObject,
            Vector2 position,
            int targetMask,
            float damage)
        {
            return new AoeSpawnCommand(
                typeId,
                position,
                targetMask,
                new DamageSnapshot(damage),
                lifetimeSeconds: 0f,
                tickIntervalSeconds: 0f,
                Geometry(templateObject));
        }

        private static AoeSpawnGeometry Geometry(GameObject templateObject, float areaSize = 1f)
        {
            return AoeSpawnGeometry.FromTemplate(
                templateObject,
                templateObject.GetComponentInChildren<Collider2D>(true),
                areaSize,
                0f);
        }

        private static void CreateAoeFixture(
            out GameObject rootObject,
            out CombatRoot root,
            out GameObject templateObject,
            out int typeId,
            Vector3? templateScale = null,
            Vector3? visualScale = null)
        {
            templateObject = new GameObject("AoeTemplate");
            templateObject.SetActive(false);
            templateObject.transform.localScale = templateScale ?? Vector3.one;
            CircleCollider2D shape = templateObject.AddComponent<CircleCollider2D>();
            shape.radius = 1f;
            GameObject visualObject = new("Visual");
            visualObject.transform.SetParent(templateObject.transform, false);
            visualObject.transform.localScale = visualScale ?? Vector3.one;
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);

            var definition = new AoeTypeDefinition();
            definition.Configure(templateObject, shape);

            rootObject = new GameObject("AoeRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<CombatRoot>();
            rootObject.SetActive(true);

            typeId = root.RegisterType(definition);
        }

        private static AoeConfig CreateMinimalAoeConfig()
        {
            GameObject prefabObject = new GameObject("MinimalAoePrefab");
            prefabObject.SetActive(false);
            CircleCollider2D hurtbox = prefabObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 0.5f;
            GameObject visualObject = new GameObject("Visual");
            visualObject.transform.SetParent(prefabObject.transform, false);
            SpriteRenderer sr = visualObject.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            BasicAoePrefab basicPrefab = prefabObject.AddComponent<BasicAoePrefab>();
            basicPrefab.Configure(sr, hurtbox);

            AoeConfig config = ScriptableObject.CreateInstance<AoeConfig>();
            config.Configure(basicPrefab);
            return config;
        }

        private static Matrix4x4 FirstScopedAoeRenderMatrix(CombatRoot root)
        {
            CombatFaction faction = Faction(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatRenderElement>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Faction == faction)
                {
                    return entityManager.GetComponentData<CombatRenderElement>(entities[i]).objectToWorld;
                }
            }

            Assert.Fail("No AOE render entity found for root.");
            return Matrix4x4.identity;
        }

        private static CombatCollisionComponent FirstScopedAoeCollision(CombatRoot root)
        {
            CombatFaction faction = Faction(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Faction == faction)
                {
                    return entityManager.GetComponentData<CombatCollisionComponent>(entities[i]);
                }
            }

            Assert.Fail("No AOE collision entity found for root.");
            return default;
        }

        private static void CreateProjectileRoot(out GameObject rootObject, out CombatRoot root)
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            rootObject = new GameObject("ProjectileRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<CombatRoot>();
            root.Configure(sprite);
            rootObject.SetActive(true);
        }

        private static AoeTargetProbe CreateTarget(Vector2 position, int targetMask)
        {
            GameObject targetObject = new("AoeTarget");
            targetObject.transform.position = position;
            AoeTargetProbe target = targetObject.AddComponent<AoeTargetProbe>();
            target.Configure(++nextTargetId, targetMask, 0.25f);
            return target;
        }

        private static MobRoot CreateMobTarget(Vector2 position)
        {
            GameObject mobObject = new("MobStatusTarget");
            mobObject.SetActive(false);
            mobObject.transform.position = position;
            Rigidbody2D body = mobObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            CircleCollider2D hurtbox = mobObject.AddComponent<CircleCollider2D>();
            hurtbox.isTrigger = true;
            SpriteRenderer renderer = mobObject.AddComponent<SpriteRenderer>();
            mobObject.AddComponent<StatusEffects>();
            MobRoot mob = mobObject.AddComponent<MobRoot>();
            mob.Configure(body, hurtbox, hurtbox, renderer, null);
            mob.ConfigureAuthoring(
                new MobBehaviour[] { ScriptableObject.CreateInstance<WanderBehaviour>() },
                new MobTrigger[] { ScriptableObject.CreateInstance<HurtRecoveryTrigger>() },
                new[] { new MobTriggerBehaviourMapping { triggerKey = MobRoot.DefaultTriggerKey, behaviourKey = "wander" } },
                10f,
                0f,
                0.5f);
            mobObject.SetActive(true);
            return mob;
        }

        private static Entity ScopeEntity(CombatRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo scopeEntityField = typeof(CombatRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(root);
        }

        private static CombatFaction Faction(CombatRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo factionField = typeof(CombatRoot).GetField("faction", Flags);
            return (CombatFaction)factionField.GetValue(root);
        }

        // ── AOE stack trigger chain tests ─────────────────────────────────────────

        [UnityTest]
        public IEnumerator LingeringAoeInitialHitAndPulseApplyDebuffStacks()
        {
            // Lingering AOE (damage=0, tickInterval=0) applies Volatile stacks each hit.
            // Threshold=10 ensures the chain never fires within 2 frames; a registered
            // chain type is still required for CombatStackEffectSnapshot.Enabled = true.
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out _);
            var lingeringDef = new AoeTypeDefinition();
            lingeringDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int lingeringTypeId = root.RegisterType(lingeringDef);
            var chainDef = new AoeTypeDefinition();
            chainDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int chainTypeId = root.RegisterType(chainDef);

            MobRoot mob = CreateMobTarget(Vector2.zero);
            mob.BindAoeRoot(root);
            mob.Register(root.TargetRegistry);

            AoeSpawnGeometry geometry = Geometry(templateObject, 2f);
            var stackEffect = new CombatStatusEffectSnapshot(
                debuffStatusId: (int)MobDebuffStatus.Volatile,
                stacksPerHit: 1,
                stackThreshold: 10,
                aoeTypeId: chainTypeId,
                aoeDamage: 0f,
                aoeLifetimeSeconds: 0f,
                aoeTickIntervalSeconds: 0f,
                aoeGeometry: geometry);

            root.Spawn(new AoeSpawnCommand(
                lingeringTypeId,
                Vector2.zero,
                DefaultTargetMask,
                new DamageSnapshot(0f),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 0f,
                geometry: geometry,
                stackEffect: stackEffect));

            yield return null; // frame 1: initial hit → 1 stack
            Assert.That(mob.GetDebuffStackCount(MobDebuffStatus.Volatile), Is.EqualTo(1),
                "Initial AOE hit should apply 1 Volatile stack.");

            yield return null; // frame 2: pulse hit (gate expired at dt=0) → 2 stacks
            Assert.That(mob.GetDebuffStackCount(MobDebuffStatus.Volatile), Is.EqualTo(2),
                "AOE pulse hit should increment stack to 2.");

            Cleanup(rootObject, templateObject, mob.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeStackThresholdChainFiresLinkedPulseAoe()
        {
            // Lingering AOE (damage=0, tickInterval=0) builds Volatile stacks.
            // After 3 hits the threshold fires a linked pulse AOE that deals 5 damage.
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out _);
            var lingeringDef = new AoeTypeDefinition();
            lingeringDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int lingeringTypeId = root.RegisterType(lingeringDef);
            var pulseDef = new AoeTypeDefinition();
            pulseDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int pulseTypeId = root.RegisterType(pulseDef);

            MobRoot mob = CreateMobTarget(Vector2.zero);
            mob.BindAoeRoot(root);
            mob.Register(root.TargetRegistry);

            const float ChainDamage = 5f;
            AoeSpawnGeometry geometry = Geometry(templateObject, 2f);
            var stackEffect = new CombatStatusEffectSnapshot(
                debuffStatusId: (int)MobDebuffStatus.Volatile,
                stacksPerHit: 1,
                stackThreshold: 3,
                aoeTypeId: pulseTypeId,
                aoeDamage: ChainDamage,
                aoeLifetimeSeconds: 0f,
                aoeTickIntervalSeconds: 0f,
                aoeGeometry: geometry);

            root.Spawn(new AoeSpawnCommand(
                lingeringTypeId,
                Vector2.zero,
                DefaultTargetMask,
                new DamageSnapshot(0f),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 0f,
                geometry: geometry,
                stackEffect: stackEffect));

            yield return null; // frame 1: 1 stack
            yield return null; // frame 2: 2 stacks
            yield return null; // frame 3: 3 stacks → threshold → chain spawned

            Assert.That(root.Counters.SpawnedAoes, Is.EqualTo(2),
                "Stack threshold should have spawned the linked pulse AOE (total spawns = lingering + chain).");
            Assert.That(mob.GetDebuffStackCount(MobDebuffStatus.Volatile), Is.EqualTo(0),
                "Stacks should be cleared after the threshold fires.");

            yield return null; // frame 4: chain pulse materialises and hits

            Assert.That(mob.CurrentHealth, Is.EqualTo(mob.MaxHealth - ChainDamage).Within(0.001f),
                "Chain pulse AOE should deal its damage on the frame it materialises.");

            Cleanup(rootObject, templateObject, mob.gameObject);
        }

        private static void Cleanup(params GameObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                Object.Destroy(objects[i]);
            }
        }

        private sealed class AoeTargetProbe : MonoBehaviour, ICombatTarget
        {
            private int targetId;
            private int targetMask;
            private float radius;

            public int TargetId => targetId;
            public Vector2 CombatTargetPosition => transform.position;
            public float CombatTargetRadius => radius;
            public Vector2 CombatTargetHalfExtents => Vector2.one * radius;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => targetMask;
            public bool IsCombatTargetActive => true;
            public int HitCount { get; private set; }
            public float TotalDamage { get; private set; }
            public DamageSnapshot LastDamage { get; private set; }

            public void Configure(int targetId, int targetMask, float radius)
            {
                this.targetId = targetId;
                this.targetMask = targetMask;
                this.radius = radius;
            }

            public void ReceiveHit(in CombatHitData hit)
            {
                HitCount++;
                LastDamage = hit.Damage;
                TotalDamage += hit.Damage.Amount;
            }
        }
    }
}
