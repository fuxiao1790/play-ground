using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.Tests.PlayMode
{
    public sealed class AoePlayModeTests
    {
        private const int AoeTypeId = 0;
        private const int DefaultTargetMask = 1;
        private static int nextTargetId = 1000;

        [Test]
        public void AoePulseHitsOverlappingTargetOnce()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(Vector2.zero, DefaultTargetMask, 2f));
            root.Step(0.01f);
            root.Step(0.01f);

            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.TotalDamage, Is.EqualTo(2f));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void AoePulseDoesNotHitOutsideShape()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(new Vector2(3f, 0f), DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(Vector2.zero, DefaultTargetMask, 2f));
            root.Step(0.01f);

            Assert.That(target.HitCount, Is.EqualTo(0));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void AoeTargetMaskFiltersHits()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(Vector2.zero, targetMask: 2);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(Vector2.zero, targetMask: 4, 2f));
            root.Step(0.01f);

            Assert.That(target.HitCount, Is.EqualTo(0));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void AoeHitReplayCallsTargetDamageCallback()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);
            int replayCount = 0;
            AoeHitContext replayContext = default;
            root.AoeHit += context =>
            {
                replayCount++;
                replayContext = context;
            };

            root.Spawn(Command(Vector2.zero, DefaultTargetMask, 3f));
            root.Step(0.01f);

            Assert.That(replayCount, Is.EqualTo(1));
            Assert.That(replayContext.Target, Is.EqualTo(target));
            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.LastDamage.Amount, Is.EqualTo(3f));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void AoeEntityIsReusedAfterPulse()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(Vector2.zero, DefaultTargetMask, 1f));
            root.Step(0.01f);
            Entity firstEntity = FirstScopedAoeEntity(root);

            root.Spawn(Command(Vector2.zero, DefaultTargetMask, 1f));
            root.Step(0.01f);
            Entity reusedEntity = FirstScopedAoeEntity(root);

            Assert.That(reusedEntity, Is.EqualTo(firstEntity));
            Assert.That(CountScopedAoeEntities(root), Is.EqualTo(1));
            Assert.That(target.HitCount, Is.EqualTo(2));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void AoeRootCreatesNoPerAoeLiveDamageOrColliderObjects()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            for (int i = 0; i < 3; i++)
            {
                root.Spawn(Command(Vector2.zero, DefaultTargetMask, 1f));
                root.Step(0.01f);
            }

            Assert.That(rootObject.transform.childCount, Is.EqualTo(0));
            Assert.That(rootObject.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Assert.That(CountScopedAoeEntities(root), Is.EqualTo(1));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [Test]
        public void AoeSystemsIgnoreCommonCombatEntityWithoutAoeTag()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity entity = entityManager.CreateEntity(
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatHitComponent),
                typeof(AoeActiveTag));

            entityManager.SetComponentData(entity, new CombatKinematicsComponent
            {
                Position = new Unity.Mathematics.float2(1f, 2f),
                Velocity = new Unity.Mathematics.float2(5f, 0f)
            });
            entityManager.SetComponentData(entity, new CombatCollisionComponent
            {
                ShapeType = CombatShapeType.Circle,
                Radius = 0.5f,
                BoundsMin = new Unity.Mathematics.float2(0.5f, 1.5f),
                BoundsMax = new Unity.Mathematics.float2(1.5f, 2.5f)
            });
            entityManager.SetComponentData(entity, new CombatHitComponent
            {
                TargetMask = ~0,
                DamageAmount = 1f,
                DirectDamageEnabled = true
            });

            root.Step(0.01f);

            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entity);
            Assert.That(kinematics.Position.x, Is.EqualTo(1f));
            Assert.That(kinematics.Position.y, Is.EqualTo(2f));

            entityManager.DestroyEntity(entity);
            Cleanup(rootObject, templateObject);
        }

        [Test]
        public void ProjectileAndAoeRootsShareDefaultWorldButUseSeparateScopes()
        {
            CreateProjectileRoot(out GameObject projectileObject, out ProjectileRoot projectileRoot);
            CreateAoeFixture(out GameObject aoeObject, out AoeRoot aoeRoot, out GameObject templateObject);
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

        [Test]
        public void AoeCountersTrackSpawnDespawnHitAndRenderBatchFields()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(Vector2.zero, DefaultTargetMask, 2f));
            root.Step(0.01f);

            AoeRuntimeCounters counters = root.Counters;
            Assert.That(counters.SpawnedAoes, Is.EqualTo(1));
            Assert.That(counters.DespawnedOrReusedAoes, Is.EqualTo(1));
            Assert.That(counters.HitEvents, Is.EqualTo(1));
            Assert.That(counters.RenderBatches, Is.GreaterThanOrEqualTo(0));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        private static AoeSpawnCommand Command(Vector2 position, int targetMask, float damage)
        {
            return new AoeSpawnCommand(
                AoeTypeId,
                position,
                targetMask,
                new DamageSnapshot(damage),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 1f);
        }

        private static void CreateAoeFixture(
            out GameObject rootObject,
            out AoeRoot root,
            out GameObject templateObject)
        {
            templateObject = new GameObject("AoeTemplate");
            templateObject.SetActive(false);
            CircleCollider2D shape = templateObject.AddComponent<CircleCollider2D>();
            shape.radius = 1f;

            var definition = new AoeTypeDefinition();
            definition.Configure(AoeTypeId, templateObject, shape, 1f);

            rootObject = new GameObject("AoeRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<AoeRoot>();
            root.Configure(new[] { definition }, ~0);
            rootObject.SetActive(true);
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

        private static Entity FirstScopedAoeEntity(AoeRoot root)
        {
            Entity result = Entity.Null;
            CountScopedAoes(
                root,
                entity =>
                {
                    result = entity;
                    return true;
                });
            Assert.That(result, Is.Not.EqualTo(Entity.Null));
            return result;
        }

        private static int CountScopedAoeEntities(AoeRoot root)
        {
            return CountScopedAoes(root, _ => true);
        }

        private static int CountScopedAoes(AoeRoot root, global::System.Func<Entity, bool> predicate)
        {
            Entity scope = AoeScopeEntity(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AoeIdentityComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int count = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Scope == scope && predicate(entities[i]))
                {
                    count++;
                }
            }

            return count;
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
