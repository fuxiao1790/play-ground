using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.Skills;
using PlayGround.Mob;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.Tests.PlayMode
{
    public sealed class AoePlayModeTests
    {
        private const int DefaultTargetMask = 1;
        private const int VolatileStackKey = 101;
        private const int PoisonStackKey = 201;
        private const int BurningStackKey = 202;
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

            root.Spawn(Command(typeId, templateObject, Vector2.zero, 2f), CombatFaction.Player);
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
            root.Spawn(Command(typeId, templateObject, Vector2.zero, 2f), CombatFaction.Player);
            yield return null;

            CombatRenderComponent render = FirstScopedAoeRenderComponent(root);

            // geometry.VisualScale (spawn-time, from transform lossyScale * areaSize) is (6, 8)
            // here (templateScale 2 * visualScale (3,4), areaSize 1). The render matrix now also
            // folds in the registered sprite's native size, since the shared atlas mesh no longer
            // bakes native size into vertices (see combat-render-atlas/004-visual-scale-folding.md).
            Vector2 nativeSize = CombatAtlasTestFixture.NativeSize;
            Assert.That(render.Rotation.x, Is.EqualTo(6f * nativeSize.x).Within(0.0001f));
            Assert.That(render.Rotation.w, Is.EqualTo(8f * nativeSize.y).Within(0.0001f));
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

            root.Spawn(new AoeSpawnRequest(
                typeId,
                Vector2.zero,
                new DamageSnapshot(2f),
                lifetimeSeconds: 0f,
                tickIntervalSeconds: 0f,
                Geometry(templateObject, 2f)), CombatFaction.Player);
            yield return null;

            CombatCollisionComponent collision = FirstScopedAoeCollision(root);
            CombatRenderComponent render = FirstScopedAoeRenderComponent(root);

            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(collision.Radius, Is.EqualTo(2f).Within(0.0001f));
            // geometry.VisualScale is (6, 8) here (visualScale (3,4) * areaSize 2, templateScale
            // defaults to 1). See the sibling assertion above for native-sprite-size folding.
            Vector2 nativeSize = CombatAtlasTestFixture.NativeSize;
            Assert.That(render.Rotation.x, Is.EqualTo(6f * nativeSize.x).Within(0.0001f));
            Assert.That(render.Rotation.w, Is.EqualTo(8f * nativeSize.y).Within(0.0001f));
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
                root.Spawn(Command(typeId, templateObject, Vector2.zero, 1f), CombatFaction.Player);
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

            root.Spawn(Command(typeId, templateObject, Vector2.zero, 3f), CombatFaction.Player);
            yield return null;

            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.LastDamage.Amount, Is.EqualTo(3f));
            Cleanup(rootObject, templateObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator AoeCountersTrackSpawnDespawnHitAndRenderBatchFields()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out int typeId);
            AoeTargetProbe target = CreateTarget(Vector2.zero, DefaultTargetMask);
            root.TargetRegistry.Register(target);

            root.Spawn(Command(typeId, templateObject, Vector2.zero, 2f), CombatFaction.Player);
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

        [Test]
        public void CombatRootRegisterSpawnTemplateDeduplicatesOnScope()
        {
            CreateProjectileRoot(out GameObject rootObject, out CombatRoot root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity scope = ScopeEntity(root);
            int projectileStartCount = entityManager.GetComponentData<ProjectileSpawnTemplate>(scope).Map.Count;
            int aoeStartCount = entityManager.GetComponentData<AoeSpawnTemplate>(scope).Map.Count;

            var projectileA = new ProjectileSpawnCommand
            {
                TypeId = ++nextTargetId,
                Count = 2,
                Speed = 10f,
                Lifetime = 1f,
                Radius = 0.25f,
                ShapeType = CombatShapeType.Circle,
                HitPayload = new ProjectileHitPayload(new CombatHitPayload
                {
                    DamageAmount = 4f,
                    DirectDamageEnabled = true
                })
            };
            ProjectileSpawnCommand projectileC = projectileA;
            projectileC.Count = 3;
            ProjectileSpawnCommand projectileRuntimeFrame = projectileA;
            projectileRuntimeFrame.Faction = CombatFaction.Player;
            projectileRuntimeFrame.ProjectileId = 123;
            projectileRuntimeFrame.SeedContactGateTargetId = 456;
            projectileRuntimeFrame.Position = new float2(7f, 9f);
            projectileRuntimeFrame.Velocity = new float2(1f, 2f);
            projectileRuntimeFrame.BoundsMin = new float2(6f, 8f);
            projectileRuntimeFrame.BoundsMax = new float2(8f, 10f);
            projectileRuntimeFrame.JitterSeed = 789u;
            projectileRuntimeFrame.DeterministicIdTickIndex = 12;

            Hash128 projectileKeyA = root.RegisterSpawnTemplate(in projectileA);
            Hash128 projectileKeyB = root.RegisterTimedSpawnTemplate(in projectileA);
            Hash128 projectileKeyC = root.RegisterSpawnTemplate(in projectileC);
            Hash128 projectileRuntimeFrameKey = root.RegisterSpawnTemplate(in projectileRuntimeFrame);
            Hash128 projectileKeyD = root.RegisterTimedSpawnTemplate(in projectileA);
            ProjectileSpawnTemplate projectileRegistry =
                entityManager.GetComponentData<ProjectileSpawnTemplate>(scope);

            Assert.That(projectileKeyB, Is.EqualTo(projectileKeyA));
            Assert.That(projectileKeyC, Is.Not.EqualTo(projectileKeyA));
            Assert.That(projectileRuntimeFrameKey, Is.EqualTo(projectileKeyA));
            Assert.That(projectileKeyD, Is.EqualTo(projectileKeyA));
            Assert.That(projectileRegistry.Map.Count, Is.EqualTo(projectileStartCount + 2));
            Assert.That(projectileRegistry.Map.ContainsKey(projectileKeyA), Is.True);
            Assert.That(projectileRegistry.Map.ContainsKey(projectileKeyC), Is.True);
            Assert.That(projectileRegistry.Map[projectileKeyA].Faction, Is.EqualTo(CombatFaction.None));
            Assert.That(projectileRegistry.Map[projectileKeyA].ProjectileId, Is.Zero);
            Assert.That(projectileRegistry.Map[projectileKeyA].Position, Is.EqualTo(default(float2)));
            Assert.That(projectileRegistry.Map[projectileKeyA].Velocity, Is.EqualTo(default(float2)));
            Assert.That(projectileRegistry.Map[projectileKeyA].BoundsMin, Is.EqualTo(default(float2)));
            Assert.That(projectileRegistry.Map[projectileKeyA].BoundsMax, Is.EqualTo(default(float2)));
            Assert.That(projectileRegistry.Map[projectileKeyA].JitterSeed, Is.Zero);
            Assert.That(projectileRegistry.Map[projectileKeyA].DeterministicIdTickIndex, Is.Zero);

            var aoeA = new AoeSpawnCommand
            {
                TypeId = ++nextTargetId,
                Lifetime = 1.5f,
                RepeatHitCooldownSeconds = 0.2f,
                Radius = 1f,
                ShapeType = CombatShapeType.Circle,
                EchoCount = 1,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = 5f,
                    CritChance = 0.1f,
                    CritMultiplier = 2f,
                    DirectDamageEnabled = true
                }
            };
            AoeSpawnCommand aoeC = aoeA;
            aoeC.EchoCount = 2;
            AoeSpawnCommand aoeRuntimeFrame = aoeA;
            aoeRuntimeFrame.Faction = CombatFaction.Mob;
            aoeRuntimeFrame.AoeId = 321;
            aoeRuntimeFrame.Position = new float2(-3f, 4f);
            aoeRuntimeFrame.BoundsMin = new float2(-4f, 3f);
            aoeRuntimeFrame.BoundsMax = new float2(-2f, 5f);
            aoeRuntimeFrame.JitterSeed = 654u;
            aoeRuntimeFrame.DeterministicIdTickIndex = 21;

            Hash128 aoeKeyA = root.RegisterSpawnTemplate(in aoeA);
            Hash128 aoeKeyB = root.RegisterTimedSpawnTemplate(in aoeA);
            Hash128 aoeKeyC = root.RegisterSpawnTemplate(in aoeC);
            Hash128 aoeRuntimeFrameKey = root.RegisterSpawnTemplate(in aoeRuntimeFrame);
            Hash128 aoeKeyD = root.RegisterTimedSpawnTemplate(in aoeA);
            AoeSpawnTemplate aoeRegistry = entityManager.GetComponentData<AoeSpawnTemplate>(scope);

            Assert.That(aoeKeyB, Is.EqualTo(aoeKeyA));
            Assert.That(aoeKeyC, Is.Not.EqualTo(aoeKeyA));
            Assert.That(aoeRuntimeFrameKey, Is.EqualTo(aoeKeyA));
            Assert.That(aoeKeyD, Is.EqualTo(aoeKeyA));
            Assert.That(aoeRegistry.Map.Count, Is.EqualTo(aoeStartCount + 2));
            Assert.That(aoeRegistry.Map.ContainsKey(aoeKeyA), Is.True);
            Assert.That(aoeRegistry.Map.ContainsKey(aoeKeyC), Is.True);
            Assert.That(aoeRegistry.Map[aoeKeyA].Faction, Is.EqualTo(CombatFaction.None));
            Assert.That(aoeRegistry.Map[aoeKeyA].AoeId, Is.Zero);
            Assert.That(aoeRegistry.Map[aoeKeyA].Position, Is.EqualTo(default(float2)));
            Assert.That(aoeRegistry.Map[aoeKeyA].BoundsMin, Is.EqualTo(default(float2)));
            Assert.That(aoeRegistry.Map[aoeKeyA].BoundsMax, Is.EqualTo(default(float2)));
            Assert.That(aoeRegistry.Map[aoeKeyA].JitterSeed, Is.Zero);
            Assert.That(aoeRegistry.Map[aoeKeyA].DeterministicIdTickIndex, Is.Zero);

            Object.DestroyImmediate(rootObject);
        }

        [Test]
        public void CompileAndRegisterAssignsDedupedIntervalTemplateKeys()
        {
            CreateProjectileRoot(out GameObject rootObject, out CombatRoot root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Entity scope = ScopeEntity(root);
            int projectileStartCount = entityManager.GetComponentData<ProjectileSpawnTemplate>(scope).Map.Count;

            BasicAttackPrefab rootPrefab = CreateProjectilePrefab("RootProjectileTemplate");
            BasicAttackPrefab childPrefab = CreateProjectilePrefab("ChildProjectileTemplate");
            ProjectileSkill rootSkillA = ScriptableObject.CreateInstance<ProjectileSkill>();
            ProjectileSkill rootSkillB = ScriptableObject.CreateInstance<ProjectileSkill>();
            ProjectileSkill childSkill = ScriptableObject.CreateInstance<ProjectileSkill>();
            SkillSet rootSetA = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet rootSetB = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet childSet = ScriptableObject.CreateInstance<SkillSet>();
            ProjectileIntervalSpawnTrigger triggerA = ScriptableObject.CreateInstance<ProjectileIntervalSpawnTrigger>();
            ProjectileIntervalSpawnTrigger triggerB = ScriptableObject.CreateInstance<ProjectileIntervalSpawnTrigger>();
            SkillLoadout loadout = ScriptableObject.CreateInstance<SkillLoadout>();
            GameObject driverObject = new("SkillDriverHarness");
            driverObject.SetActive(false);
            SkillDriver driver = driverObject.AddComponent<SkillDriver>();

            ConfigureProjectile(rootSkillA, rootPrefab, damage: 0f);
            ConfigureProjectile(rootSkillB, rootPrefab, damage: 0f);
            ConfigureProjectile(childSkill, childPrefab, damage: 3f);
            triggerA.intervalSeconds = 0.25f;
            triggerB.intervalSeconds = 0.25f;
            triggerA.projectileCount = 1;
            triggerB.projectileCount = 1;
            SetField(rootSetA, "skill", rootSkillA);
            SetField(rootSetA, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(rootSetB, "skill", rootSkillB);
            SetField(rootSetB, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(childSet, "skill", childSkill);
            SetField(childSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(loadout, "slots", new global::System.Collections.Generic.List<LoadoutSlot>
            {
                new SkillSetSlot { skillSet = rootSetA },
                new TriggerLinkSlot { link = triggerA },
                new SkillSetSlot { skillSet = childSet },
                new SkillSetSlot { skillSet = rootSetB },
                new TriggerLinkSlot { link = triggerB },
                new SkillSetSlot { skillSet = childSet },
            });
            SetField(driver, "loadout", loadout);
            SetField(driver, "combatRoot", root);

            CompileAndRegister(driver);
            var first = (RuntimeProjectileDefinition)CompiledRuntime(driver, 0);
            var second = (RuntimeProjectileDefinition)CompiledRuntime(driver, 1);
            RuntimeChildSpawnSetup firstSetup = first.ChildSpawnSetup;
            RuntimeChildSpawnSetup secondSetup = second.ChildSpawnSetup;
            ProjectileSpawnTemplate registry = entityManager.GetComponentData<ProjectileSpawnTemplate>(scope);

            Assert.That(firstSetup, Is.Not.Null);
            Assert.That(secondSetup, Is.Not.Null);
            Assert.That(firstSetup.TemplateKey, Is.Not.EqualTo(default(Hash128)));
            Assert.That(secondSetup.TemplateKey, Is.EqualTo(firstSetup.TemplateKey));
            Assert.That(registry.Map.ContainsKey(firstSetup.TemplateKey), Is.True);
            Assert.That(registry.Map.Count, Is.LessThanOrEqualTo(projectileStartCount + 1));

            triggerB.projectileCount = 2;
            CompileAndRegister(driver);
            first = (RuntimeProjectileDefinition)CompiledRuntime(driver, 0);
            second = (RuntimeProjectileDefinition)CompiledRuntime(driver, 1);
            Hash128 originalKey = first.ChildSpawnSetup.TemplateKey;
            Hash128 changedCountKey = second.ChildSpawnSetup.TemplateKey;
            registry = entityManager.GetComponentData<ProjectileSpawnTemplate>(scope);

            Assert.That(originalKey, Is.EqualTo(firstSetup.TemplateKey));
            Assert.That(changedCountKey, Is.Not.EqualTo(originalKey));
            Assert.That(registry.Map.ContainsKey(changedCountKey), Is.True);
            Assert.That(registry.Map.Count, Is.LessThanOrEqualTo(projectileStartCount + 2));

            triggerB.projectileCount = 1;
            CompileAndRegister(driver);
            second = (RuntimeProjectileDefinition)CompiledRuntime(driver, 1);

            Assert.That(second.ChildSpawnSetup.TemplateKey, Is.EqualTo(originalKey));

            Cleanup(rootObject, rootPrefab.gameObject, childPrefab.gameObject, driverObject);
            CleanupObjects(
                rootSkillA,
                rootSkillB,
                childSkill,
                rootSetA,
                rootSetB,
                childSet,
                triggerA,
                triggerB,
                loadout);
        }

        [UnityTest]
        public IEnumerator LingeringAoeIntervalChildrenUseRegistryTemplatesUntilSourceExpires()
        {
            CreateProjectileRoot(out GameObject rootObject, out CombatRoot root);
            LingeringAoePrefab projectileSourcePrefab = CreateLingeringAoePrefab("ProjectileChildSourceTemplate");
            LingeringAoePrefab aoeSourcePrefab = CreateLingeringAoePrefab("AoeChildSourceTemplate");
            BasicAttackPrefab projectileChildPrefab = CreateProjectilePrefab("IntervalProjectileChildTemplate");
            BasicAoePrefab aoeChildPrefab = CreateAoePrefab("IntervalAoeChildTemplate");
            LingeringAoeSkill projectileSourceSkill = ScriptableObject.CreateInstance<LingeringAoeSkill>();
            LingeringAoeSkill aoeSourceSkill = ScriptableObject.CreateInstance<LingeringAoeSkill>();
            ProjectileSkill projectileChildSkill = ScriptableObject.CreateInstance<ProjectileSkill>();
            AoeSkill aoeChildSkill = ScriptableObject.CreateInstance<AoeSkill>();
            SkillSet projectileSourceSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet aoeSourceSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet projectileChildSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet aoeChildSet = ScriptableObject.CreateInstance<SkillSet>();
            ProjectileIntervalSpawnTrigger projectileTrigger = ScriptableObject.CreateInstance<ProjectileIntervalSpawnTrigger>();
            AoeIntervalSpawnTrigger aoeTrigger = ScriptableObject.CreateInstance<AoeIntervalSpawnTrigger>();
            SkillLoadout loadout = ScriptableObject.CreateInstance<SkillLoadout>();
            GameObject driverObject = new("SkillDriverHarness");
            driverObject.SetActive(false);
            SkillDriver driver = driverObject.AddComponent<SkillDriver>();

            ConfigureLingeringAoe(projectileSourceSkill, projectileSourcePrefab, damage: 0f, lifetime: 0.07f);
            ConfigureLingeringAoe(aoeSourceSkill, aoeSourcePrefab, damage: 0f, lifetime: 0.07f);
            ConfigureProjectile(projectileChildSkill, projectileChildPrefab, damage: 1f);
            ConfigureAoe(aoeChildSkill, aoeChildPrefab, damage: 1f);
            projectileTrigger.intervalSeconds = 0.02f;
            projectileTrigger.projectileCount = 1;
            aoeTrigger.intervalSeconds = 0.02f;
            aoeTrigger.echoCount = 1;
            SetField(projectileSourceSet, "skill", projectileSourceSkill);
            SetField(projectileSourceSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(aoeSourceSet, "skill", aoeSourceSkill);
            SetField(aoeSourceSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(projectileChildSet, "skill", projectileChildSkill);
            SetField(projectileChildSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(aoeChildSet, "skill", aoeChildSkill);
            SetField(aoeChildSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(loadout, "slots", new global::System.Collections.Generic.List<LoadoutSlot>
            {
                new SkillSetSlot { skillSet = projectileSourceSet },
                new TriggerLinkSlot { link = projectileTrigger },
                new SkillSetSlot { skillSet = projectileChildSet },
                new SkillSetSlot { skillSet = aoeSourceSet },
                new TriggerLinkSlot { link = aoeTrigger },
                new SkillSetSlot { skillSet = aoeChildSet },
            });
            SetField(driver, "loadout", loadout);
            SetField(driver, "combatRoot", root);

            CompileAndRegister(driver);
            var projectileSource = (RuntimeAoeDefinition)CompiledRuntime(driver, 0);
            var aoeSource = (RuntimeAoeDefinition)CompiledRuntime(driver, 1);
            int projectileChildTypeId = projectileSource.ChildSpawnSetup.ChildDefinition.TypeId;
            int aoeChildTypeId = aoeSource.AoeIntervalSpawnSetup.ChildDefinition.TypeId;

            SkillSpawnTranslator.Spawn(projectileSource, Vector2.zero, Vector2.right, Vector2.zero, root, CombatFaction.Player);
            SkillSpawnTranslator.Spawn(aoeSource, Vector2.zero, Vector2.right, Vector2.zero, root, CombatFaction.Player);

            for (int i = 0; i < 10; i++)
                yield return new WaitForSeconds(0.02f);

            int projectileCountAfterLifetime = ScopedProjectileCount(root, projectileChildTypeId);
            int aoeCountAfterLifetime = ScopedAoeCount(root, aoeChildTypeId);
            Assert.That(projectileCountAfterLifetime, Is.GreaterThanOrEqualTo(2));
            Assert.That(aoeCountAfterLifetime, Is.GreaterThanOrEqualTo(2));

            for (int i = 0; i < 8; i++)
                yield return new WaitForSeconds(0.02f);

            Assert.That(ScopedProjectileCount(root, projectileChildTypeId), Is.EqualTo(projectileCountAfterLifetime));
            Assert.That(ScopedAoeCount(root, aoeChildTypeId), Is.EqualTo(aoeCountAfterLifetime));
            LogAssert.NoUnexpectedReceived();

            Cleanup(
                rootObject,
                projectileSourcePrefab.gameObject,
                aoeSourcePrefab.gameObject,
                projectileChildPrefab.gameObject,
                aoeChildPrefab.gameObject,
                driverObject);
            CleanupObjects(
                projectileSourceSkill,
                aoeSourceSkill,
                projectileChildSkill,
                aoeChildSkill,
                projectileSourceSet,
                aoeSourceSet,
                projectileChildSet,
                aoeChildSet,
                projectileTrigger,
                aoeTrigger,
                loadout);
        }

        [UnityTest]
        public IEnumerator ProjectileIntervalChildrenKeepNestedAoeIntervalSpawner()
        {
            CreateProjectileRoot(out GameObject rootObject, out CombatRoot root);
            BasicAttackPrefab rootPrefab = CreateProjectilePrefab("NestedIntervalRootProjectileTemplate");
            BasicAttackPrefab middlePrefab = CreateProjectilePrefab("NestedIntervalMiddleProjectileTemplate");
            BasicAoePrefab aoePrefab = CreateAoePrefab("NestedIntervalAoeChildTemplate");
            ProjectileSkill rootSkill = ScriptableObject.CreateInstance<ProjectileSkill>();
            ProjectileSkill middleSkill = ScriptableObject.CreateInstance<ProjectileSkill>();
            AoeSkill aoeSkill = ScriptableObject.CreateInstance<AoeSkill>();
            SkillSet rootSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet middleSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet aoeSet = ScriptableObject.CreateInstance<SkillSet>();
            ProjectileIntervalSpawnTrigger projectileTrigger = ScriptableObject.CreateInstance<ProjectileIntervalSpawnTrigger>();
            AoeIntervalSpawnTrigger aoeTrigger = ScriptableObject.CreateInstance<AoeIntervalSpawnTrigger>();
            SkillLoadout loadout = ScriptableObject.CreateInstance<SkillLoadout>();
            GameObject driverObject = new("SkillDriverHarness");
            driverObject.SetActive(false);
            SkillDriver driver = driverObject.AddComponent<SkillDriver>();

            ConfigureProjectile(rootSkill, rootPrefab, damage: 0f);
            ConfigureProjectile(middleSkill, middlePrefab, damage: 0f);
            ConfigureAoe(aoeSkill, aoePrefab, damage: 1f);
            projectileTrigger.intervalSeconds = 0.02f;
            aoeTrigger.intervalSeconds = 0.02f;
            SetField(rootSet, "skill", rootSkill);
            SetField(rootSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(middleSet, "skill", middleSkill);
            SetField(middleSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(aoeSet, "skill", aoeSkill);
            SetField(aoeSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(loadout, "slots", new global::System.Collections.Generic.List<LoadoutSlot>
            {
                new SkillSetSlot { skillSet = rootSet },
                new TriggerLinkSlot { link = projectileTrigger },
                new SkillSetSlot { skillSet = middleSet },
                new TriggerLinkSlot { link = aoeTrigger },
                new SkillSetSlot { skillSet = aoeSet },
            });
            SetField(driver, "loadout", loadout);
            SetField(driver, "combatRoot", root);

            CompileAndRegister(driver);
            var rootRuntime = (RuntimeProjectileDefinition)CompiledRuntime(driver, 0);
            RuntimeProjectileDefinition middleRuntime = rootRuntime.ChildSpawnSetup.ChildDefinition;
            RuntimeAoeDefinition aoeRuntime = middleRuntime.AoeIntervalSpawnSetup.ChildDefinition;

            SkillSpawnTranslator.Spawn(rootRuntime, Vector2.zero, Vector2.right, Vector2.zero, root, CombatFaction.Player);

            for (int i = 0; i < 10; i++)
                yield return new WaitForSeconds(0.02f);

            Assert.That(ScopedProjectileCount(root, middleRuntime.TypeId), Is.GreaterThanOrEqualTo(1));
            Assert.That(ScopedAoeCount(root, aoeRuntime.TypeId), Is.GreaterThanOrEqualTo(1));
            LogAssert.NoUnexpectedReceived();

            Cleanup(rootObject, rootPrefab.gameObject, middlePrefab.gameObject, aoePrefab.gameObject, driverObject);
            CleanupObjects(
                rootSkill,
                middleSkill,
                aoeSkill,
                rootSet,
                middleSet,
                aoeSet,
                projectileTrigger,
                aoeTrigger,
                loadout);
        }

        [UnityTest]
        public IEnumerator ProjectileImpactAoeApplicatorStackTriggerDetonatesStackSet()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject rootTemplateObject, out _);
            BasicAttackPrefab projectilePrefab = CreateProjectilePrefab("RootProjectileTemplate");
            BasicAoePrefab applicatorPrefab = CreateAoePrefab("ApplicatorAoeTemplate");
            BasicAoePrefab detonationPrefab = CreateAoePrefab("DetonationAoeTemplate");
            ProjectileSkill rootSkill = ScriptableObject.CreateInstance<ProjectileSkill>();
            AoeSkill applicatorSkill = ScriptableObject.CreateInstance<AoeSkill>();
            AoeSkill detonationSkill = ScriptableObject.CreateInstance<AoeSkill>();
            StackingSupport stackingSupport = ScriptableObject.CreateInstance<StackingSupport>();
            SkillSet rootSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet applicatorSet = ScriptableObject.CreateInstance<SkillSet>();
            SkillSet detonationSet = ScriptableObject.CreateInstance<SkillSet>();
            OnImpactAoeTrigger impactTrigger = ScriptableObject.CreateInstance<OnImpactAoeTrigger>();
            StackTrigger stackTrigger = ScriptableObject.CreateInstance<StackTrigger>();
            SkillLoadout loadout = ScriptableObject.CreateInstance<SkillLoadout>();
            GameObject driverObject = new("SkillDriverHarness");
            driverObject.SetActive(false);
            SkillDriver driver = driverObject.AddComponent<SkillDriver>();

            ConfigureProjectile(rootSkill, projectilePrefab, damage: 0f);
            ConfigureAoe(applicatorSkill, applicatorPrefab, damage: 0f);
            ConfigureAoe(detonationSkill, detonationPrefab, damage: 6f);
            SetField(stackingSupport, "stackThreshold", 2);
            SetField(stackingSupport, "debuffLifetimeSeconds", 10f);
            SetField(rootSet, "skill", rootSkill);
            SetField(rootSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(applicatorSet, "skill", applicatorSkill);
            SetField(applicatorSet, "supports", global::System.Array.Empty<SkillSupport>());
            SetField(detonationSet, "skill", detonationSkill);
            SetField(detonationSet, "supports", new SkillSupport[] { stackingSupport });
            SetField(loadout, "slots", new global::System.Collections.Generic.List<LoadoutSlot>
            {
                new SkillSetSlot { skillSet = rootSet },
                new TriggerLinkSlot { link = impactTrigger },
                new SkillSetSlot { skillSet = applicatorSet },
                new TriggerLinkSlot { link = stackTrigger },
                new SkillSetSlot { skillSet = detonationSet },
            });
            SetField(driver, "loadout", loadout);
            SetField(driver, "combatRoot", root);
            CompileAndRegister(driver);
            RuntimeSkillDefinition runtime = FirstCompiledRuntime(driver);
            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectileRuntime = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectileRuntime.ImpactAoeDefinition, Is.Not.Null);
            Assert.That(projectileRuntime.ImpactAoeDefinition.StackingDetonation, Is.Not.Null);

            MobRoot mob = CreateMobTarget(new Vector2(1f, 0f));
            mob.BindCombatRoot(root);
            mob.Register(root.TargetRegistry);
            SkillSpawnTranslator.Spawn(runtime, Vector2.zero, Vector2.right, Vector2.zero, root, CombatFaction.Player);
            for (int i = 0; i < 4; i++)
                yield return null;

            int debuffKey = projectileRuntime.ImpactAoeDefinition.StackingDetonation.DebuffKey;
            Assert.That(EcsDebuffStackCount(mob, debuffKey), Is.EqualTo(1));

            SkillSpawnTranslator.Spawn(runtime, Vector2.zero, Vector2.right, Vector2.zero, root, CombatFaction.Player);
            for (int i = 0; i < 8; i++)
                yield return null;

            Assert.That(EcsDebuffStackCount(mob, debuffKey), Is.EqualTo(0));
            Assert.That(mob.CurrentHealth, Is.EqualTo(mob.MaxHealth - 6f).Within(0.001f));

            Cleanup(
                rootObject,
                rootTemplateObject,
                projectilePrefab.gameObject,
                applicatorPrefab.gameObject,
                detonationPrefab.gameObject,
                driverObject,
                mob.gameObject);
            CleanupObjects(
                rootSkill,
                applicatorSkill,
                detonationSkill,
                stackingSupport,
                rootSet,
                applicatorSet,
                detonationSet,
                impactTrigger,
                stackTrigger,
                loadout);
        }

        private static AoeSpawnRequest Command(
            int typeId,
            GameObject templateObject,
            Vector2 position,
            float damage)
        {
            return new AoeSpawnRequest(
                typeId,
                position,
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
            renderer.sprite = CombatAtlasTestFixture.Sprite;

            var definition = new AoeTypeDefinition();
            definition.Configure(templateObject, shape);

            rootObject = new GameObject("AoeRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<CombatRoot>();
            root.ConfigureAtlas(CombatAtlasTestFixture.Atlas);
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
            sr.sprite = CombatAtlasTestFixture.Sprite;
            BasicAoePrefab basicPrefab = prefabObject.AddComponent<BasicAoePrefab>();
            basicPrefab.Configure(sr, hurtbox);

            AoeConfig config = ScriptableObject.CreateInstance<AoeConfig>();
            config.Configure(basicPrefab);
            return config;
        }

        private static BasicAttackPrefab CreateProjectilePrefab(string name)
        {
            GameObject prefabObject = new(name);
            prefabObject.SetActive(false);
            GameObject visualObject = new("Visual");
            visualObject.transform.SetParent(prefabObject.transform, false);
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CombatAtlasTestFixture.Sprite;
            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(prefabObject.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 0.35f;
            BasicAttackPrefab prefab = prefabObject.AddComponent<BasicAttackPrefab>();
            prefab.Configure(renderer, hurtbox);
            return prefab;
        }

        private static BasicAoePrefab CreateAoePrefab(string name)
        {
            GameObject prefabObject = new(name);
            prefabObject.SetActive(false);
            GameObject visualObject = new("Visual");
            visualObject.transform.SetParent(prefabObject.transform, false);
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CombatAtlasTestFixture.Sprite;
            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(prefabObject.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 1f;
            BasicAoePrefab prefab = prefabObject.AddComponent<BasicAoePrefab>();
            prefab.Configure(renderer, hurtbox);
            return prefab;
        }

        private static LingeringAoePrefab CreateLingeringAoePrefab(string name)
        {
            GameObject prefabObject = new(name);
            prefabObject.SetActive(false);
            GameObject visualObject = new("Visual");
            visualObject.transform.SetParent(prefabObject.transform, false);
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CombatAtlasTestFixture.Sprite;
            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.transform.SetParent(prefabObject.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 1f;
            LingeringAoePrefab prefab = prefabObject.AddComponent<LingeringAoePrefab>();
            prefab.Configure(renderer, hurtbox);
            return prefab;
        }

        private static void ConfigureProjectile(ProjectileSkill skill, BasicAttackPrefab prefab, float damage)
        {
            var definition = (ProjectileDefinition)skill.Definition;
            definition.prefab = prefab;
            definition.speed = 16f;
            definition.lifetime = 1f;
            definition.damage = damage;
            definition.directDamageEnabled = damage > 0f;
        }

        private static void ConfigureAoe(AoeSkill skill, BasicAoePrefab prefab, float damage)
        {
            var definition = (AoeDefinition)skill.Definition;
            definition.prefab = prefab;
            definition.baseAreaSize = 1f;
            definition.damage = damage;
            definition.directDamageEnabled = damage > 0f;
        }

        private static void ConfigureLingeringAoe(
            LingeringAoeSkill skill,
            LingeringAoePrefab prefab,
            float damage,
            float lifetime,
            float tickInterval = 0f)
        {
            var definition = (LingeringAoeDefinition)skill.Definition;
            definition.prefab = prefab;
            definition.baseAreaSize = 1f;
            definition.damage = damage;
            definition.directDamageEnabled = damage > 0f;
            definition.lifetimeSeconds = lifetime;
            definition.tickIntervalSeconds = tickInterval;
        }

        private static CombatRenderComponent FirstScopedAoeRenderComponent(CombatRoot root)
        {
            CombatFaction faction = Faction(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatRenderComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Faction == faction)
                {
                    return entityManager.GetComponentData<CombatRenderComponent>(entities[i]);
                }
            }

            Assert.Fail("No AOE render entity found for root.");
            return default;
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
            Sprite sprite = CombatAtlasTestFixture.Sprite;
            rootObject = new GameObject("ProjectileRoot");
            rootObject.SetActive(false);
            root = rootObject.AddComponent<CombatRoot>();
            root.Configure(sprite);
            root.ConfigureAtlas(CombatAtlasTestFixture.Atlas);
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
            MobRoot mob = mobObject.AddComponent<MobRoot>();
            mob.Configure(body, hurtbox, hurtbox, renderer, null);
            mob.ConfigureAuthoring(10f, 0f, 0.5f);
            mobObject.SetActive(true);
            return mob;
        }

        private static Entity ScopeEntity(CombatRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo scopeEntityField = typeof(CombatRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(root);
        }

        private static CombatFaction Faction(CombatRoot root) => CombatFaction.Player;

        private static void CompileAndRegister(SkillDriver driver)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "CompileAndRegister",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(driver, null);
        }

        private static RuntimeSkillDefinition FirstCompiledRuntime(SkillDriver driver)
        {
            return CompiledRuntime(driver, 0);
        }

        private static RuntimeSkillDefinition CompiledRuntime(SkillDriver driver, int index)
        {
            FieldInfo field = typeof(SkillDriver).GetField(
                "compiledSlots",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var compiledSlots = (RuntimeSkillDefinition[])field.GetValue(driver);
            Assert.That(compiledSlots, Is.Not.Null);
            Assert.That(compiledSlots.Length, Is.GreaterThan(index));
            Assert.That(compiledSlots[index], Is.Not.Null);
            return compiledSlots[index];
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void CleanupObjects(params Object[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                    Object.Destroy(objects[i]);
            }
        }

        private static StackEffectSnapshot StackEffect(
            CombatRoot root,
            AoeSpawnGeometry geometry,
            int debuffKey,
            int aoeTypeId,
            float damage,
            int threshold)
        {
            var detonationTemplate = new AoeSpawnCommand
            {
                TypeId = aoeTypeId,
                Lifetime = 0f,
                RepeatHitCooldownSeconds = 0f,
                Radius = geometry.Radius,
                ShapeType = geometry.ShapeType,
                HalfExtents = new Unity.Mathematics.float2(geometry.HalfExtents.x, geometry.HalfExtents.y),
                RotationRadians = geometry.RotationRadians,
                AreaSize = geometry.AreaSize,
                EchoCount = 1,
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = damage,
                    CritMultiplier = 1.5f,
                    DirectDamageEnabled = damage > 0f
                }
            };
            Hash128 detonationKey = root.RegisterSpawnTemplate(in detonationTemplate);

            return new StackEffectSnapshot
            {
                DebuffKey = debuffKey,
                Threshold = threshold,
                Lifetime = 10f,
                Faction = CombatFaction.None,
                Contribution = new StackContribution
                {
                    Damage = damage / threshold,
                    ProjectileCount = 0,
                    AreaSize = geometry.AreaSize / threshold
                },
                DetonationKind = StackDetonationKind.ImpactAoe,
                DetonationKey = detonationKey
            };
        }

        // ── AOE stack trigger tests ───────────────────────────────────────────────

        [UnityTest]
        public IEnumerator LingeringAoeInitialHitAndPulseApplyDebuffStacks()
        {
            // Lingering AOE (damage=0, tickInterval=0) applies Volatile stacks each hit.
            // Threshold=10 ensures detonation never fires within 2 frames; a registered
            // detonation type is still required for StackEffectSnapshot.Enabled = true.
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out _);
            var lingeringDef = new AoeTypeDefinition();
            lingeringDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int lingeringTypeId = root.RegisterType(lingeringDef);
            var detonationDef = new AoeTypeDefinition();
            detonationDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int detonationTypeId = root.RegisterType(detonationDef);

            MobRoot mob = CreateMobTarget(Vector2.zero);
            mob.BindCombatRoot(root);
            mob.Register(root.TargetRegistry);

            AoeSpawnGeometry geometry = Geometry(templateObject, 2f);
            var stackEffect = StackEffect(root, geometry, VolatileStackKey, detonationTypeId, 0f, 10);

            root.Spawn(new AoeSpawnRequest(
                lingeringTypeId,
                Vector2.zero,
                new DamageSnapshot(0f),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 0f,
                geometry: geometry,
                stackEffect: stackEffect), CombatFaction.Player);

            yield return null; // frame 1: initial hit, 1 stack
            Assert.That(EcsDebuffStackCount(mob, VolatileStackKey), Is.EqualTo(1),
                "Initial AOE hit should apply 1 Volatile stack.");

            yield return null; // frame 2: pulse hit (gate expired at dt=0), 2 stacks
            Assert.That(EcsDebuffStackCount(mob, VolatileStackKey), Is.EqualTo(2),
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
            mob.BindCombatRoot(root);
            mob.Register(root.TargetRegistry);

            const float ChainDamage = 5f;
            AoeSpawnGeometry geometry = Geometry(templateObject, 2f);
            var stackEffect = StackEffect(root, geometry, VolatileStackKey, pulseTypeId, ChainDamage, 3);

            root.Spawn(new AoeSpawnRequest(
                lingeringTypeId,
                Vector2.zero,
                new DamageSnapshot(0f),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 0f,
                geometry: geometry,
                stackEffect: stackEffect), CombatFaction.Player);

            yield return null; // frame 1: 1 stack
            yield return null; // frame 2: 2 stacks
            yield return null; // frame 3: 3 stacks -> threshold -> detonation spawned

            Assert.That(ScopedAoeCount(root, pulseTypeId), Is.EqualTo(1),
                "Stack threshold should have spawned the linked pulse AOE.");
            Assert.That(EcsDebuffStackCount(mob, VolatileStackKey), Is.EqualTo(0),
                "Stacks should be cleared after the threshold fires.");

            yield return null; // frame 4: detonation pulse materialises and hits

            Assert.That(mob.CurrentHealth, Is.EqualTo(mob.MaxHealth - ChainDamage).Within(0.001f),
                "Chain pulse AOE should deal its damage on the frame it materialises.");

            Cleanup(rootObject, templateObject, mob.gameObject);
        }

        [UnityTest]
        public IEnumerator StackAccrualKeepsDifferentDebuffKeysIndependent()
        {
            CreateAoeFixture(out GameObject rootObject, out CombatRoot root, out GameObject templateObject, out _);
            var poisonTypeDef = new AoeTypeDefinition();
            poisonTypeDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int poisonTypeId = root.RegisterType(poisonTypeDef);
            var burningTypeDef = new AoeTypeDefinition();
            burningTypeDef.Configure(templateObject, templateObject.GetComponentInChildren<CircleCollider2D>(true));
            int burningTypeId = root.RegisterType(burningTypeDef);

            MobRoot mob = CreateMobTarget(Vector2.zero);
            mob.Register(root.TargetRegistry);

            AoeSpawnGeometry geometry = Geometry(templateObject, 2f);
            root.Spawn(new AoeSpawnRequest(
                poisonTypeId,
                Vector2.zero,
                new DamageSnapshot(0f),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 0f,
                geometry: geometry,
                stackEffect: SingleStageStackEffect(root, geometry, PoisonStackKey, poisonTypeId, 2)), CombatFaction.Player);
            root.Spawn(new AoeSpawnRequest(
                burningTypeId,
                Vector2.zero,
                new DamageSnapshot(0f),
                lifetimeSeconds: 10f,
                tickIntervalSeconds: 0f,
                geometry: geometry,
                stackEffect: SingleStageStackEffect(root, geometry, BurningStackKey, burningTypeId, 3)), CombatFaction.Player);

            yield return null;
            Assert.That(EcsDebuffStackCount(mob, PoisonStackKey), Is.EqualTo(1));
            Assert.That(EcsDebuffStackCount(mob, BurningStackKey), Is.EqualTo(1));
            Assert.That(ScopedAoeCount(root, poisonTypeId), Is.EqualTo(1));
            Assert.That(ScopedAoeCount(root, burningTypeId), Is.EqualTo(1));

            yield return null;
            Assert.That(EcsDebuffStackCount(mob, PoisonStackKey), Is.EqualTo(0));
            Assert.That(EcsDebuffStackCount(mob, BurningStackKey), Is.EqualTo(2));
            Assert.That(ScopedAoeCount(root, poisonTypeId), Is.EqualTo(2));
            Assert.That(ScopedAoeCount(root, burningTypeId), Is.EqualTo(1));

            yield return null;
            Assert.That(EcsDebuffStackCount(mob, PoisonStackKey), Is.EqualTo(1));
            Assert.That(EcsDebuffStackCount(mob, BurningStackKey), Is.EqualTo(0));
            Assert.That(ScopedAoeCount(root, poisonTypeId), Is.EqualTo(2));
            Assert.That(ScopedAoeCount(root, burningTypeId), Is.EqualTo(2));

            Cleanup(rootObject, templateObject, mob.gameObject);
        }

        private static void Cleanup(params GameObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                Object.Destroy(objects[i]);
            }
        }

        private static int EcsDebuffStackCount(ICombatTarget target, int debuffKey)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            Assert.That(target.CombatTargetProxy, Is.Not.EqualTo(Entity.Null));
            Assert.That(entityManager.HasBuffer<TargetStackEntry>(target.CombatTargetProxy), Is.True);

            DynamicBuffer<TargetStackEntry> entries =
                entityManager.GetBuffer<TargetStackEntry>(target.CombatTargetProxy);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].DebuffKey == debuffKey)
                    return entries[i].Count;
            }

            return 0;
        }

        private static StackEffectSnapshot SingleStageStackEffect(
            CombatRoot root,
            AoeSpawnGeometry geometry,
            int debuffKey,
            int aoeTypeId,
            int threshold)
        {
            return StackEffect(root, geometry, debuffKey, aoeTypeId, 0f, threshold);
        }

        private static int ScopedAoeCount(CombatRoot root, int typeId)
        {
            int count = 0;
            CombatFaction faction = Faction(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Faction == faction && identity.TypeId == typeId)
                    count++;
            }

            return count;
        }

        private static int ScopedProjectileCount(CombatRoot root, int typeId)
        {
            int count = 0;
            CombatFaction faction = Faction(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ProjectileIdentityComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                ProjectileIdentityComponent identity =
                    entityManager.GetComponentData<ProjectileIdentityComponent>(entities[i]);
                if (identity.Faction == faction && identity.TypeId == typeId)
                    count++;
            }

            return count;
        }

        private static CombatHitPayload FirstScopedAoeHitPayload(CombatRoot root, int typeId)
        {
            CombatFaction faction = Faction(root);
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<AoeHitSpawnComponent>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                AoeIdentityComponent identity = entityManager.GetComponentData<AoeIdentityComponent>(entities[i]);
                if (identity.Faction == faction && identity.TypeId == typeId)
                    return entityManager.GetComponentData<AoeHitSpawnComponent>(entities[i]).HitPayload;
            }

            Assert.Fail("No matching AOE hit payload found.");
            return default;
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
