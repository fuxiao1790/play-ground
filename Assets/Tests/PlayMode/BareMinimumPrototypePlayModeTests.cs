using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.Mob.Behaviours;
using PlayGround.Mob.Triggers;
using PlayGround.Player;
using PlayGround.Game;
using PlayGround.Spawn;
using PlayGround.System.Projectile;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.Tests.PlayMode
{
    public sealed class BareMinimumPrototypePlayModeTests
    {
        [Test]
        public void PlayerMovementMovesBodyFromInput()
        {
            GameObject player = new("PlayerMovementTest");
            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            var movement = new PlayerMovement(body, 5f);

            movement.SetMoveInput(Vector2.right);
            movement.FixedTick();

            Assert.That(body.linearVelocity.x, Is.GreaterThan(0f));
            Object.Destroy(player);
        }

        [Test]
        public void PlayerMovementDashUsesMoveDirection()
        {
            GameObject player = new("PlayerDashTest");
            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            var movement = new PlayerMovement(body, 5f, dashSpeed: 12f);

            movement.SetMoveInput(Vector2.up);
            movement.TryStartDash(Vector2.right);
            movement.FixedTick();

            Assert.That(movement.IsDashing, Is.True);
            Assert.That(body.linearVelocity.y, Is.GreaterThan(5f));
            Assert.That(body.linearVelocity.x, Is.EqualTo(0f).Within(0.001f));
            Object.Destroy(player);
        }

        [Test]
        public void PlayerHealthSoftDeathDisablesBodyAndHurtbox()
        {
            GameObject player = new("PlayerHealthTest");
            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            CircleCollider2D bodyCollider = player.AddComponent<CircleCollider2D>();
            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(player.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            SpriteRenderer renderer = player.AddComponent<SpriteRenderer>();
            var animatorDriver = new PlayerAnimatorDriver(null, renderer);
            var health = new PlayerHealth(body, bodyCollider, hurtbox, renderer, animatorDriver, 5f, 0.08f);

            health.TakeDamage(new DamageSnapshot(5f));

            Assert.That(health.IsAlive, Is.False);
            Assert.That(body.simulated, Is.False);
            Assert.That(bodyCollider.enabled, Is.False);
            Assert.That(hurtbox.enabled, Is.False);
            Assert.That(renderer.enabled, Is.False);
            Object.Destroy(player);
        }

        [Test]
        public void ProjectileHitReducesDummyHealth()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), ProjectileShapeType.Circle));
            projectileRoot.Step(0.01f);

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileRootDoesNotCreateProjectileSceneChildren()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), ProjectileShapeType.Circle));
            projectileRoot.Step(0.01f);

            Assert.That(projectileObject.transform.childCount, Is.EqualTo(0));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileLifetimeDeactivatesEcsEntity()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);

            projectileRoot.Spawn(new ProjectileSpawnCommand(new Vector2(50f, 50f), Vector2.right, 0f, 0f, 1f, new DamageSnapshot(4f), ProjectileShapeType.Circle));
            projectileRoot.Step(0.01f);

            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileTargetMaskFiltersHits()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(4f),
                ProjectileShapeType.Circle,
                targetMask: 2);

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.01f);

            Assert.That(mob.CurrentHealth, Is.EqualTo(10f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileTargetsUseHurtboxLayerMask()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out _, out GameObject mobObject, out MobRoot mob);
            int mobHurtboxLayer = LayerMask.NameToLayer(GameplayLayers.MobHurtbox);
            Assume.That(mobHurtboxLayer, Is.GreaterThanOrEqualTo(0));
            mobObject.GetComponent<Collider2D>().gameObject.layer = mobHurtboxLayer;

            Assert.That(mob.ProjectileTargetMask, Is.EqualTo(1 << mobHurtboxLayer));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void GameRootBindsTaggedMobToProjectileRootByTagAndLayer()
        {
            int mobHurtboxLayer = LayerMask.NameToLayer(GameplayLayers.MobHurtbox);
            Assume.That(mobHurtboxLayer, Is.GreaterThanOrEqualTo(0));

            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            projectileObject.tag = GameplayTags.PlayerProjectileRoot;
            projectileRoot.ConfigureTargetBinding(1 << mobHurtboxLayer, GameplayTags.Mob);
            mobObject.tag = GameplayTags.Mob;
            mobObject.GetComponent<Collider2D>().gameObject.layer = mobHurtboxLayer;

            GameObject gameRootObject = new("GameRoot");
            gameRootObject.AddComponent<GameRoot>();

            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(4f),
                ProjectileShapeType.Circle,
                targetMask: projectileRoot.TargetMask);

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.01f);

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(gameRootObject);
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileDirectDamageToggleSuppressesTargetDamageButKeepsHitEvent()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);
            int hitCount = 0;
            projectileRoot.ProjectileHit += _ => hitCount++;

            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(4f),
                ProjectileShapeType.Circle,
                directDamageEnabled: false);

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.01f);

            Assert.That(hitCount, Is.EqualTo(1));
            Assert.That(mob.CurrentHealth, Is.EqualTo(10f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void PiercingProjectileCanRepeatHitAfterCooldown()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                ProjectileShapeType.Circle,
                pierceCount: 2,
                repeatHitCooldownSeconds: 0.02f);

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.01f);
            projectileRoot.Step(0.01f);
            projectileRoot.Step(0.02f);

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileChildSpawnedChildAppliesChildPayloadDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mobObject.transform.position = new Vector2(50f, 50f);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnCommand(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                ProjectileShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), ProjectileShapeType.Circle, 0f, new DamageSnapshot(1f)),
                directDamageEnabled: false);

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.02f);
            projectileRoot.Step(0.001f);

            Assert.That(mob.CurrentHealth, Is.EqualTo(9f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileHitPayloadDispatchesToSourceAndTargetActors()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);
            GameObject sourceObject = new("ProjectileSource");
            HitActorProbe sourceProbe = sourceObject.AddComponent<HitActorProbe>();
            EntityId sourceNodeId = sourceObject.GetEntityId();

            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                ProjectileShapeType.Circle,
                sourceNodeId: sourceNodeId);

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.01f);

            Assert.That(sourceProbe.CallCount, Is.EqualTo(1));
            Assert.That(sourceProbe.LastRole, Is.EqualTo(ProjectileHitActorRole.Source));
            Assert.That(sourceProbe.LastPayload.SourceNodeId, Is.EqualTo(sourceNodeId));
            Assert.That(mob.CurrentHealth, Is.EqualTo(8f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(sourceObject);
        }

        [Test]
        public void ProjectileChildSpawnedChildrenArePreparedForRenderBatchOnNextStep()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);

            var command = new ProjectileSpawnCommand(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                ProjectileShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), ProjectileShapeType.Circle, 0f, new DamageSnapshot(1f)));

            projectileRoot.Spawn(command);
            projectileRoot.Step(0.02f);
            projectileRoot.Step(0.001f);

            Assert.That(RenderInstanceCount(projectileRoot, 0), Is.EqualTo(3));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileChildSpawnRejectsUnsupportedRenderType()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            var command = new ProjectileSpawnCommand(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                ProjectileShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 16, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), ProjectileShapeType.Circle, 0f, new DamageSnapshot(1f)));

            Assert.Throws<global::System.InvalidOperationException>(() => projectileRoot.Spawn(command));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void SpawnerUsesPoolAndEnforcesGlobalCap()
        {
            CreateSpawnFixture(1, 0, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);

            MobRoot first = spawner.RequestSpawn(point);
            MobRoot second = spawner.RequestSpawn(point);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null);
            Assert.That(spawner.ActiveMobCount(), Is.EqualTo(1));
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [Test]
        public void SpawnPointLocalCapReopensAfterSoftDeath()
        {
            CreateSpawnFixture(4, 1, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);

            MobRoot first = spawner.RequestSpawn(point);
            MobRoot blocked = spawner.RequestSpawn(point);
            first.SoftDie();
            MobRoot second = spawner.RequestSpawn(point);

            Assert.That(blocked, Is.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(point.ActiveLocalMobCount, Is.EqualTo(1));
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [Test]
        public void SpawnerActivatesMobsClonedFromInactivePrefab()
        {
            CreateSpawnFixture(1, 0, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);
            prefabObject.SetActive(false);

            MobRoot mob = spawner.RequestSpawn(point);

            Assert.That(mob, Is.Not.Null);
            Assert.That(mob.gameObject.activeSelf, Is.True);
            Assert.That(mob.IsProjectileTargetActive, Is.True);
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [Test]
        public void GameRootAcceptsAuthoredSpawnerWhenNoSceneMobsAreAuthored()
        {
            GameObject projectileObject = new("ProjectileRoot");
            projectileObject.SetActive(false);
            ProjectileRoot projectileRoot = projectileObject.AddComponent<ProjectileRoot>();
            projectileRoot.Configure(Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f));
            projectileObject.SetActive(true);

            GameObject spawnerObject = new("MobSpawnerRoot");
            spawnerObject.AddComponent<MobSpawnerRoot>();

            GameObject gameRootObject = new("GameRoot");
            gameRootObject.SetActive(false);
            GameRoot gameRoot = gameRootObject.AddComponent<GameRoot>();
            gameRoot.Configure(projectileRoot, new MobRoot[0]);
            gameRootObject.SetActive(true);

            Assert.That(Object.FindAnyObjectByType<MobSpawnerRoot>(), Is.Not.Null);
            Object.Destroy(gameRootObject);
            Object.Destroy(spawnerObject);
            Object.Destroy(projectileObject);
        }

        private static void CreateProjectileHitFixture(
            out GameObject projectileObject,
            out ProjectileRoot projectileRoot,
            out GameObject mobObject,
            out MobRoot mob)
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            projectileObject = new GameObject("ProjectileRoot");
            projectileObject.SetActive(false);
            projectileRoot = projectileObject.AddComponent<ProjectileRoot>();
            projectileRoot.Configure(sprite);
            projectileObject.SetActive(true);

            mobObject = new GameObject("Mob");
            mobObject.SetActive(false);
            Rigidbody2D body = mobObject.AddComponent<Rigidbody2D>();
            CircleCollider2D hurtbox = mobObject.AddComponent<CircleCollider2D>();
            hurtbox.isTrigger = true;
            SpriteRenderer renderer = mobObject.AddComponent<SpriteRenderer>();
            mob = mobObject.AddComponent<MobRoot>();
            mob.Configure(body, hurtbox, hurtbox, renderer, null);
            mob.ConfigureAuthoring(
                new MobBehaviour[] { ScriptableObject.CreateInstance<WanderBehaviour>() },
                new MobTrigger[] { ScriptableObject.CreateInstance<HurtRecoveryTrigger>() },
                new[] { new MobTriggerBehaviourMapping { triggerKey = MobRoot.DefaultTriggerKey, behaviourKey = "wander" } },
                10f,
                0f,
                0.5f);
            mobObject.SetActive(true);
        }

        private static int RenderInstanceCount(ProjectileRoot projectileRoot, int typeId)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var batchField = typeof(ProjectileRoot).GetField("renderBatchEntitiesByType", Flags);
            var entityManagerField = typeof(ProjectileRoot).GetField("entityManager", Flags);
            var batches = (Dictionary<int, Entity>)batchField.GetValue(projectileRoot);
            var entityManager = (EntityManager)entityManagerField.GetValue(projectileRoot);

            if (!batches.TryGetValue(typeId, out Entity batchEntity)
                || batchEntity == Entity.Null
                || !entityManager.Exists(batchEntity)
                || !entityManager.HasBuffer<ProjectileRenderElement>(batchEntity))
            {
                return 0;
            }

            return entityManager.GetBuffer<ProjectileRenderElement>(batchEntity).Length;
        }

        private static void CreateSpawnFixture(
            int globalCap,
            int localCap,
            out GameObject spawnerObject,
            out MobSpawnerRoot spawner,
            out SpawnPoint point,
            out GameObject prefabObject,
            out MobSpawnPool pool)
        {
            prefabObject = CreateMobPrefab("SpawnedMobPrefab", out MobRoot prefab);
            pool = ScriptableObject.CreateInstance<MobSpawnPool>();
            pool.Configure(new[] { prefab });

            spawnerObject = new GameObject("Spawner");
            spawnerObject.SetActive(false);
            spawner = spawnerObject.AddComponent<MobSpawnerRoot>();
            GameObject pointObject = new("SpawnPoint");
            pointObject.transform.SetParent(spawnerObject.transform, false);
            point = pointObject.AddComponent<SpawnPoint>();
            point.Configure(pool, 10f, 0f, localCap);
            spawner.Configure(pool, globalCap, points: new[] { point });
            spawnerObject.SetActive(true);
        }

        private static GameObject CreateMobPrefab(string name, out MobRoot mob)
        {
            GameObject mobObject = new(name);
            mobObject.SetActive(false);
            Rigidbody2D body = mobObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            CircleCollider2D bodyCollider = mobObject.AddComponent<CircleCollider2D>();
            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(mobObject.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.isTrigger = true;
            SpriteRenderer renderer = mobObject.AddComponent<SpriteRenderer>();
            mob = mobObject.AddComponent<MobRoot>();
            mob.Configure(body, bodyCollider, hurtbox, renderer, null);
            mob.ConfigureAuthoring(
                new MobBehaviour[] { ScriptableObject.CreateInstance<WanderBehaviour>() },
                new MobTrigger[] { ScriptableObject.CreateInstance<HurtRecoveryTrigger>() },
                new[] { new MobTriggerBehaviourMapping { triggerKey = MobRoot.DefaultTriggerKey, behaviourKey = "wander" } },
                10f,
                0f,
                0.5f);
            mobObject.SetActive(true);
            return mobObject;
        }

        private sealed class HitActorProbe : MonoBehaviour, IProjectileHitActor
        {
            public EntityId ProjectileHitNodeId => gameObject.GetEntityId();
            public int CallCount { get; private set; }
            public ProjectileHitActorRole LastRole { get; private set; }
            public ProjectileHitPayload LastPayload { get; private set; }

            public void ReceiveProjectileHitPayload(
                in ProjectileHitPayload payload,
                in ProjectileHitContext context,
                ProjectileHitActorRole role)
            {
                CallCount++;
                LastRole = role;
                LastPayload = payload;
            }
        }
    }
}
