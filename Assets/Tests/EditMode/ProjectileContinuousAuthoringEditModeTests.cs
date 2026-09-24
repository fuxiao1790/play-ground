using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Modifiers;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Authoring;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Projectiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGround.Tests.EditMode
{
    public sealed class ProjectileContinuousAuthoringEditModeTests
    {
        private readonly List<UnityEngine.Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void OnValidate_ReportsAuthoredContinuousTrackingConflict()
        {
            var definition = new ProjectileDefinition { continuousCollision = true, trackingEnabled = true };

            LogAssert.Expect(LogType.Error, "Projectile definitions cannot enable both continuous collision and tracking.");
            definition.OnValidate();
        }

        [Test]
        public void Compiler_BlocksContinuousProjectileWhenHomingSupportEnablesTracking()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>();
            ((ProjectileDefinition)skill.Definition).continuousCollision = true;
            HomingSupport support = CreateAsset<HomingSupport>();
            SkillSet set = CreateSkillSet(skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectile.ContinuousCollision, Is.True);
            Assert.That(projectile.Tracking.Enabled, Is.True);
            Assert.That(projectile.SpawnBlocked, Is.True,
                "Conflict must block spawn rather than silently disabling tracking.");
        }

        [Test]
        public void Driver_ReportsBlockedConflictAndRefundsReadySlot()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>();
            ((ProjectileDefinition)skill.Definition).continuousCollision = true;
            HomingSupport support = CreateAsset<HomingSupport>();
            SkillSet set = CreateSkillSet(skill, support);
            SkillLoadout loadout = CreateAsset<SkillLoadout>();
            SetField(loadout, "nodes", new List<SkillLoadoutNode> { new(set) });
            var gameObject = new GameObject("Continuous Projectile Driver Test");
            createdObjects.Add(gameObject);
            SkillDriver driver = gameObject.AddComponent<SkillDriver>();
            SetField(driver, "loadout", loadout);

            InvokeCompileAndRegister(driver);
            Assert.That(ContainsWarning(
                driver.ValidationWarnings,
                SkillValidationWarningCode.ContinuousCollisionCannotTrack,
                SkillValidationSeverity.Error), Is.True);

            SkillSlotState state = driver.GetSlotState(0);
            Assert.That(state.IsReady, Is.True);
            driver.Tick(true, Vector2.right, Vector2.right);
            Assert.That(state.IsReady, Is.True, "Blocked slot refunds its fire instead of consuming cooldown.");
        }

        [Test]
        public void LaneRouting_IsNotStatOrBehaviorContextModifiable()
        {
            Assert.That(Enum.GetNames(typeof(SkillStat)), Does.Not.Contain("ContinuousCollision"));
            Assert.That(typeof(ProjectileBehaviorContext).GetProperty(
                "ContinuousCollision", BindingFlags.Instance | BindingFlags.Public), Is.Null);
            Assert.That(typeof(ProjectileBehaviorContext).GetMethod(
                "EnableContinuousCollision", BindingFlags.Instance | BindingFlags.Public), Is.Null);
        }

        [Test]
        public void Compiler_OnlyFlagsFastTrackingProjectilesForTunneling()
        {
            RuntimeProjectileDefinition fastTracking = CompileProjectile(speed: 100f, tracking: true);
            RuntimeProjectileDefinition fastDiscrete = CompileProjectile(speed: 100f, tracking: false);
            RuntimeProjectileDefinition slowTracking = CompileProjectile(speed: 1f, tracking: true);

            Assert.That(fastTracking.TrackingMayTunnel, Is.True);
            Assert.That(fastDiscrete.TrackingMayTunnel, Is.False);
            Assert.That(slowTracking.TrackingMayTunnel, Is.False);
        }

        [Test]
        public void SimulationAssembly_DoesNotContainCompilerLintConstant()
        {
            Assembly simulation = typeof(ProjectileTag).Assembly;
            Assert.That(simulation.GetType(typeof(SkillSetCompiler).FullName), Is.Null);
            Assert.That(simulation.GetTypes(), Has.None.Matches<Type>(type =>
                type.GetField("SmallestExpectedTargetRadius", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null));
        }

        [Test]
        public void ContinuousFlagReachesCommandsForDirectIntervalAndStackPaths()
        {
            BasicAttackPrefab prefab = CreateProjectilePrefab();
            ProjectileSkill directSkill = CreateProjectileSkill(prefab, continuous: true);
            RuntimeProjectileDefinition direct = CompileProjectile(directSkill);

            ProjectileSkill intervalRootSkill = CreateProjectileSkill(prefab, continuous: false);
            ProjectileSkill intervalChildSkill = CreateProjectileSkill(prefab, continuous: true);
            IntervalSpawnTrigger interval = CreateAsset<IntervalSpawnTrigger>();
            RuntimeProjectileDefinition intervalRoot = CompileProjectile(
                CreateSkillSet(intervalRootSkill), interval, CreateSkillSet(intervalChildSkill));

            ProjectileSkill stackRootSkill = CreateProjectileSkill(prefab, continuous: false);
            ProjectileSkill stackChildSkill = CreateProjectileSkill(prefab, continuous: true);
            RuntimeProjectileDefinition stackRoot = CompileProjectile(
                CreateSkillSet(stackRootSkill),
                CreateAsset<StackTrigger>(),
                CreateSkillSet(stackChildSkill));

            Assert.That(BuildCommand(direct).ContinuousCollision, Is.EqualTo(1), "Direct cast");
            Assert.That(BuildCommand(intervalRoot.ChildSpawnSetup.ChildDefinition).ContinuousCollision, Is.EqualTo(1), "Interval child");
            Assert.That(BuildCommand((RuntimeProjectileDefinition)stackRoot.StackingDetonation.Detonation).ContinuousCollision,
                Is.EqualTo(1), "Stack detonation");
        }

        [Test]
        public void ShippedProjectileSkillsDoNotTriggerTrackingTunnelingLint()
        {
            string[] guids = AssetDatabase.FindAssets("t:ProjectileSkill", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                ProjectileSkill skill = AssetDatabase.LoadAssetAtPath<ProjectileSkill>(path);
                if (skill == null)
                    continue;

                SkillSet set = CreateSkillSet(skill);
                var runtime = (RuntimeProjectileDefinition)SkillSetCompiler.Compile(
                    new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);
                Assert.That(runtime.TrackingMayTunnel, Is.False, path);
            }
        }

        private RuntimeProjectileDefinition CompileProjectile(float speed, bool tracking)
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>();
            var definition = (ProjectileDefinition)skill.Definition;
            definition.speed = speed;
            definition.trackingEnabled = tracking;
            SkillSet set = CreateSkillSet(skill);
            return (RuntimeProjectileDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);
        }

        private RuntimeProjectileDefinition CompileProjectile(ProjectileSkill skill)
        {
            return (RuntimeProjectileDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(CreateSkillSet(skill)) }, 0, SkillStatSnapshot.Identity);
        }

        private RuntimeProjectileDefinition CompileProjectile(
            SkillSet root,
            TriggerLink trigger,
            SkillSet child)
        {
            return (RuntimeProjectileDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(root, trigger), new SkillLoadoutNode(child) },
                0,
                SkillStatSnapshot.Identity);
        }

        private ProjectileSkill CreateProjectileSkill(BasicAttackPrefab prefab, bool continuous)
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>();
            var definition = (ProjectileDefinition)skill.Definition;
            definition.prefab = prefab;
            definition.continuousCollision = continuous;
            return skill;
        }

        private BasicAttackPrefab CreateProjectilePrefab()
        {
            var gameObject = new GameObject("Continuous Test Projectile Prefab");
            gameObject.SetActive(false);
            CircleCollider2D hurtbox = gameObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 0.25f;
            BasicAttackPrefab prefab = gameObject.AddComponent<BasicAttackPrefab>();
            prefab.Configure(null, hurtbox);
            createdObjects.Add(gameObject);
            return prefab;
        }

        private static ProjectileSpawnCommand BuildCommand(RuntimeProjectileDefinition definition)
        {
            Type builder = typeof(SkillDriver).Assembly.GetType("PlayGround.Skills.SkillIntervalTemplateBuilder");
            Assert.That(builder, Is.Not.Null);
            MethodInfo method = builder.GetMethod("BuildProjectileTemplate", BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            ParameterInfo[] parameters = method.GetParameters();
            object command = method.Invoke(null, new object[]
            {
                definition,
                ProjectileChildSpawnBehavior.Default,
                null,
                Activator.CreateInstance(parameters[3].ParameterType),
                Activator.CreateInstance(parameters[4].ParameterType),
                Activator.CreateInstance(parameters[5].ParameterType),
                0f
            });
            return (ProjectileSpawnCommand)command;
        }

        private SkillSet CreateSkillSet(Skill skill, params SkillSupport[] supports)
        {
            SkillSet set = CreateAsset<SkillSet>();
            SetField(set, "skill", skill);
            SetField(set, "supports", supports);
            return set;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            createdObjects.Add(asset);
            return asset;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {name}.");
            field.SetValue(target, value);
        }

        private static void InvokeCompileAndRegister(SkillDriver driver)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "CompileAndRegister", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(driver, null);
        }

        private static bool ContainsWarning(
            IReadOnlyList<SkillValidationWarning> warnings,
            SkillValidationWarningCode code,
            SkillValidationSeverity severity)
        {
            for (int i = 0; i < warnings.Count; i++)
            {
                if (warnings[i].Code == code && warnings[i].Severity == severity)
                    return true;
            }

            return false;
        }
    }
}
