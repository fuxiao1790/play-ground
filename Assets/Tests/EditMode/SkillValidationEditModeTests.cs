using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class SkillValidationEditModeTests
    {
        private readonly List<Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                    Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void ValidatorWarnsWhenProjectileSupportIsOnAoeSkill()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("Aoe Skill");
            MultipleProjectilesSupport support = CreateAsset<MultipleProjectilesSupport>("Multiple Projectiles");
            SkillSet set = CreateSkillSet("Aoe Set", skill, support);

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(set));

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedSupportForSkill));
            Assert.That(warnings[0].Message, Does.Contain("will be ignored"));
        }

        [Test]
        public void ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedTriggerTarget));
            Assert.That(warnings[0].Message, Does.Contain("will do nothing"));
        }

        [Test]
        public void CompilerIgnoresProjectileIntervalTargetAoeSkill()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(((RuntimeProjectileDefinition)runtime).ChildSpawnSetup, Is.Null);
        }

        [Test]
        public void CompilerMapsProjectileEnergyRateCostAndThresholdJitter()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            trigger.energyPerSecond = 2f;
            trigger.energyJitterPercent = 25f;
            ((ProjectileDefinition)targetSkill.Definition).manaCost = 4f;
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.EnergyPerSecond, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(setup.EnergyThreshold, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(setup.EnergyThresholdJitter, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void CompilerMapsAoeEnergyRateCostAndThresholdJitter()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            AoeIntervalSpawnTrigger trigger = CreateAsset<AoeIntervalSpawnTrigger>("AOE Interval Spawn");
            trigger.energyPerSecond = 1.5f;
            trigger.energyJitterPercent = 10f;
            trigger.echoCount = 2;
            ((AoeDefinitionBase)targetSkill.Definition).manaCost = 3f;
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectile.ChildSpawnSetup, Is.Null);
            Assert.That(projectile.AoeIntervalSpawnSetup, Is.Not.Null);
            Assert.That(projectile.AoeIntervalSpawnSetup.ChildDefinition, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(projectile.AoeIntervalSpawnSetup.EnergyPerSecond, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(projectile.AoeIntervalSpawnSetup.EnergyThreshold, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(projectile.AoeIntervalSpawnSetup.EnergyThresholdJitter, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(projectile.AoeIntervalSpawnSetup.Count, Is.EqualTo(3));
        }

        [Test]
        public void CompilerRaisesChildEnergyThresholdForManaCostSupport()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            ((ProjectileDefinition)targetSkill.Definition).manaCost = 2f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet(
                "Child Projectile Set",
                targetSkill,
                CreateAsset<MultipleProjectilesSupport>("Multiple Projectiles"));
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup.ChildDefinition.ManaCost, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(setup.EnergyThreshold, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void CompilerAppliesIntervalManaToEnergyRatio()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            ((ProjectileDefinition)targetSkill.Definition).manaCost = 4f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            trigger.manaToEnergyRatio = 2f;

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup.EnergyThreshold, Is.EqualTo(8f).Within(0.0001f));
        }

        [Test]
        public void CompilerPopulatesAoeIntervalScatterRadius()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            ((AoeDefinitionBase)targetSkill.Definition).scatterRadius = 9f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            AoeIntervalSpawnTrigger trigger = CreateAsset<AoeIntervalSpawnTrigger>("AOE Interval Spawn");
            trigger.scatterRadius = 2f;
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            RuntimeAoeIntervalSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).AoeIntervalSpawnSetup;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.ScatterRadius, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void CompilerPopulatesProjectileIntervalSetupOnLingeringAoeSource()
        {
            LingeringAoeSkill sourceSkill = CreateAsset<LingeringAoeSkill>("Lingering AOE Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Lingering AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Projectile Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.ChildSpawnSetup, Is.Not.Null);
            Assert.That(aoe.ChildSpawnSetup.ChildDefinition, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(aoe.AoeIntervalSpawnSetup, Is.Null);
        }

        [Test]
        public void CompilerPopulatesAoeIntervalSetupOnLingeringAoeSource()
        {
            LingeringAoeSkill sourceSkill = CreateAsset<LingeringAoeSkill>("Lingering AOE Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Lingering AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            AoeIntervalSpawnTrigger trigger = CreateAsset<AoeIntervalSpawnTrigger>("AOE Interval Spawn");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.ChildSpawnSetup, Is.Null);
            Assert.That(aoe.AoeIntervalSpawnSetup, Is.Not.Null);
            Assert.That(aoe.AoeIntervalSpawnSetup.ChildDefinition, Is.TypeOf<RuntimeAoeDefinition>());
        }

        [Test]
        public void CompilerLeavesPulseAoeIntervalSourceAsNoOp()
        {
            AoeSkill sourceSkill = CreateAsset<AoeSkill>("Pulse AOE Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Pulse AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Projectile Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.ChildSpawnSetup, Is.Null);
            Assert.That(aoe.AoeIntervalSpawnSetup, Is.Null);
        }

        [Test]
        public void ValidatorWarnsWhenIntervalSpawnSourceIsPulseAoe()
        {
            AoeSkill sourceSkill = CreateAsset<AoeSkill>("Pulse AOE Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Pulse AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Projectile Set", targetSkill);
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(HasWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, 0, "Pulse AOEs have no duration"), Is.True);
        }

        [Test]
        public void ValidatorWarnsWhenAoeIntervalSpawnSourceIsPulseAoe()
        {
            AoeSkill sourceSkill = CreateAsset<AoeSkill>("Pulse AOE Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Pulse AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            AoeIntervalSpawnTrigger trigger = CreateAsset<AoeIntervalSpawnTrigger>("AOE Interval Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(HasWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, 0, "Pulse AOEs have no duration"), Is.True);
        }

        [Test]
        public void CompilerTreatsRegularAoeAsPulseAoe()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("Regular AOE Skill");
            SkillSet set = CreateSkillSet("Regular AOE Set", skill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.LifetimeSeconds, Is.EqualTo(0f));
            Assert.That(aoe.TickIntervalSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void CompilerInvokesConversionSupportCompileHook()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            TrackingConversionSupport support = CreateAsset<TrackingConversionSupport>("Conversion Support");
            SkillSet set = CreateSkillSet("Projectile Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(support.WasCompiled, Is.True);
            Assert.That(support.DefinitionSeen, Is.SameAs(skill.Definition));
            Assert.That(support.RuntimeSeen, Is.SameAs(runtime));
        }

        [Test]
        public void CompilerBuildsStackingSupportRuntimeDetonation()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SetField(stackingSupport, "stackThreshold", 4);
            SetField(stackingSupport, "debuffLifetimeSeconds", 6f);
            SetField(stackingSupport, "stacksPerHit", 2);
            SkillSet set = CreateSkillSet("Stacking Support Set", skill, stackingSupport);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeStackingDetonation>());
            var stacking = (RuntimeStackingDetonation)runtime;
            Assert.That(stacking.Detonation, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(stacking.StackThreshold, Is.EqualTo(4));
            Assert.That(stacking.DebuffLifetimeSeconds, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(stacking.StacksPerHit, Is.EqualTo(2));
            Assert.That(stacking.DebuffName, Is.EqualTo("AOE Skill"));
            Assert.That(stacking.DebuffKey, Is.EqualTo(-1));
            Assert.That(typeof(StackingSupport).GetField("debuffName", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
            Assert.That(typeof(StackingSupport).GetField("cosmeticDebuffStatus", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
        }

        [Test]
        public void CompilerAppliesAdditiveSupportsToStackingSupportDetonation()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            AddedDamageSupport damageSupport = CreateAsset<AddedDamageSupport>("Added Damage");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet set = CreateSkillSet("Stacking Support Set", skill, damageSupport, stackingSupport);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            var stacking = (RuntimeStackingDetonation)runtime;
            Assert.That(((RuntimeAoeDefinition)stacking.Detonation).Damage, Is.EqualTo(15f).Within(0.0001f));
        }

        [Test]
        public void CompilerCopiesLingeringAoeTiming()
        {
            LingeringAoeSkill skill = CreateAsset<LingeringAoeSkill>("Lingering AOE Skill");
            var definition = (LingeringAoeDefinition)skill.Definition;
            definition.lifetimeSeconds = 3f;
            definition.tickIntervalSeconds = 0.4f;
            SkillSet set = CreateSkillSet("Lingering AOE Set", skill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.LifetimeSeconds, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(aoe.TickIntervalSeconds, Is.EqualTo(0.4f).Within(0.0001f));
        }

        [Test]
        public void LingeringAoeDefinitionUsesLingeringPrefabField()
        {
            FieldInfo field = typeof(LingeringAoeDefinition).GetField(
                "prefab",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(LingeringAoePrefab)));
        }

        [Test]
        public void RegistrationAssignsDistinctStackingDetonationDebuffKeys()
        {
            var first = new RuntimeStackingDetonation();
            var second = new RuntimeStackingDetonation();

            AssignStackingDetonationDebuffKey(first);
            AssignStackingDetonationDebuffKey(second);
            int firstKey = first.DebuffKey;

            AssignStackingDetonationDebuffKey(first);

            Assert.That(firstKey, Is.GreaterThan(0));
            Assert.That(second.DebuffKey, Is.GreaterThan(0));
            Assert.That(second.DebuffKey, Is.Not.EqualTo(firstKey));
            Assert.That(first.DebuffKey, Is.EqualTo(firstKey));
        }

        [Test]
        public void ValidatorDoesNotWarnForProjectileToAoeImpactLink()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
            OnImpactAoeTrigger trigger = CreateAsset<OnImpactAoeTrigger>("Impact AOE");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void CompilerAttachesStackTriggerToProjectileApplicator()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Stack Detonation Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Stack Detonation Set", targetSkill, stackingSupport);
            StackTrigger trigger = CreateAsset<StackTrigger>("Stack Trigger");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectile.StackingDetonation, Is.Not.Null);
            Assert.That(projectile.StackingDetonation.Detonation, Is.TypeOf<RuntimeAoeDefinition>());
        }

        [Test]
        public void CompilerAttachesStackTriggerAfterProjectileIntervalSpawnLink()
        {
            ProjectileSkill rootSkill = CreateAsset<ProjectileSkill>("Root Projectile Skill");
            ProjectileSkill applicatorSkill = CreateAsset<ProjectileSkill>("Applicator Projectile Skill");
            AoeSkill detonationSkill = CreateAsset<AoeSkill>("Stack Detonation Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet rootSet = CreateSkillSet("Root Projectile Set", rootSkill);
            SkillSet applicatorSet = CreateSkillSet("Applicator Projectile Set", applicatorSkill);
            SkillSet detonationSet = CreateSkillSet("Stack Detonation Set", detonationSkill, stackingSupport);
            ProjectileIntervalSpawnTrigger childTrigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            StackTrigger stackTrigger = CreateAsset<StackTrigger>("Stack Trigger");
            var nodes = new[]
            {
                new SkillLoadoutNode(rootSet, childTrigger),
                new SkillLoadoutNode(applicatorSet, stackTrigger),
                new SkillLoadoutNode(detonationSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var rootProjectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(rootProjectile.ChildSpawnSetup, Is.Not.Null);
            Assert.That(rootProjectile.ChildSpawnSetup.ChildDefinition.StackingDetonation, Is.Not.Null);
        }

        [Test]
        public void CompilerAttachesStackTriggerAfterNormalImpactLinks()
        {
            ProjectileSkill rootSkill = CreateAsset<ProjectileSkill>("Root Projectile Skill");
            AoeSkill aoeApplicatorSkill = CreateAsset<AoeSkill>("Applicator AOE Skill");
            ProjectileSkill projectileApplicatorSkill = CreateAsset<ProjectileSkill>("Applicator Projectile Skill");
            AoeSkill detonationSkill = CreateAsset<AoeSkill>("Stack Detonation Skill");
            StackingSupport firstStackingSupport = CreateAsset<StackingSupport>("First Stacking Support");
            StackingSupport secondStackingSupport = CreateAsset<StackingSupport>("Second Stacking Support");
            SkillSet rootSet = CreateSkillSet("Root Projectile Set", rootSkill);
            SkillSet aoeApplicatorSet = CreateSkillSet("Applicator AOE Set", aoeApplicatorSkill);
            SkillSet projectileApplicatorSet = CreateSkillSet("Applicator Projectile Set", projectileApplicatorSkill);
            SkillSet firstDetonationSet = CreateSkillSet("First Stack Detonation Set", detonationSkill, firstStackingSupport);
            SkillSet secondDetonationSet = CreateSkillSet("Second Stack Detonation Set", detonationSkill, secondStackingSupport);
            OnImpactAoeTrigger impactAoeTrigger = CreateAsset<OnImpactAoeTrigger>("Impact AOE");
            OnImpactProjectileTrigger impactProjectileTrigger = CreateAsset<OnImpactProjectileTrigger>("Impact Projectile");
            StackTrigger firstStackTrigger = CreateAsset<StackTrigger>("First Stack Trigger");
            StackTrigger secondStackTrigger = CreateAsset<StackTrigger>("Second Stack Trigger");
            var nodes = new SkillLoadoutNode[]
            {
                new(rootSet, impactAoeTrigger),
                new(aoeApplicatorSet, firstStackTrigger),
                new(firstDetonationSet),
                new(rootSet, impactProjectileTrigger),
                new(projectileApplicatorSet, secondStackTrigger),
                new(secondDetonationSet),
            };

            RuntimeSkillDefinition impactAoeRuntime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);
            RuntimeSkillDefinition impactProjectileRuntime = SkillSetCompiler.Compile(nodes, 3, SkillStatSnapshot.Identity);

            Assert.That(impactAoeRuntime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(((RuntimeProjectileDefinition)impactAoeRuntime).ImpactAoeDefinition, Is.Not.Null);
            Assert.That(((RuntimeProjectileDefinition)impactAoeRuntime).ImpactAoeDefinition.StackingDetonation, Is.Not.Null);
            Assert.That(impactProjectileRuntime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(((RuntimeProjectileDefinition)impactProjectileRuntime).ImpactProjectileDefinition, Is.Not.Null);
            Assert.That(((RuntimeProjectileDefinition)impactProjectileRuntime).ImpactProjectileDefinition.StackingDetonation, Is.Not.Null);
        }

        [Test]
        public void CompilerAttachesStackTriggerToStackingDetonationApplicator()
        {
            LingeringAoeSkill applicatorSkill = CreateAsset<LingeringAoeSkill>("Lingering AOE Applicator");
            LingeringAoeSkill firstDetonationSkill = CreateAsset<LingeringAoeSkill>("Stacking Lingering AOE");
            AoeSkill secondDetonationSkill = CreateAsset<AoeSkill>("Stacking AOE");
            StackingSupport firstStackingSupport = CreateAsset<StackingSupport>("First Stacking Support");
            StackingSupport secondStackingSupport = CreateAsset<StackingSupport>("Second Stacking Support");
            SkillSet applicatorSet = CreateSkillSet("Lingering Applicator Set", applicatorSkill);
            SkillSet firstDetonationSet = CreateSkillSet("Stacking Lingering Set", firstDetonationSkill, firstStackingSupport);
            SkillSet secondDetonationSet = CreateSkillSet("Stacking AOE Set", secondDetonationSkill, secondStackingSupport);
            StackTrigger firstStackTrigger = CreateAsset<StackTrigger>("First Stack Trigger");
            StackTrigger secondStackTrigger = CreateAsset<StackTrigger>("Second Stack Trigger");
            var nodes = new[]
            {
                new SkillLoadoutNode(applicatorSet, firstStackTrigger),
                new SkillLoadoutNode(firstDetonationSet, secondStackTrigger),
                new SkillLoadoutNode(secondDetonationSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var applicator = (RuntimeAoeDefinition)runtime;
            Assert.That(applicator.StackingDetonation, Is.Not.Null);

            // The first stacking detonation is itself the applicator for the second
            // stack trigger: its inner spawned definition must carry the downstream
            // stacking detonation, not the RuntimeStackingDetonation wrapper.
            Assert.That(applicator.StackingDetonation.Detonation, Is.TypeOf<RuntimeAoeDefinition>());
            var firstDetonation = (RuntimeAoeDefinition)applicator.StackingDetonation.Detonation;
            Assert.That(firstDetonation.StackingDetonation, Is.Not.Null);
            Assert.That(firstDetonation.StackingDetonation.Detonation, Is.TypeOf<RuntimeAoeDefinition>());
        }

        [Test]
        public void DriverDoesNotBindStackingSupportSetAsRoot()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet set = CreateSkillSet("Stacking Set", skill, stackingSupport);
            SkillLoadout loadout = CreateLoadout("Loadout", new SkillLoadoutNode(set));
            var gameObject = new GameObject("Player Skill Driver Test");
            createdObjects.Add(gameObject);
            SkillDriver driver = gameObject.AddComponent<SkillDriver>();
            SetField(driver, "loadout", loadout);

            CompileAndRegister(driver);

            Assert.That(driver.SlotCount, Is.EqualTo(0));
        }

        [Test]
        public void ValidatorWarnsWhenStackingSupportSetIsNotStackTriggerEffect()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet set = CreateSkillSet("Stacking Set", skill, stackingSupport);

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(set));

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedStackingDetonation));
            Assert.That(warnings[0].Message, Does.Contain("not the effect of a StackTrigger"));
        }

        [Test]
        public void ValidatorWarnsWhenStackTriggerTargetHasNoStackingSupport()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            StackTrigger trigger = CreateAsset<StackTrigger>("Stack Trigger");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedStackingDetonation));
            Assert.That(warnings[0].Message, Does.Contain("with no StackingSupport"));
        }

        [Test]
        public void ValidatorWarnsWhenStackingSupportSetIsTargetedByNonStackTrigger()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Stack Detonation Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Stacking Set", targetSkill, stackingSupport);
            OnImpactAoeTrigger trigger = CreateAsset<OnImpactAoeTrigger>("Impact AOE");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(HasWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, 0, "is not a StackTrigger"), Is.True);
        }

        private SkillValidationWarning[] Validate(params SkillLoadoutNode[] nodes)
        {
            var list = new List<SkillLoadoutNode>(nodes);
            var warnings = new List<SkillValidationWarning>();
            SkillLoadoutValidator.Validate(list, warnings);
            return warnings.ToArray();
        }

        private SkillLoadout CreateLoadout(string name, params SkillLoadoutNode[] nodes)
        {
            SkillLoadout loadout = CreateAsset<SkillLoadout>(name);
            SetField(loadout, "nodes", new List<SkillLoadoutNode>(nodes));
            return loadout;
        }

        private static bool HasWarning(
            SkillValidationWarning[] warnings,
            SkillValidationWarningCode code,
            int slotIndex,
            string messageFragment)
        {
            for (int i = 0; i < warnings.Length; i++)
            {
                if (warnings[i].Code == code
                    && warnings[i].SlotIndex == slotIndex
                    && warnings[i].Message.Contains(messageFragment))
                {
                    return true;
                }
            }

            return false;
        }

        private SkillSet CreateSkillSet(string name, Skill skill, params SkillSupport[] supports)
        {
            SkillSet set = CreateAsset<SkillSet>(name);
            SetField(set, "skill", skill);
            SetField(set, "supports", supports);
            return set;
        }

        private T CreateAsset<T>(string name) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            createdObjects.Add(asset);
            return asset;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void AssignStackingDetonationDebuffKey(RuntimeStackingDetonation definition)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "EnsureStackingDetonationDebuffKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { definition });
        }

        private static void CompileAndRegister(SkillDriver driver)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "CompileAndRegister",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(driver, null);
        }

        private sealed class TrackingConversionSupport : ConversionSupport
        {
            public bool WasCompiled { get; private set; }
            public SkillDefinition DefinitionSeen { get; private set; }
            public RuntimeSkillDefinition RuntimeSeen { get; private set; }

            public override bool ConvertsToTriggeredOnly => true;

            public override RuntimeSkillDefinition Compile(
                SkillDefinition definition,
                RuntimeSkillDefinition runtime,
                SkillStatSnapshot snapshot)
            {
                WasCompiled = true;
                DefinitionSeen = definition;
                RuntimeSeen = runtime;
                return runtime;
            }
        }
    }
}
