using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using PlayGround.Skills;
using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.Mob.Behaviours;
using PlayGround.Mob.Triggers;
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
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle));
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileRootDoesNotCreateProjectileSceneChildren()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle));
            yield return null;

            Assert.That(projectileObject.transform.childCount, Is.EqualTo(0));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [Test]
        public void ProjectileRootRegistersTemplateBeforeAwake()
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            GameObject rootObject = new("ProjectileRoot");
            rootObject.SetActive(false);
            ProjectileRoot root = rootObject.AddComponent<ProjectileRoot>();
            root.Configure(sprite);
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
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);

            projectileRoot.Spawn(new ProjectileSpawnCommand(new Vector2(50f, 50f), Vector2.right, 0f, 0f, 1f, new DamageSnapshot(4f), CombatShapeType.Circle));
            yield return null;

            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileSystemsIgnoreCommonCombatEntityWithoutProjectileTag()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity entity = entityManager.CreateEntity(
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(ProjectileActiveTag));

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
        public IEnumerator ProjectileTargetMaskFiltersHits()
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
                CombatShapeType.Circle,
                targetMask: 2);

            projectileRoot.Spawn(command);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(10f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileTrackingAcquiresTargetInForwardArea()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mobObject.transform.position = new Vector2(10f, 10f);
            mob.Register(projectileRoot.TargetRegistry);

            var command = new ProjectileSpawnCommand(
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

            projectileRoot.Spawn(command);
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
        public IEnumerator GameRootBindsTaggedMobToProjectileRootByTagAndLayer()
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
                CombatShapeType.Circle,
                targetMask: projectileRoot.TargetMask);

            projectileRoot.Spawn(command);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(6f));
            Object.Destroy(gameRootObject);
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileDirectDamageToggleSuppressesTargetDamage()
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
                CombatShapeType.Circle,
                directDamageEnabled: false);

            projectileRoot.Spawn(command);
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(10f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileReplaySkipsTargetThatDiesEarlierInSameHitBuffer()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            ProjectileReplayProbe probe = CreateProjectileReplayProbe(Vector2.zero);
            projectileRoot.TargetRegistry.Register(probe);
            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new DamageSnapshot(10f),
                CombatShapeType.Circle);
            projectileRoot.Spawn(command);
            projectileRoot.Spawn(command);
            yield return null;

            // Both hits land on the same target in one frame → one batch per unique target.
            // The probe receives both hits via ReceiveHits; alive check happens once at batch start.
            Assert.That(probe.HitCount, Is.EqualTo(2));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(probe.gameObject);
        }

        [UnityTest]
        public IEnumerator ProjectileImpactAoeRoutesThroughAoeRootInEcs()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            CreateAoeFixture(out GameObject aoeObject, out AoeRoot aoeRoot, out GameObject aoeTemplateObject, out int aoeTypeId);
            // ECS routing replaces the old managed CombatSpawnRouter.
            CombatSpawnRoutingBinder.Bind(projectileRoot, aoeRoot);
            mob.Register(projectileRoot.TargetRegistry);
            mob.Register(aoeRoot.TargetRegistry);

            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new Vector2(1f, 1f),
                0f,
                new DamageSnapshot(4f),
                CombatShapeType.Circle,
                targetMask: ~0,
                directDamageEnabled: false,
                impactAoe: new ProjectileImpactAoeSnapshot(
                    typeId: aoeTypeId,
                    targetMask: ~0,
                    damageAmount: 3f,
                    lifetimeSeconds: 0f,
                    tickIntervalSeconds: 0f,
                    AoeGeometry(aoeTemplateObject)));

            projectileRoot.Spawn(command);

            // Internal spawns now stay in ECS and resolve within a few frames.
            // directDamageEnabled:false means only the impact AOE (3) applies, so
            // the final health of exactly 7 proves the projectile dealt no direct
            // damage and the impact AOE routed and hit once.
            for (int i = 0; i < 8; i++)
            {
                yield return null;
            }

            Assert.That(mob.CurrentHealth, Is.EqualTo(7f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(aoeObject);
            Object.Destroy(aoeTemplateObject);
        }

        [UnityTest]
        public IEnumerator AoeProjectileBurstRoutesThroughProjectileRootInEcsWithoutImpactPayload()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            CreateAoeFixture(out GameObject aoeObject, out AoeRoot aoeRoot, out GameObject aoeTemplateObject, out int aoeTypeId);
            CombatSpawnRoutingBinder.Bind(projectileRoot, aoeRoot);
            mob.Register(projectileRoot.TargetRegistry);
            mob.Register(aoeRoot.TargetRegistry);

            var burst = new AoeProjectileBurstSnapshot(
                projectileTypeId: 0,
                targetMask: ~0,
                count: 1,
                spreadDegrees: 0f,
                speed: 0f,
                lifetimeSeconds: 1f,
                radius: 1f,
                halfExtents: Vector2.one,
                rotationRadians: 0f,
                shapeType: CombatShapeType.Circle,
                damage: new DamageSnapshot(2f));

            aoeRoot.Spawn(new AoeSpawnCommand(
                typeId: aoeTypeId,
                position: Vector2.zero,
                targetMask: ~0,
                damage: new DamageSnapshot(0f),
                lifetimeSeconds: 0f,
                tickIntervalSeconds: 0f,
                AoeGeometry(aoeTemplateObject),
                projectileBurst: burst));

            // The AOE deals 0 direct damage; only the burst projectile (2) applies.
            // Final health of exactly 8 proves the burst routed to the projectile
            // scope, hit once, and did NOT chain its own impact AOE (which would
            // subtract additional health).
            for (int i = 0; i < 8; i++)
            {
                yield return null;
            }

            Assert.That(mob.CurrentHealth, Is.EqualTo(8f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(aoeObject);
            Object.Destroy(aoeTemplateObject);
        }

        [UnityTest]
        public IEnumerator PiercingProjectileCanRepeatHitAfterCooldown()
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
                CombatShapeType.Circle,
                pierceCount: 2,
                repeatHitCooldownSeconds: 0.02f);

            projectileRoot.Spawn(command);
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
        public IEnumerator ProjectileChildSpawnedChildAppliesChildPayloadDamage()
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
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)),
                directDamageEnabled: false);

            projectileRoot.Spawn(command);
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
        public IEnumerator ProjectileChildSpawnedChildUsesRootTargetMaskForTracking()
        {
            int mobHurtboxLayer = LayerMask.NameToLayer(GameplayLayers.MobHurtbox);
            Assume.That(mobHurtboxLayer, Is.GreaterThanOrEqualTo(0));
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            int mobHurtboxMask = 1 << mobHurtboxLayer;
            projectileRoot.ConfigureTargetBinding(mobHurtboxMask);
            mobObject.layer = mobHurtboxLayer;
            mobObject.transform.position = new Vector2(10f, 10f);
            mob.Register(projectileRoot.TargetRegistry);

            var childSpawn = new ProjectileChildSpawnConfig(
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
                targetMask: 1,
                tracking: new ProjectileTrackingConfig(true, 50f, 360f, 0f),
                behavior: new ProjectileChildSpawnBehavior(1, ProjectileChildSpawnPatternType.Forward));
            var command = new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                0.25f,
                new Vector2(0.25f, 0.25f),
                0f,
                new DamageSnapshot(1f),
                CombatShapeType.Circle,
                childSpawn: childSpawn,
                directDamageEnabled: false);

            projectileRoot.Spawn(command);
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
        public IEnumerator ProjectileHitPayloadPreservesSourceIdForTargetDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            ProjectileReplayProbe probe = CreateProjectileReplayProbe(Vector2.zero);
            projectileRoot.TargetRegistry.Register(probe);
            GameObject sourceObject = new("ProjectileSource");
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
                CombatShapeType.Circle,
                sourceNodeId: sourceNodeId);

            projectileRoot.Spawn(command);
            yield return null;

            Assert.That(probe.LastSourceNodeId, Is.EqualTo(sourceNodeId));
            Assert.That(probe.LastDamage.Amount, Is.EqualTo(2f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
            Object.Destroy(probe.gameObject);
            Object.Destroy(sourceObject);
        }

        [UnityTest]
        public IEnumerator ProjectileChildSpawnedChildrenArePreparedForRenderBatchOnNextStep()
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
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 0, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)));

            projectileRoot.Spawn(command);
            Time.captureDeltaTime = 0.02f;
            yield return null;
            Time.captureDeltaTime = 0.001f;
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(RenderInstanceCount(projectileRoot, 0), Is.EqualTo(3));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileChildSpawnerStoresIntervalJitterInEcs()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            const float IntervalJitterSeconds = 0.25f;
            var command = new ProjectileSpawnCommand(
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

            projectileRoot.Spawn(command);
            yield return null;

            Entity childSpawnerEntity = FirstScopedChildSpawnerEntity(projectileRoot);
            ProjectileChildSpawnerComponent spawner =
                World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<ProjectileChildSpawnerComponent>(childSpawnerEntity);

            Assert.That(spawner.IntervalJitterSeconds, Is.EqualTo(IntervalJitterSeconds).Within(0.0001f));
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
                CombatShapeType.Circle,
                childSpawn: new ProjectileChildSpawnConfig(1, 16, 0.01f, 0f, 0f, 1f, 1f, new Vector2(1f, 1f), CombatShapeType.Circle, 0f, new DamageSnapshot(1f)));

            Assert.Throws<global::System.InvalidOperationException>(() => projectileRoot.Spawn(command));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileSpawnReusesExpiredEcsEntityAfterPoolWarmup()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            var command = new ProjectileSpawnCommand(new Vector2(50f, 50f), Vector2.right, 0f, 0f, 1f, new DamageSnapshot(1f), CombatShapeType.Circle);

            projectileRoot.Spawn(command);
            Time.captureDeltaTime = 0.01f;
            yield return null;
            int warmedCount = CountScopedProjectileEntities(projectileRoot);

            for (int i = 0; i < 4; i++)
            {
                projectileRoot.Spawn(command);
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
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(Vector2.zero, Vector2.right, 0f, 1f, 1f, new DamageSnapshot(1f), CombatShapeType.Circle));
            Time.captureDeltaTime = 0.01f;
            yield return null;
            Assert.That(SumScopedContactGates(projectileRoot), Is.GreaterThan(0));

            projectileRoot.Spawn(new ProjectileSpawnCommand(new Vector2(50f, 50f), Vector2.right, 0f, 1f, 1f, new DamageSnapshot(1f), CombatShapeType.Circle));
            yield return null;
            Time.captureDeltaTime = 0f;

            Assert.That(CountScopedProjectileEntities(projectileRoot), Is.EqualTo(1));
            Assert.That(SumScopedContactGates(projectileRoot), Is.EqualTo(0));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator ProjectileChildSpawnRequestsReuseChildEntitiesAfterPoolWarmup()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            var command = ChildSpawnerCommand();

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
        public IEnumerator ProjectileChildSpawnerParentKeepsStableArchetypeDuringReuse()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out _);
            var command = ChildSpawnerCommand();

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
            out AoeRoot root,
            out GameObject templateObject,
            out int typeId)
        {
            templateObject = new GameObject("AoeTemplate");
            templateObject.SetActive(false);
            CircleCollider2D shape = templateObject.AddComponent<CircleCollider2D>();
            shape.radius = 1f;

            var definition = new AoeTypeDefinition();
            definition.Configure(templateObject, shape);

            rootObject = new GameObject("AoeRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<AoeRoot>();
            root.Configure(~0);
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

        private static ProjectileSpawnCommand ChildSpawnerCommand()
        {
            return new ProjectileSpawnCommand(
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

        private static IEnumerator SpawnAndDrainChildCycle(ProjectileRoot projectileRoot, ProjectileSpawnCommand command)
        {
            projectileRoot.Spawn(command);
            Time.captureDeltaTime = 0.002f;
            yield return null;
            yield return null;
            Time.captureDeltaTime = 0f;
        }

        private static int RenderInstanceCount(ProjectileRoot projectileRoot, int typeId)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scopeEntityField   = typeof(ProjectileRoot).GetField("scopeEntity", Flags);
            var submitQueriesField = typeof(ProjectileRoot).GetField("submitQueriesByType", Flags);
            var scopeEntity   = (Entity)scopeEntityField.GetValue(projectileRoot);
            var submitQueries = (EntityQuery[])submitQueriesField.GetValue(projectileRoot);

            if (submitQueries == null || typeId >= submitQueries.Length)
            {
                return 0;
            }

            EntityQuery query = submitQueries[typeId];
            query.SetSharedComponentFilter(new CombatRenderScope { Scope = scopeEntity });
            int count = query.CalculateEntityCount();
            query.ResetFilter();
            return count;
        }

        private static Vector2 ProjectileVelocity(ProjectileRoot projectileRoot)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scopeEntityField = typeof(ProjectileRoot).GetField("scopeEntity", Flags);
            var scopeEntity = (Entity)scopeEntityField.GetValue(projectileRoot);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                ProjectileIdentityComponent identity = entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]);
                if (identity.Scope != scopeEntity)
                {
                    continue;
                }

                CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entities[i]);
                return new Vector2(kinematics.Velocity.x, kinematics.Velocity.y);
            }

            Assert.Fail("No projectile entity found for root.");
            return Vector2.zero;
        }

        private static float MaxProjectileVelocityY(ProjectileRoot projectileRoot)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scopeEntityField = typeof(ProjectileRoot).GetField("scopeEntity", Flags);
            var scopeEntity = (Entity)scopeEntityField.GetValue(projectileRoot);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            float maxVelocityY = float.NegativeInfinity;

            for (int i = 0; i < entities.Length; i++)
            {
                ProjectileIdentityComponent identity = entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]);
                if (identity.Scope != scopeEntity)
                {
                    continue;
                }

                CombatKinematicsComponent kinematics = entityManager.GetComponentData<CombatKinematicsComponent>(entities[i]);
                maxVelocityY = Mathf.Max(maxVelocityY, kinematics.Velocity.y);
            }

            if (float.IsNegativeInfinity(maxVelocityY))
            {
                Assert.Fail("No projectile entity found for root.");
            }

            return maxVelocityY;
        }

        private static int CountScopedProjectileEntities(ProjectileRoot projectileRoot)
        {
            return CountScopedProjectiles(projectileRoot, _ => true);
        }

        private static int CountScopedChildSpawnerEntities(ProjectileRoot projectileRoot)
        {
            return CountScopedProjectiles(
                projectileRoot,
                entity => World.DefaultGameObjectInjectionWorld.EntityManager.HasComponent<ProjectileChildSpawnerTag>(entity));
        }

        private static Entity FirstScopedChildSpawnerEntity(ProjectileRoot projectileRoot)
        {
            Entity result = Entity.Null;
            CountScopedProjectiles(
                projectileRoot,
                entity =>
                {
                    EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                    if (!entityManager.HasComponent<ProjectileChildSpawnerTag>(entity))
                    {
                        return false;
                    }

                    result = entity;
                    return true;
                });
            Assert.That(result, Is.Not.EqualTo(Entity.Null));
            return result;
        }

        private static int SumScopedContactGates(ProjectileRoot projectileRoot)
        {
            Entity scope = ProjectileScopeEntity(projectileRoot);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<ProjectileContactGateElement>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int gateCount = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                ProjectileIdentityComponent identity = entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]);
                if (identity.Scope != scope)
                {
                    continue;
                }

                gateCount += entityManager.GetBuffer<ProjectileContactGateElement>(entities[i]).Length;
            }

            return gateCount;
        }

        private static int CountScopedProjectiles(ProjectileRoot projectileRoot, global::System.Func<Entity, bool> predicate)
        {
            Entity scope = ProjectileScopeEntity(projectileRoot);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int count = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                ProjectileIdentityComponent identity = entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]);
                if (identity.Scope == scope && predicate(entities[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static Entity ProjectileScopeEntity(ProjectileRoot projectileRoot)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scopeEntityField = typeof(ProjectileRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(projectileRoot);
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
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(
                Vector2.zero, Vector2.right, 0f, 1f, 1f,
                new Vector2(1f, 1f), 0f,
                new DamageSnapshot(3f), CombatShapeType.Circle,
                critChance: 1f, critMultiplier: 2f));
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(4f).Within(0.001f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator Projectile_CritChanceZero_HitDealsBaseDamage()
        {
            CreateProjectileHitFixture(out GameObject projectileObject, out ProjectileRoot projectileRoot, out GameObject mobObject, out MobRoot mob);
            mob.Register(projectileRoot.TargetRegistry);

            projectileRoot.Spawn(new ProjectileSpawnCommand(
                Vector2.zero, Vector2.right, 0f, 1f, 1f,
                new Vector2(1f, 1f), 0f,
                new DamageSnapshot(3f), CombatShapeType.Circle,
                critChance: 0f, critMultiplier: 2f));
            yield return null;

            Assert.That(mob.CurrentHealth, Is.EqualTo(7f).Within(0.001f));
            Object.Destroy(projectileObject);
            Object.Destroy(mobObject);
        }

        [UnityTest]
        public IEnumerator Aoe_CritChanceOne_HitDealsMultipliedDamage()
        {
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject, out int typeId);
            CritProbe probe = CreateCritProbe(Vector2.zero);
            root.TargetRegistry.Register(probe);

            root.Spawn(new AoeSpawnCommand(typeId, Vector2.zero, ~0,
                new DamageSnapshot(3f), 0f, 0f,
                AoeGeometry(templateObject),
                critChance: 1f, critMultiplier: 2f));
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
            CreateAoeFixture(out GameObject rootObject, out AoeRoot root, out GameObject templateObject, out int typeId);
            CritProbe probe = CreateCritProbe(Vector2.zero);
            root.TargetRegistry.Register(probe);

            root.Spawn(new AoeSpawnCommand(typeId, Vector2.zero, ~0,
                new DamageSnapshot(3f), 0f, 0f,
                AoeGeometry(templateObject),
                critChance: 0f, critMultiplier: 2f));
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
