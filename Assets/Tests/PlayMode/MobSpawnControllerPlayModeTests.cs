using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.Spawn;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Projectiles;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGround.Tests.PlayMode
{
    public sealed class MobSpawnControllerPlayModeTests
    {
        [UnityTest]
        public IEnumerator PoolReuseReturnsSameMobInstance()
        {
            CreateFixture(1, 0f, out GameObject combatObject, out _, out GameObject prefabObject, out SpawnController controller);

            controller.Spawn();
            MobRoot first = SingleActiveMob();
            first.SoftDie();
            yield return null;

            controller.Spawn();
            MobRoot second = SingleActiveMob();

            Assert.That(second, Is.SameAs(first));
            Object.Destroy(controller.gameObject);
            Object.Destroy(prefabObject);
            Object.Destroy(combatObject);
        }

        [UnityTest]
        public IEnumerator ContinuousStreamHoldsCap()
        {
            CreateFixture(3, 100f, out GameObject combatObject, out _, out GameObject prefabObject, out SpawnController controller);

            Time.captureDeltaTime = 0.1f;
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(controller.ActiveCount, Is.EqualTo(3));
            Assert.That(ActiveMobs().Length, Is.EqualTo(3));
            Object.Destroy(controller.gameObject);
            Object.Destroy(prefabObject);
            Object.Destroy(combatObject);
        }

        [UnityTest]
        public IEnumerator ReusedMobRegistersFreshProxyAndTakesDamage()
        {
            CreateFixture(1, 0f, out GameObject combatObject, out CombatRoot combatRoot, out GameObject prefabObject, out SpawnController controller);

            controller.Spawn();
            MobRoot first = SingleActiveMob();
            first.SoftDie();
            yield return null;

            controller.Spawn();
            yield return null;
            MobRoot reused = SingleActiveMob();
            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.IsCombatTargetActive, Is.True);
            Assert.That(reused.CombatTargetProxy, Is.Not.EqualTo(Entity.Null));
            Assert.That(World.DefaultGameObjectInjectionWorld.EntityManager.Exists(reused.CombatTargetProxy), Is.True);

            combatRoot.Spawn(
                new ProjectileSpawnRequest(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle),
                CombatFaction.Player);
            yield return null;

            Assert.That(reused.CurrentHealth, Is.EqualTo(6f).Within(0.001f));
            Object.Destroy(controller.gameObject);
            Object.Destroy(prefabObject);
            Object.Destroy(combatObject);
        }

        [UnityTest]
        public IEnumerator ReusedMobHasFreshPerLifeState()
        {
            CreateFixture(1, 0f, out GameObject combatObject, out _, out GameObject prefabObject, out SpawnController controller);

            controller.Spawn();
            MobRoot first = SingleActiveMob();
            first.SoftDie();
            yield return null;

            controller.Spawn();
            MobRoot reused = SingleActiveMob();
            Rigidbody2D body = reused.GetComponent<Rigidbody2D>();
            Collider2D collider = reused.GetComponent<Collider2D>();
            SpriteRenderer renderer = reused.GetComponent<SpriteRenderer>();

            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.IsAlive, Is.True);
            Assert.That(reused.CurrentHealth, Is.EqualTo(reused.MaxHealth).Within(0.001f));
            Assert.That(collider.enabled, Is.True);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(body.simulated, Is.True);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Object.Destroy(controller.gameObject);
            Object.Destroy(prefabObject);
            Object.Destroy(combatObject);
        }

        [UnityTest]
        public IEnumerator ReclaimAccountingReturnsKilledMobsToPool()
        {
            CreateFixture(3, 0f, out GameObject combatObject, out _, out GameObject prefabObject, out SpawnController controller);

            controller.Spawn();
            controller.Spawn();
            controller.Spawn();
            MobRoot[] firstBatch = ActiveMobs();
            Assert.That(controller.ActiveCount, Is.EqualTo(3));

            for (int i = 0; i < firstBatch.Length; i++)
            {
                firstBatch[i].SoftDie();
            }

            yield return null;
            Assert.That(controller.ActiveCount, Is.EqualTo(0));

            controller.Spawn();
            controller.Spawn();
            controller.Spawn();
            var reused = new HashSet<MobRoot>(ActiveMobs());

            Assert.That(reused.Count, Is.EqualTo(3));
            for (int i = 0; i < firstBatch.Length; i++)
            {
                Assert.That(reused.Contains(firstBatch[i]), Is.True);
            }

            Object.Destroy(controller.gameObject);
            Object.Destroy(prefabObject);
            Object.Destroy(combatObject);
        }

        [Test]
        public void SpawnPointSamplesInsideDisabledPolygonArea()
        {
            GameObject pointObject = new("PolygonSpawnPoint");
            PolygonCollider2D polygon = pointObject.AddComponent<PolygonCollider2D>();
            polygon.SetPath(
                0,
                new[]
                {
                    new Vector2(-1f, -1f),
                    new Vector2(1f, -1f),
                    new Vector2(0f, 1f)
                });
            polygon.enabled = false;
            SpawnPoint point = pointObject.AddComponent<SpawnPoint>();
            var rng = new global::System.Random(17);

            for (int i = 0; i < 32; i++)
            {
                Vector2 sampled = point.SamplePosition(rng);
                Vector2 local = pointObject.transform.InverseTransformPoint(sampled);
                Assert.That(IsInsideTriangle(local), Is.True);
            }

            Object.Destroy(pointObject);
        }

        private static void CreateFixture(
            int cap,
            float spawnsPerSecond,
            out GameObject combatObject,
            out CombatRoot combatRoot,
            out GameObject prefabObject,
            out SpawnController controller)
        {
            combatObject = new GameObject("CombatRoot");
            combatObject.SetActive(false);
            combatRoot = combatObject.AddComponent<CombatRoot>();
            combatRoot.Configure(CombatAtlasTestFixture.Sprite);
            combatRoot.ConfigureAtlas(CombatAtlasTestFixture.Atlas);
            combatObject.SetActive(true);

            MobRoot prefab = CreateMobPrefab(out prefabObject);
            MobSpawnTable table = ScriptableObject.CreateInstance<MobSpawnTable>();
            table.Configure(new[] { prefab });

            ContinuousStreamBehaviour behaviour = ScriptableObject.CreateInstance<ContinuousStreamBehaviour>();
            SetField(behaviour, "spawnsPerSecond", spawnsPerSecond);
            SetField(behaviour, "maxSpawnsPerTick", 8);

            FixedPointPlacement placement = ScriptableObject.CreateInstance<FixedPointPlacement>();
            GameObject controllerObject = new("SpawnController");
            controllerObject.SetActive(false);
            controller = controllerObject.AddComponent<SpawnController>();
            GameObject pointObject = new("SpawnPoint");
            pointObject.transform.SetParent(controllerObject.transform, false);
            pointObject.transform.position = Vector2.zero;
            SpawnPoint point = pointObject.AddComponent<SpawnPoint>();

            SetField(controller, "table", table);
            SetField(controller, "behaviour", behaviour);
            SetField(controller, "placement", placement);
            SetField(controller, "spawnPoints", new[] { point });
            SetField(controller, "cap", cap);
            SetField(controller, "prewarm", cap);
            SetField(controller, "randomSeed", 123);
            controllerObject.SetActive(true);
            controller.Bind(combatRoot, null, null);
        }

        private static MobRoot CreateMobPrefab(out GameObject prefabObject)
        {
            prefabObject = new GameObject("MobPrefab");
            prefabObject.SetActive(false);
            Rigidbody2D body = prefabObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            CircleCollider2D hurtbox = prefabObject.AddComponent<CircleCollider2D>();
            hurtbox.isTrigger = true;
            SpriteRenderer renderer = prefabObject.AddComponent<SpriteRenderer>();
            MobRoot mob = prefabObject.AddComponent<MobRoot>();
            mob.Configure(body, hurtbox, hurtbox, renderer, null);
            mob.ConfigureAuthoring(10f, 0f, 0.5f);
            return mob;
        }

        private static MobRoot SingleActiveMob()
        {
            MobRoot[] mobs = ActiveMobs();
            Assert.That(mobs.Length, Is.EqualTo(1));
            return mobs[0];
        }

        private static MobRoot[] ActiveMobs() =>
            Object.FindObjectsByType<MobRoot>(FindObjectsInactive.Exclude);

        private static bool IsInsideTriangle(Vector2 point)
        {
            Vector2 a = new(-1f, -1f);
            Vector2 b = new(1f, -1f);
            Vector2 c = new(0f, 1f);
            float d1 = Sign(point, a, b);
            float d2 = Sign(point, b, c);
            float d3 = Sign(point, c, a);
            bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNegative && hasPositive);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        private static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
