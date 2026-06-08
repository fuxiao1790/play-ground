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
                out AoeRoot root,
                out GameObject templateObject,
                out int typeId,
                templateScale: new Vector3(3f, 3f, 1f));
            AoeTargetProbe target = CreateTarget(new Vector2(2.5f, 0f), DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(typeId, Vector2.zero, DefaultTargetMask, 2f));
            yield return null;

            Assert.That(target.HitCount, Is.EqualTo(1));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeVisualBakeUsesSpriteRendererTransformScale()
        {
            CreateAoeFixture(
                out GameObject rootObject,
                out AoeRoot root,
                out GameObject templateObject,
                out int typeId,
                templateScale: new Vector3(2f, 2f, 1f),
                visualScale: new Vector3(3f, 4f, 1f));
            root.Spawn(Command(typeId, Vector2.zero, DefaultTargetMask, 2f));
            yield return null;

            Matrix4x4 matrix = FirstScopedAoeRenderMatrix(root);

            Assert.That(matrix.m00, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(matrix.m11, Is.EqualTo(8f).Within(0.0001f));
            Cleanup(rootObject, templateObject);
        }

        [UnityTest]
        public IEnumerator AoeRootCreatesNoPerAoeLiveDamageOrColliderObjects()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            for (int i = 0; i < 3; i++)
            {
                root.Spawn(Command(typeId, Vector2.zero, DefaultTargetMask, 1f));
                yield return null;
            }

            Assert.That(rootObject.transform.childCount, Is.EqualTo(0));
            Assert.That(rootObject.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeHitReplayCallsTargetDamageCallback()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);
            int replayCount = 0;
            AoeHitContext replayContext = default;
            root.AoeHit += context =>
            {
                replayCount++;
                replayContext = context;
            };

            root.Spawn(Command(typeId, Vector2.zero, DefaultTargetMask, 3f));
            yield return null;

            Assert.That(replayCount, Is.EqualTo(1));
            Assert.That(replayContext.Target, Is.EqualTo(target));
            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.LastDamage.Amount, Is.EqualTo(3f));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator MobStatusTriggerSpawnsAoeThroughBoundAoeRoot()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject, out _);
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
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(typeId, Vector2.zero, DefaultTargetMask, 2f));
            yield return null;

            AoeRuntimeCounters counters = root.Counters;
            Assert.That(counters.SpawnedAoes, Is.EqualTo(1));
            Assert.That(counters.DespawnedOrReusedAoes, Is.EqualTo(1));
            Assert.That(counters.HitEvents, Is.EqualTo(1));
            Assert.That(counters.RenderBatches, Is.GreaterThanOrEqualTo(0));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void ProjectileAndAoeRootsShareDefaultWorldButUseSeparateScopes()
        {
            CreateProjectileRoot(out GameObject projectileObject, out ProjectileRoot projectileRoot);
            CreateAoeFixture(out GameObject aoeObject, out AoeRoot aoeRoot, out GameObject templateObject, out _);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity projectileScope = ProjectileScopeEntity(projectileRoot);
            Entity aoeScope = AoeScopeEntity(aoeRoot);

            Assert.That(projectileScope, Is.Not.EqualTo(aoeScope));
            Assert.That(entityManager.HasComponent<ProjectileScope>(projectileScope), Is.True);
            Assert.That(entityManager.HasComponent<AoeScope>(projectileScope), Is.False);
            Assert.That(entityManager.HasComponent<AoeScope>(aoeScope), Is.True);
            Assert.That(entityManager.HasComponent<ProjectileScope>(aoeScope), Is.False);

            Cleanup(projectileObject, aoeObject, templateObject);
        }

        private static AoeSpawnCommand Command(int typeId, Vector2 position, int targetMask, float damage)
        {
            return new AoeSpawnCommand(
                typeId,
                position,
                targetMask,
                new DamageSnapshot(damage),
                lifetimeSeconds: 0f,
                tickIntervalSeconds: 0f);
        }

        private static void CreateAoeFixture(
            out GameObject rootObject,
            out AoeRoot root,
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
            definition.Configure(templateObject, shape, 1f);

            rootObject = new GameObject("AoeRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<AoeRoot>();
            root.Configure(~0);
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

        private static Matrix4x4 FirstScopedAoeRenderMatrix(AoeRoot root)
        {
            Entity scope = AoeScopeEntity(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatRenderElement>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Scope == scope)
                {
                    return entityManager.GetComponentData<CombatRenderElement>(entities[i]).objectToWorld;
                }
            }

            Assert.Fail("No AOE render entity found for root.");
            return Matrix4x4.identity;
        }

        private static void CreateProjectileRoot(out GameObject rootObject, out ProjectileRoot root)
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            rootObject = new GameObject("ProjectileRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<ProjectileRoot>();
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

        private static Entity AoeScopeEntity(AoeRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo scopeEntityField = typeof(AoeRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(root);
        }

        private static Entity ProjectileScopeEntity(ProjectileRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo scopeEntityField = typeof(ProjectileRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(root);
        }

        private static void Cleanup(params GameObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                Object.Destroy(objects[i]);
            }
        }

        private sealed class AoeTargetProbe : MonoBehaviour, IAoeTarget
        {
            private int targetId;
            private int targetMask;
            private float radius;

            public int TargetId => targetId;
            public Vector2 AoeTargetPosition => transform.position;
            public float AoeTargetRadius => radius;
            public Vector2 AoeTargetHalfExtents => Vector2.one * radius;
            public float AoeTargetRotationRadians => 0f;
            public CombatShapeType AoeTargetShapeType => CombatShapeType.Circle;
            public int AoeTargetMask => targetMask;
            public bool IsAoeTargetActive => true;
            public int HitCount { get; private set; }
            public float TotalDamage { get; private set; }
            public DamageSnapshot LastDamage { get; private set; }

            public void Configure(int targetId, int targetMask, float radius)
            {
                this.targetId = targetId;
                this.targetMask = targetMask;
                this.radius = radius;
            }

            public void ReceiveAoeHit(DamageSnapshot damage)
            {
                HitCount++;
                LastDamage = damage;
                TotalDamage += damage.Amount;
            }
        }
    }
}
