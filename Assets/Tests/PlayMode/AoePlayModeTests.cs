using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
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
