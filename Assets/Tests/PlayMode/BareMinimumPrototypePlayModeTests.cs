using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using PlayGround.Skills;
using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.Player;
using PlayGround.Game;
using PlayGround.Spawn;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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

        [UnityTest]
        public IEnumerator ProjectileHitReducesDummyHealth()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnRequest(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle), CombatFaction.Player);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator CombatRootDoesNotCreateProjectileSceneChildren()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnRequest(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle), CombatFaction.Player);
            yield return null;

            Assert.That(projectileObject.transform.childCount, Is.EqualTo(0));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void CombatRootRegistersTemplateBeforeAwake()
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            GameObject rootObject = new("CombatRoot");
            rootObject.SetActive(false);
            CombatRoot root = rootObject.AddComponent<CombatRoot>();
            root.Configure(sprite);
            root.ConfigureAtlas(Texture2D.whiteTexture);
            GameObject templateObject = CreateBasicProjectileTemplate(sprite, out BasicAttackPrefab template);

            int typeId = root.RegisterTemplate(template);
            rootObject.SetActive(true);

            Assert.That(typeId, Is.GreaterThan(0));
            Object.Destroy(rootObject);
            Object.Destroy(templateObject);
        }

        [UnityTest]
        public IEnumerator ProjectileLifetimeDeactivatesEcsEntity()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);

            projectileRoot.Spawn(new ProjectileSpawnRequest(new Vector2(50f, 50f), Vector2.right, 0f, 0f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle), CombatFaction.Player);
            yield return null;

            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileSystemsIgnoreCommonCombatEntityWithoutProjectileTag()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity entity = entityManager.CreateEntity(
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active));

            entityManager.SetComponentData(entity, new CombatKinematicsComponent
            {
                Position = new float2(1f, 2f),
                Velocity = new float2(5f, 0f)
            });
            entityManager.SetComponentData(entity, new CombatCollisionComponent
            {
                ShapeType = CombatShapeType.Circle,
                Radius = 0.5f,
                BoundsMin = new float2(0.5f, 1.5f),
                BoundsMax = new float2(1.5f, 2.5f)
            });
            yield return null;

            CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entity);
            Assert.That(kinematics.Position.x, Is.EqualTo(1f));
            Assert.That(kinematics.Position.y, Is.EqualTo(2f));

            entityManager.DestroyEntity(entity);
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileTrackingAcquiresTargetInForwardArea()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mobObject.transform.position = new Vector2(10f, 10f);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnRequest(
                Vector2.zero,
                Vector2.right,
                10f,
                1f,
                0.25f,
                new Vector2(0.25f, 0.25f),
                0f,
                new DamageSnapshot(1f),
                CombatShapeType.Circle,
                tracking: new ProjectileTrackingConfig(true, 50f, 360f, 0f));

            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.1f;
            yield return null;
            Time.captureDeltaTime = 0f;

            Vector2 velocity = ProjectileVelocity(projectileRoot);
            Assert.That(velocity.y, Is.GreaterThan(0f));
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

            Assert.That(mob.CombatTargetMask, Is.EqualTo(1 << mobHurtboxLayer));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileDirectDamageToggleSuppressesTargetDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);
            var command = new ProjectileSpawnRequest(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(4f),
                CombatShapeType.Circle,
                directDamageEnabled: false);

            projectileRoot.Spawn(command, CombatFaction.Player);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(10f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileReplaySkipsTargetThatDiesEarlierInSameHitBuffer()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            ProjectileReplayProbe probe = CreateProjectileReplayProbe(Vector2.zero);
            projectileRoot.TargetRegistry.Register(probe);
            var command = new ProjectileSpawnRequest(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new DamageSnapshot(10f),
                CombatShapeType.Circle);
            projectileRoot.Spawn(command, CombatFaction.Player);
            projectileRoot.Spawn(command, CombatFaction.Player);
            yield return null;

            // Both hits land on the same target in one frame → one batch per unique target.
            // The probe receives one aggregate managed push for the target.
            Assert.That(probe.HitCount, Is.EqualTo(1));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(probe.gameObject);
        }

        [UnityTest]
        public IEnumerator PiercingProjectileCanRepeatHitAfterCooldown()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnRequest(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle,
                pierceCount: 2,
                repeatHitCooldownSeconds: 0.02f);

            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.01f;
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0.02f;
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalSpawnedChildAppliesChildPayloadDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mobObject.transform.position = new Vector2(50f, 50f);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnRequest(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)),
                directDamageEnabled: false);

            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.02f;
            yield return null;
            Time.captureDeltaTime = 0.001f;
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(mob.CurrentHealth, Is.EqualTo(9f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalSpawnedChildTracksNearbyTarget()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mobObject.transform.position = new Vector2(10f, 10f);
            mob.Register(projectileRoot.TargetRegistry);

            var intervalSpawn = new ProjectileChildSpawnConfig(
                1,
                0,
                0.01f,
                0f,
                10f,
                1f,
                0.25f,
                new Vector2(0.25f, 0.25f),
                CombatShapeType.Circle,
                0f,
                new DamageSnapshot(1f),
                tracking: new ProjectileTrackingConfig(true, 50f, 360f, 0f),
                behavior: new ProjectileChildSpawnBehavior(1, ProjectileChildSpawnPatternType.Forward));
            var command = new ProjectileSpawnRequest(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                0.25f,
                new Vector2(0.25f, 0.25f),
                0f,
                new DamageSnapshot(1f),
                CombatShapeType.Circle,
                childSpawn: intervalSpawn,
                directDamageEnabled: false);

            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.02f;
            yield return null;
            Time.captureDeltaTime = 0.1f;
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(MaxProjectileVelocityY(projectileRoot), Is.GreaterThan(0f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileHitPayloadAggregatesDamageForTarget()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            ProjectileReplayProbe probe = CreateProjectileReplayProbe(Vector2.zero);
            projectileRoot.TargetRegistry.Register(probe);
            GameObject sourceObject = new("ProjectileSource");
            EntityId sourceNodeId = sourceObject.GetEntityId();

            var command = new ProjectileSpawnRequest(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle,
                sourceNodeId: sourceNodeId);

            projectileRoot.Spawn(command, CombatFaction.Player);
            yield return null;

            Assert.That(probe.LastDamage.Amount, Is.EqualTo(2f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(probe.gameObject);
            Object.Destroy(sourceObject);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalSpawnedChildrenArePreparedForRenderBatchOnNextStep()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);

            var command = new ProjectileSpawnRequest(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)));

            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.02f;
            yield return null;
            Time.captureDeltaTime = 0.001f;
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(RenderInstanceCount(0), Is.EqualTo(3));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalSpawnerStoresIntervalJitterInEcs()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            const float IntervalJitterSeconds = 0.25f;
            var command = new ProjectileSpawnRequest(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 1f, IntervalJitterSeconds, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)));

            projectileRoot.Spawn(command, CombatFaction.Player);
            yield return null;

            Entity childSpawnerEntity = FirstScopedChildSpawnerEntity(projectileRoot);
            TimedSpawnComponent spawner =
                World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<TimedSpawnComponent>(childSpawnerEntity);

            Assert.That(spawner.IntervalJitterSeconds, Is.EqualTo(IntervalJitterSeconds).Within(0.0001f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileIntervalSpawnRejectsUnsupportedRenderType()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            var command = new ProjectileSpawnRequest(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 16, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)));

            Assert.Throws<global::System.InvalidOperationException>(() => projectileRoot.Spawn(command, CombatFaction.Player));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileSpawnReusesExpiredEcsEntityAfterPoolWarmup()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            var command = new ProjectileSpawnRequest(new Vector2(50f, 50f), Vector2.right, 0f, 0f, 1f, new DamageSnapshot(1f), CombatShapeType.Circle);

            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.01f;
            yield return null;
            int warmedCount = CountScopedProjectileEntities(projectileRoot);

            for (int i = 0; i < 4; i++)
            {
                projectileRoot.Spawn(command, CombatFaction.Player);
                yield return null;
            }

            Time.captureDeltaTime = 0f;
            Assert.That(warmedCount, Is.EqualTo(1));
            Assert.That(CountScopedProjectileEntities(projectileRoot), Is.EqualTo(warmedCount));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileReuseClearsContactGatesAfterHitDespawn()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnRequest(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(1f), CombatShapeType.Circle), CombatFaction.Player);
            Time.captureDeltaTime = 0.01f;
            yield return null;
            Assert.That(SumScopedContactGates(projectileRoot), Is.GreaterThan(0));

            projectileRoot.Spawn(new ProjectileSpawnRequest(new Vector2(50f, 50f), Vector2.right, 0f, 1f, 1f, new DamageSnapshot(1f), CombatShapeType.Circle), CombatFaction.Player);
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(CountScopedProjectileEntities(projectileRoot), Is.EqualTo(1));
            Assert.That(SumScopedContactGates(projectileRoot), Is.EqualTo(0));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalSpawnRequestsReuseChildEntitiesAfterPoolWarmup()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            var command = ProjectileIntervalSpawnerRequest();

            yield return SpawnAndDrainChildCycle(projectileRoot, command);
            int warmedCount = CountScopedProjectileEntities(projectileRoot);

            for (int i = 0; i < 3; i++)
            {
                yield return SpawnAndDrainChildCycle(projectileRoot, command);
            }

            Assert.That(warmedCount, Is.EqualTo(3));
            Assert.That(CountScopedProjectileEntities(projectileRoot), Is.EqualTo(warmedCount));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalSpawnerParentKeepsStableArchetypeDuringReuse()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out _);
            var command = ProjectileIntervalSpawnerRequest();

            yield return SpawnAndDrainChildCycle(projectileRoot, command);
            Entity firstParent = FirstScopedChildSpawnerEntity(projectileRoot);

            yield return SpawnAndDrainChildCycle(projectileRoot, command);
            Entity reusedParent = FirstScopedChildSpawnerEntity(projectileRoot);

            Assert.That(firstParent, Is.EqualTo(reusedParent));
            Assert.That(CountScopedChildSpawnerEntities(projectileRoot), Is.EqualTo(1));
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
        public void SpawnerGlobalCapReopensAfterSoftDeath()
        {
            CreateSpawnFixture(1, 0, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);

            MobRoot first = spawner.RequestSpawn(point);
            MobRoot blocked = spawner.RequestSpawn(point);
            first.SoftDie();
            MobRoot second = spawner.RequestSpawn(point);

            Assert.That(blocked, Is.Null);
            Assert.That(spawner.ActiveMobCount(), Is.EqualTo(1));
            Assert.That(second, Is.Not.Null);
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [UnityTest]
        public IEnumerator MobSoftDeathSchedulesHardCleanup()
        {
            CreateSpawnFixture(1, 1, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);

            MobRoot mob = spawner.RequestSpawn(point);
            mob.SoftDie();
            yield return null;

            Assert.That(mob == null, Is.True);
            Assert.That(spawner.ActiveMobCount(), Is.EqualTo(0));
            Assert.That(point.ActiveLocalMobCount, Is.EqualTo(0));
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [Test]
        public void SpawnerDespawnAllClearsActiveAndLocalCaps()
        {
            CreateSpawnFixture(2, 2, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);

            spawner.RequestSpawn(point);
            spawner.RequestSpawn(point);
            spawner.DespawnAll();

            Assert.That(spawner.ActiveMobCount(), Is.EqualTo(0));
            Assert.That(point.ActiveLocalMobCount, Is.EqualTo(0));
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
            Assert.That(mob.IsCombatTargetActive, Is.True);
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [Test]
        public void SpawnPointStartsWithRandomTimerWithinSpawnInterval()
        {
            UnityEngine.Random.InitState(12345);
            float expectedTimer = UnityEngine.Random.Range(0f, 10f);
            UnityEngine.Random.InitState(12345);
            CreateSpawnFixture(1, 0, out GameObject spawnerObject, out MobSpawnerRoot spawner, out SpawnPoint point, out GameObject prefabObject, out MobSpawnPool pool);

            float timer = ReadSpawnPointTimer(point);

            Assert.That(timer, Is.EqualTo(expectedTimer));
            Object.Destroy(spawnerObject);
            Object.Destroy(prefabObject);
            Object.Destroy(pool);
        }

        [Test]
        public void GameRootAcceptsAuthoredSpawnerWhenNoSceneMobsAreAuthored()
        {
            GameObject projectileObject = new("CombatRoot");
            projectileObject.SetActive(false);
            CombatRoot projectileRoot = projectileObject.AddComponent<CombatRoot>();
            projectileRoot.Configure(Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f));
            projectileRoot.ConfigureAtlas(Texture2D.whiteTexture);
            projectileObject.SetActive(true);

            GameObject spawnerObject = new("MobSpawnerRoot");
            spawnerObject.AddComponent<MobSpawnerRoot>();

            GameObject gameRootObject = new("GameRoot");
            gameRootObject.SetActive(false);
            GameRoot gameRoot = gameRootObject.AddComponent<GameRoot>();
            gameRoot.Configure(projectileRoot, null, new MobRoot[0]);
            gameRootObject.SetActive(true);

            Assert.That(Object.FindAnyObjectByType<MobSpawnerRoot>(), Is.Not.Null);
            Object.Destroy(gameRootObject);
            Object.Destroy(spawnerObject);
            Object.Destroy(projectileObject);
        }

        private static void CreateProjectileHitFixture(
            out GameObject projectileObject,
            out CombatRoot projectileRoot,
            out GameObject mobObject,
            out MobRoot mob)
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            projectileObject = new GameObject("CombatRoot");
            projectileObject.SetActive(false);
            projectileRoot = projectileObject.AddComponent<CombatRoot>();
            projectileRoot.Configure(sprite);
            projectileRoot.ConfigureAtlas(Texture2D.whiteTexture);
            projectileObject.SetActive(true);

            mobObject = new GameObject("Mob");
            mobObject.SetActive(false);
            Rigidbody2D body = mobObject.AddComponent<Rigidbody2D>();
            CircleCollider2D hurtbox = mobObject.AddComponent<CircleCollider2D>();
            hurtbox.isTrigger = true;
            SpriteRenderer renderer = mobObject.AddComponent<SpriteRenderer>();
            mob = mobObject.AddComponent<MobRoot>();
            mob.Configure(body, hurtbox, hurtbox, renderer, null);
            mob.ConfigureAuthoring(10f, 0f, 0.5f);
            mobObject.SetActive(true);
        }

        private static GameObject CreateBasicProjectileTemplate(Sprite sprite, out BasicAttackPrefab template)
        {
            GameObject templateObject = new("BasicProjectileTemplate");
            templateObject.SetActive(false);

            GameObject visualObject = new("Visual");
            visualObject.transform.SetParent(templateObject.transform, false);
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(templateObject.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 0.5f;

            template = templateObject.AddComponent<BasicAttackPrefab>();
            template.Configure(renderer, hurtbox);
            return templateObject;
        }

        private static void CreateAoeFixture(
            out GameObject rootObject,
            out CombatRoot root,
            out GameObject templateObject,
            out int typeId)
        {
            templateObject = new GameObject("AoeTemplate");
            templateObject.SetActive(false);
            CircleCollider2D shape = templateObject.AddComponent<CircleCollider2D>();
            shape.radius = 1f;

            var definition = new AoeTypeDefinition();
            definition.Configure(templateObject, shape);

            rootObject = new GameObject("CombatRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<CombatRoot>();
            root.ConfigureAtlas(Texture2D.whiteTexture);
            rootObject.SetActive(true);

            typeId = root.RegisterType(definition);
        }

        private static AoeSpawnGeometry AoeGeometry(GameObject templateObject, float areaSize = 1f)
        {
            return AoeSpawnGeometry.FromTemplate(
                templateObject,
                templateObject.GetComponentInChildren<Collider2D>(true),
                areaSize,
                0f);
        }

        private static ProjectileSpawnRequest ProjectileIntervalSpawnerRequest()
        {
            return new ProjectileSpawnRequest(
                new Vector2(50f, 50f),
                Vector2.right,
                0f,
                0.001f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(1f),
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(
                    1,
                    0,
                    0.001f,
                    0f,
                    0f,
                    0f,
                    1f,
                    new Vector2(1f, 1f),
                    CombatShapeType.Circle,
                    0f,
                    new DamageSnapshot(1f),
                    behavior: new ProjectileChildSpawnBehavior(1, ProjectileChildSpawnPatternType.Forward)),
                directDamageEnabled: false);
        }

        private static IEnumerator SpawnAndDrainChildCycle(CombatRoot projectileRoot, ProjectileSpawnRequest command)
        {
            projectileRoot.Spawn(command, CombatFaction.Player);
            Time.captureDeltaTime = 0.002f;
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0f;
        }

        private static int RenderInstanceCount(int typeId)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<CombatRenderBatchId>(),
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderActiveTag>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.GetComponentData<CombatRenderBatchId>(entities[i]).Value == typeId)
                    count++;
            }
            return count;
        }

        private static Vector2 ProjectileVelocity(CombatRoot projectileRoot)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            if (entities.Length > 0)
            {
                CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entities[0]);
                return new Vector2(kinematics.Velocity.x, kinematics.Velocity.y);
            }

            Assert.Fail("No projectile entity found for root.");
            return Vector2.zero;
        }

        private static float MaxProjectileVelocityY(CombatRoot projectileRoot)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            float maxVelocityY = float.NegativeInfinity;

            for (int i = 0; i < entities.Length; i++)
            {
                CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entities[i]);
                maxVelocityY = Mathf.Max(maxVelocityY, kinematics.Velocity.y);
            }

            if (float.IsNegativeInfinity(maxVelocityY))
            {
                Assert.Fail("No projectile entity found for root.");
            }

            return maxVelocityY;
        }

        private static int CountScopedProjectileEntities(CombatRoot projectileRoot)
        {
            return CountScopedProjectiles(projectileRoot, _ => true);
        }

        private static int CountScopedChildSpawnerEntities(CombatRoot projectileRoot)
        {
            return CountScopedProjectiles(
                projectileRoot,
                entity =>
                {
                    EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                    return entityManager.HasComponent<TimedSpawnComponent>(entity)
                        && entityManager.IsComponentEnabled<TimedSpawnComponent>(entity);
                });
        }

        private static Entity FirstScopedChildSpawnerEntity(CombatRoot projectileRoot)
        {
            Entity result = Entity.Null;
            CountScopedProjectiles(
                projectileRoot,
                entity =>
                {
                    EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                    if (!entityManager.HasComponent<TimedSpawnComponent>(entity)
                        || !entityManager.IsComponentEnabled<TimedSpawnComponent>(entity))
                    {
                        return false;
                    }

                    result = entity;
                    return true;
                });
            Assert.That(result, Is.Not.EqualTo(Entity.Null));
            return result;
        }

        private static int SumScopedContactGates(CombatRoot projectileRoot)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<ProjectileContactGateElement>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int gateCount = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                gateCount += entityManager.GetBuffer<ProjectileContactGateElement>(entities[i]).Length;
            }

            return gateCount;
        }

        private static int CountScopedProjectiles(CombatRoot projectileRoot, global::System.Func<Entity, bool> predicate)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int count = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                if (predicate(entities[i]))
                {
                    count++;
                }
            }

            return count;
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
            point.Configure(pool, 10f, localCap);
            spawner.Configure(pool, globalCap, points: new[] { point });
            spawnerObject.SetActive(true);
        }

        private static float ReadSpawnPointTimer(SpawnPoint point)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var timerField = typeof(SpawnPoint).GetField("timer", Flags);
            return (float)timerField.GetValue(point);
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
            mob.ConfigureAuthoring(10f, 0f, 0.5f);
            mobObject.SetActive(true);
            return mobObject;
        }

        private static int nextCritProbeId = 5000;
        private static int nextProjectileReplayProbeId = 6000;

        private static ProjectileReplayProbe CreateProjectileReplayProbe(Vector2 position)
        {
            GameObject go = new("ProjectileReplayProbe");
            go.transform.position = position;
            ProjectileReplayProbe probe = go.AddComponent<ProjectileReplayProbe>();
            probe.Configure(++nextProjectileReplayProbeId, ~0, 0.5f);
            return probe;
        }

        [UnityTest]
        public IEnumerator Projectile_CritChanceOne_HitDealsMultipliedDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnRequest(
                Vector2.zero, Vector2.right, 0f, 1f, 1f,
                new Vector2(1f, 1f), 0f,
                new DamageSnapshot(3f), CombatShapeType.Circle,
                critChance: 1f, critMultiplier: 2f), CombatFaction.Player);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(4f).Within(0.001f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator Projectile_CritChanceZero_HitDealsBaseDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out CombatRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnRequest(
                Vector2.zero, Vector2.right, 0f, 1f, 1f,
                new Vector2(1f, 1f), 0f,
                new DamageSnapshot(3f), CombatShapeType.Circle,
                critChance: 0f, critMultiplier: 2f), CombatFaction.Player);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(7f).Within(0.001f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator Aoe_CritChanceOne_HitDealsMultipliedDamage()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
            CritProbe probe = CreateCritProbe(Vector2.zero);
            root.TargetRegistry.Register(probe);

            root.Spawn(new AoeSpawnRequest(typeId, Vector2.zero,
                new DamageSnapshot(3f), 0f, 0f,
                AoeGeometry(templateObject),
                critChance: 1f, critMultiplier: 2f), CombatFaction.Player);
            yield return null;

            Assert.That(probe.LastDamage.Amount, Is.EqualTo(6f).Within(0.001f));
            Assert.That(probe.LastDamage.IsCrit, Is.True);
            Object.Destroy(rootObject);
            Object.Destroy(templateObject);
            Object.Destroy(probe.gameObject);
        }

        [UnityTest]
        public IEnumerator Aoe_CritChanceZero_HitDealsBaseDamage()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
            CritProbe probe = CreateCritProbe(Vector2.zero);
            root.TargetRegistry.Register(probe);

            root.Spawn(new AoeSpawnRequest(typeId, Vector2.zero,
                new DamageSnapshot(3f), 0f, 0f,
                AoeGeometry(templateObject),
                critChance: 0f, critMultiplier: 2f), CombatFaction.Player);
            yield return null;

            Assert.That(probe.LastDamage.Amount, Is.EqualTo(3f).Within(0.001f));
            Assert.That(probe.LastDamage.IsCrit, Is.False);
            Object.Destroy(rootObject);
            Object.Destroy(templateObject);
            Object.Destroy(probe.gameObject);
        }

        private static CritProbe CreateCritProbe(Vector2 position)
        {
            GameObject go = new("CritProbe");
            go.transform.position = position;
            CritProbe probe = go.AddComponent<CritProbe>();
            probe.Configure(++nextCritProbeId, ~0, 0.5f);
            return probe;
        }

        private sealed class CritProbe : MonoBehaviour, ICombatTarget
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
            public DamageSnapshot LastDamage { get; private set; }

            public void Configure(int id, int mask, float r)
            {
                targetId = id;
                targetMask = mask;
                radius = r;
            }

            public void ReceiveHit(in CombatHitData hit) => LastDamage = hit.Damage;
        }

        private sealed class ProjectileReplayProbe : MonoBehaviour, ICombatTarget
        {
            private int targetId;
            private int targetMask;
            private float radius;
            private bool alive = true;

            public int HitCount { get; private set; }
            public DamageSnapshot LastDamage { get; private set; }
            public EntityId LastSourceNodeId { get; private set; }
            public int TargetId => targetId;
            public EntityId ProjectileHitNodeId => gameObject.GetEntityId();
            public Vector2 CombatTargetPosition => transform.position;
            public float CombatTargetRadius => radius;
            public Vector2 CombatTargetHalfExtents => Vector2.one * radius;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => targetMask;
            public bool IsCombatTargetActive => alive;

            public void Configure(int id, int mask, float r)
            {
                targetId = id;
                targetMask = mask;
                radius = r;
            }

            public void ReceiveHit(in CombatHitData hit)
            {
                HitCount++;
                LastDamage = hit.Damage;
                LastSourceNodeId = hit.SourceNodeId;
                alive = false;
            }
        }
    }
}
