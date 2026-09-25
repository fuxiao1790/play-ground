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
using PlayGround.System.Combat.Projectiles;
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
        public void CompilerPopulatesAoeIntervalSetupWhenProjectileSourceTargetsAoeSkill()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
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
        }

        [Test]
        public void CompilerLeavesIntervalChildBurstToTheChildSet()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            var targetDefinition = (ProjectileDefinition)targetSkill.Definition;
            targetDefinition.count = 3;
            targetDefinition.spreadDegrees = 45f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup.Behavior.Count, Is.EqualTo(3));
            Assert.That(setup.Behavior.SpreadDegrees, Is.EqualTo(45f));
            Assert.That(setup.Behavior.PatternType, Is.EqualTo(ProjectileChildSpawnPatternType.SideSpray));
        }

        [Test]
        public void CompilerUsesRadialChildPatternForStationaryProjectileSource()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Stationary Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            ((ProjectileDefinition)sourceSkill.Definition).speed = 0f;
            SkillSet sourceSet = CreateSkillSet("Stationary Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup.Behavior.PatternType, Is.EqualTo(ProjectileChildSpawnPatternType.Radial));
        }

        [Test]
        public void CompilerAppliesManaCostAddedAlongsideOwnStat()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ((ProjectileDefinition)skill.Definition).manaCost = 10f;
            PiercingSupport support = CreateAsset<PiercingSupport>("Piercing");
            SetField(support, "pierceCount", 2);
            SetField(support, "manaCostAdded", 3f);
            SkillSet set = CreateSkillSet("Projectile Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectile.ManaCost, Is.EqualTo(13f).Within(0.0001f));
            Assert.That(projectile.PierceCount, Is.EqualTo(2));
        }

        [Test]
        public void CompilerAppliesManaCostIncreasedAlongsideOwnStat()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            var definition = (AoeDefinitionBase)skill.Definition;
            definition.manaCost = 10f;
            definition.baseAreaSize = 4f;
            IncreasedAoeSupport support = CreateAsset<IncreasedAoeSupport>("Increased AOE");
            SetField(support, "areaSizeMultiplier", 1.5f);
            SetField(support, "manaCostIncreased", 1.5f);
            SkillSet set = CreateSkillSet("AOE Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.ManaCost, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(aoe.AreaSize, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void CompilerAppliesManaCostMultiplierAlongsideOwnStat()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            var definition = (AoeDefinitionBase)skill.Definition;
            definition.manaCost = 10f;
            definition.baseAreaSize = 4f;
            ConcentratedEffectSupport support = CreateAsset<ConcentratedEffectSupport>("Concentrated Effect");
            SetField(support, "areaSizeMultiplier", 0.75f);
            SetField(support, "manaCostMultiplier", 1.5f);
            SkillSet set = CreateSkillSet("AOE Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var aoe = (RuntimeAoeDefinition)runtime;
            Assert.That(aoe.ManaCost, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(aoe.AreaSize, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void CompilerAppliesFullManaCostModifierSetFromHomingSupport()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ((ProjectileDefinition)skill.Definition).manaCost = 10f;
            HomingSupport support = CreateAsset<HomingSupport>("Homing");
            SetField(support, "manaCostAdded", 3f);
            SetField(support, "manaCostIncreased", 1.5f);
            SetField(support, "manaCostMultiplier", 2f);
            SkillSet set = CreateSkillSet("Projectile Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(((RuntimeProjectileDefinition)runtime).ManaCost, Is.EqualTo(39f).Within(0.0001f));
        }

        [Test]
        public void CompilerCombinesManaCostFoldOrderAcrossSupports()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            ((AoeDefinitionBase)skill.Definition).manaCost = 10f;
            MultipleProjectilesSupport added = CreateAsset<MultipleProjectilesSupport>("Multiple Projectiles");
            SetField(added, "manaCostAdded", 3f);
            IncreasedAoeSupport increased = CreateAsset<IncreasedAoeSupport>("Increased AOE");
            SetField(increased, "manaCostIncreased", 1.5f);
            ConcentratedEffectSupport multiplier = CreateAsset<ConcentratedEffectSupport>("Concentrated Effect");
            SetField(multiplier, "manaCostMultiplier", 1.5f);
            SkillSet set = CreateSkillSet("AOE Set", skill, added, increased, multiplier);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(((RuntimeAoeDefinition)runtime).ManaCost, Is.EqualTo(29.25f).Within(0.0001f));
        }

        [Test]
        public void CompilerLeavesManaCostUnchangedForSupportsWithoutManaModifiers()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            ((AoeDefinitionBase)skill.Definition).manaCost = 10f;
            AddedDamageSupport support = CreateAsset<AddedDamageSupport>("Added Damage");
            SkillSet set = CreateSkillSet("AOE Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(((RuntimeAoeDefinition)runtime).ManaCost, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void CompilerMapsProjectileEnergyRateAndCost()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            trigger.energyPerSecond = 2f;
            trigger.initialEnergyPercent = 25f;
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
            Assert.That(setup.InitialEnergyPercent, Is.EqualTo(25f).Within(0.0001f));
            Assert.That(setup.EnergyThreshold, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void TimedSpawnInitialEnergyRollUsesDeterministicSymmetricPercentRange()
        {
            var timedSpawn = new TimedSpawnComponent
            {
                JitterSeed = 17,
                EnergyThreshold = 10f,
                InitialEnergyPercent = 25f
            };

            bool hasNegativeRoll = false;
            for (int sourceId = 1; sourceId <= 32; sourceId++)
            {
                timedSpawn.SourceId = sourceId;
                float roll = TimedSpawnInitialEnergy.Roll(timedSpawn);
                Assert.That(roll, Is.InRange(-2.5f, 2.5f));
                hasNegativeRoll |= roll < 0f;
            }

            timedSpawn.SourceId = 42;
            float first = TimedSpawnInitialEnergy.Roll(timedSpawn);
            float second = TimedSpawnInitialEnergy.Roll(timedSpawn);
            timedSpawn.JitterSeed = 99;
            float differentTemplateSeed = TimedSpawnInitialEnergy.Roll(timedSpawn);
            timedSpawn.SourceId = 43;
            float nextEntity = TimedSpawnInitialEnergy.Roll(timedSpawn);

            Assert.That(hasNegativeRoll, Is.True);
            Assert.That(second, Is.EqualTo(first).Within(0.0001f));
            Assert.That(differentTemplateSeed, Is.EqualTo(first).Within(0.0001f));
            Assert.That(nextEntity, Is.Not.EqualTo(first));
        }

        [Test]
        public void CompilerMapsProjectileEnergyRateWithStatModifiers()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            trigger.energyPerSecond = 2f;
            var snapshot = new SkillStatSnapshot(
                increasedRatePercent: 0f,
                damageMultiplier: 1f,
                critChance: 0f,
                critMultiplier: 1.5f,
                baseEnergyGain: 1f,
                increasedEnergyGain: 0.5f,
                energyGainMultiplier: 2f);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                snapshot);

            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.EnergyPerSecond, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void CompilerMapsAoeEnergyRateAndCost()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            trigger.energyPerSecond = 1.5f;
            ((AoeDefinitionBase)targetSkill.Definition).manaCost = 3f;
            ((AoeDefinitionBase)targetSkill.Definition).echoCount = 3;
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
            Assert.That(projectile.AoeIntervalSpawnSetup.ChildDefinition.EchoCount, Is.EqualTo(3));
        }

        [Test]
        public void CompilerMapsAoeEnergyRateWithStatModifiers()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            trigger.energyPerSecond = 1.5f;
            var snapshot = new SkillStatSnapshot(
                increasedRatePercent: 0f,
                damageMultiplier: 1f,
                critChance: 0f,
                critMultiplier: 1.5f,
                baseEnergyGain: 0.5f,
                increasedEnergyGain: 0.5f,
                energyGainMultiplier: 2f);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                snapshot);

            RuntimeAoeIntervalSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).AoeIntervalSpawnSetup;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.EnergyPerSecond, Is.EqualTo(2f).Within(0.0001f));
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
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");

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
        public void CompilerAppliesIntervalManaCostMultiplier()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            ((ProjectileDefinition)targetSkill.Definition).manaCost = 4f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            trigger.manaCostMultiplier = 2f;

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
        public void CompilerAppliesIntervalManaCostIncreased()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            ((ProjectileDefinition)targetSkill.Definition).manaCost = 4f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            trigger.manaCostMultiplier = 2f;
            trigger.manaCostIncreased = 1.5f;

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup.EnergyThreshold, Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void CompilerLeavesAoeIntervalScatterRadiusToTheChildSet()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            ((AoeDefinitionBase)targetSkill.Definition).scatterRadius = 9f;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            RuntimeAoeIntervalSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).AoeIntervalSpawnSetup;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.ChildDefinition.ScatterRadius, Is.EqualTo(9f).Within(0.0001f));
        }

        [Test]
        public void CompilerPopulatesProjectileIntervalSetupOnLingeringAoeSource()
        {
            LingeringAoeSkill sourceSkill = CreateAsset<LingeringAoeSkill>("Lingering AOE Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Lingering AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
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
            Assert.That(aoe.ChildSpawnSetup.Behavior.PatternType, Is.EqualTo(ProjectileChildSpawnPatternType.Radial));
            Assert.That(aoe.AoeIntervalSpawnSetup, Is.Null);
        }

        [Test]
        public void CompilerPopulatesAoeIntervalSetupOnLingeringAoeSource()
        {
            LingeringAoeSkill sourceSkill = CreateAsset<LingeringAoeSkill>("Lingering AOE Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("AOE Skill");
            SkillSet sourceSet = CreateSkillSet("Lingering AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("AOE Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
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
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
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
        public void ValidatorErrorsWhenIntervalSpawnSourceIsPulseAoe()
        {
            AoeSkill sourceSkill = CreateAsset<AoeSkill>("Pulse AOE Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Pulse AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            SkillValidationWarning matchedWarning = global::System.Array.Find(warnings, candidate =>
                candidate.Code == SkillValidationWarningCode.UnsupportedTriggerSource
                && candidate.SlotIndex == 0);
            Assert.That(matchedWarning.Message, Does.Contain("projectile or lingering AOE"));
            Assert.That(matchedWarning.Severity, Is.EqualTo(SkillValidationSeverity.Error));
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
        public void CompilerBuildsHitEnergyTriggerRuntimeComposition()
        {
            AoeSkill applicatorSkill = CreateAsset<AoeSkill>("Applicator AOE Skill");
            AoeSkill triggeredSkill = CreateAsset<AoeSkill>("Triggered AOE Skill");
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");
            SetField(trigger, "energyContributionMultiplier", 0.5f);
            SetField(trigger, "energyRequirementMultiplier", 1.5f);
            SetField(trigger, "retentionSeconds", 6f);
            SkillSet applicatorSet = CreateSkillSet("Applicator AOE Set", applicatorSkill);
            SkillSet triggeredSet = CreateSkillSet("Triggered AOE Set", triggeredSkill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(applicatorSet, trigger),
                    new SkillLoadoutNode(triggeredSet),
                },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            RuntimeHitEnergyTrigger hitEnergy = ((RuntimeAoeDefinition)runtime).HitEnergyTrigger;
            Assert.That(hitEnergy, Is.Not.Null);
            Assert.That(hitEnergy.TriggeredSkill, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(hitEnergy.TriggeredSkill, Is.Not.SameAs(runtime));
            Assert.That(hitEnergy.EnergyContributionMultiplier, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(hitEnergy.EnergyRequirementMultiplier, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(hitEnergy.RetentionSeconds, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(typeof(RuntimeSkillDefinition).IsAssignableFrom(typeof(RuntimeHitEnergyTrigger)), Is.False);
        }

        [Test]
        public void CompilerAppliesAdditiveSupportsToHitEnergyTriggeredSkill()
        {
            AoeSkill applicatorSkill = CreateAsset<AoeSkill>("Applicator AOE Skill");
            AoeSkill triggeredSkill = CreateAsset<AoeSkill>("Triggered AOE Skill");
            AddedDamageSupport damageSupport = CreateAsset<AddedDamageSupport>("Added Damage");
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");
            SkillSet applicatorSet = CreateSkillSet("Applicator AOE Set", applicatorSkill);
            SkillSet triggeredSet = CreateSkillSet("Triggered AOE Set", triggeredSkill, damageSupport);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(applicatorSet, trigger),
                    new SkillLoadoutNode(triggeredSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeHitEnergyTrigger hitEnergy = ((RuntimeAoeDefinition)runtime).HitEnergyTrigger;
            Assert.That(((RuntimeAoeDefinition)hitEnergy.TriggeredSkill).Damage, Is.EqualTo(15f).Within(0.0001f));
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
        public void RegistrationAssignsStableDistinctHitEnergyAccumulatorIds()
        {
            var first = new RuntimeHitEnergyTrigger();
            var second = new RuntimeHitEnergyTrigger();

            AssignHitEnergyAccumulatorId(first);
            AssignHitEnergyAccumulatorId(second);
            int firstId = first.AccumulatorId;

            AssignHitEnergyAccumulatorId(first);

            Assert.That(firstId, Is.GreaterThan(0));
            Assert.That(second.AccumulatorId, Is.GreaterThan(0));
            Assert.That(second.AccumulatorId, Is.Not.EqualTo(firstId));
            Assert.That(first.AccumulatorId, Is.EqualTo(firstId));
        }

        [Test]
        public void CompilerAttachesHitEnergyTriggerToProjectileApplicator()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Hit Energy Output Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Hit Energy Output Set", targetSkill);
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");
            var nodes = new[]
            {
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectile.HitEnergyTrigger, Is.Not.Null);
            Assert.That(projectile.HitEnergyTrigger.TriggeredSkill, Is.TypeOf<RuntimeAoeDefinition>());
        }

        [Test]
        public void CompilerAttachesHitEnergyTriggerAfterProjectileIntervalSpawnLink()
        {
            ProjectileSkill rootSkill = CreateAsset<ProjectileSkill>("Root Projectile Skill");
            ProjectileSkill applicatorSkill = CreateAsset<ProjectileSkill>("Applicator Projectile Skill");
            AoeSkill triggeredSkill = CreateAsset<AoeSkill>("Hit Energy Output Skill");
            SkillSet rootSet = CreateSkillSet("Root Projectile Set", rootSkill);
            SkillSet applicatorSet = CreateSkillSet("Applicator Projectile Set", applicatorSkill);
            SkillSet triggeredSet = CreateSkillSet("Hit Energy Output Set", triggeredSkill);
            IntervalSpawnTrigger childTrigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            HitEnergyTrigger hitEnergyTrigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");
            var nodes = new[]
            {
                new SkillLoadoutNode(rootSet, childTrigger),
                new SkillLoadoutNode(applicatorSet, hitEnergyTrigger),
                new SkillLoadoutNode(triggeredSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var rootProjectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(rootProjectile.ChildSpawnSetup, Is.Not.Null);
            Assert.That(rootProjectile.ChildSpawnSetup.ChildDefinition.HitEnergyTrigger, Is.Not.Null);
        }

        [Test]
        public void CompilerAttachesAdjacentHitEnergyEdgesRecursively()
        {
            LingeringAoeSkill applicatorSkill = CreateAsset<LingeringAoeSkill>("Lingering AOE Applicator");
            LingeringAoeSkill firstTriggeredSkill = CreateAsset<LingeringAoeSkill>("First Triggered Lingering AOE");
            AoeSkill secondTriggeredSkill = CreateAsset<AoeSkill>("Second Triggered AOE");
            SkillSet applicatorSet = CreateSkillSet("Lingering Applicator Set", applicatorSkill);
            SkillSet firstTriggeredSet = CreateSkillSet("First Triggered Lingering Set", firstTriggeredSkill);
            SkillSet secondTriggeredSet = CreateSkillSet("Second Triggered AOE Set", secondTriggeredSkill);
            HitEnergyTrigger firstTrigger = CreateAsset<HitEnergyTrigger>("First Hit Energy Trigger");
            HitEnergyTrigger secondTrigger = CreateAsset<HitEnergyTrigger>("Second Hit Energy Trigger");
            var nodes = new[]
            {
                new SkillLoadoutNode(applicatorSet, firstTrigger),
                new SkillLoadoutNode(firstTriggeredSet, secondTrigger),
                new SkillLoadoutNode(secondTriggeredSet),
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var applicator = (RuntimeAoeDefinition)runtime;
            Assert.That(applicator.HitEnergyTrigger, Is.Not.Null);
            Assert.That(applicator.HitEnergyTrigger.TriggeredSkill, Is.TypeOf<RuntimeAoeDefinition>());
            var firstTriggered = (RuntimeAoeDefinition)applicator.HitEnergyTrigger.TriggeredSkill;
            Assert.That(firstTriggered.HitEnergyTrigger, Is.Not.Null);
            Assert.That(firstTriggered.HitEnergyTrigger.TriggeredSkill, Is.TypeOf<RuntimeAoeDefinition>());
        }

        [Test]
        public void DriverDoesNotBindHitEnergyTriggeredSkillAsRoot()
        {
            AoeSkill applicatorSkill = CreateAsset<AoeSkill>("Applicator AOE Skill");
            AoeSkill triggeredSkill = CreateAsset<AoeSkill>("Triggered AOE Skill");
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");
            SkillSet applicatorSet = CreateSkillSet("Applicator AOE Set", applicatorSkill);
            SkillSet triggeredSet = CreateSkillSet("Triggered AOE Set", triggeredSkill);
            SkillLoadout loadout = CreateLoadout(
                "Loadout",
                new SkillLoadoutNode(applicatorSet, trigger),
                new SkillLoadoutNode(triggeredSet));
            var gameObject = new GameObject("Player Skill Driver Test");
            createdObjects.Add(gameObject);
            SkillDriver driver = gameObject.AddComponent<SkillDriver>();
            SetField(driver, "loadout", loadout);

            CompileAndRegister(driver);

            Assert.That(driver.SlotCount, Is.EqualTo(1));
        }

        [Test]
        public void ClearSkillKeepsItsAdjacentTriggers()
        {
            SkillSet previousSet = CreateAsset<SkillSet>("Previous Set");
            SkillSet clearedSet = CreateAsset<SkillSet>("Cleared Set");
            SkillSet nextSet = CreateAsset<SkillSet>("Next Set");
            IntervalSpawnTrigger previousTrigger = CreateAsset<IntervalSpawnTrigger>("Previous Trigger");
            IntervalSpawnTrigger nextTrigger = CreateAsset<IntervalSpawnTrigger>("Next Trigger");
            SkillLoadout loadout = CreateLoadout(
                "Loadout",
                new SkillLoadoutNode(previousSet, previousTrigger),
                new SkillLoadoutNode(clearedSet, nextTrigger),
                new SkillLoadoutNode(nextSet));
            var gameObject = new GameObject("Clear Skill Driver Test");
            createdObjects.Add(gameObject);
            SkillDriver driver = gameObject.AddComponent<SkillDriver>();

            MethodInfo method = typeof(SkillDriver).GetMethod(
                "TryApplyEdit",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var arguments = new object[]
            {
                loadout,
                new SkillLoadoutEditCommand(0, SkillLoadoutEditKind.ClearSkill, 1),
                null,
            };
            bool applied = (bool)method.Invoke(driver, arguments);

            Assert.That(applied, Is.True);
            Assert.That(arguments[2], Is.Null);
            Assert.That(loadout.Nodes[0].TriggerToNext, Is.SameAs(previousTrigger));
            Assert.That(loadout.Nodes[1].SkillSet, Is.Null);
            Assert.That(loadout.Nodes[1].TriggerToNext, Is.SameAs(nextTrigger));
        }

        [Test]
        public void ValidatorWarnsWhenHitEnergyTriggerTargetsTargetedSkill()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            TargetedSkill targetSkill = CreateAsset<TargetedSkill>("Targeted Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Targeted Set", targetSkill);
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(sourceSet, trigger),
                new SkillLoadoutNode(targetSet));

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedTriggerTarget));
        }

        [Test]
        public void CompilerCopiesLaunchAimPolicyToIntervalProjectileChildOnly()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            SetTriggerLinkField(trigger, "projectileLaunchAimMode", ProjectileLaunchAimMode.NearestHostile);
            SetTriggerLinkField(trigger, "projectileLaunchAimRange", 5f);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(targetSet),
                },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var rootProjectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(rootProjectile.ProjectileLaunchAimMode, Is.EqualTo(ProjectileLaunchAimMode.None),
                "Root/active-skill projectile must never inherit a trigger's launch-aim policy.");

            RuntimeProjectileDefinition childDefinition = rootProjectile.ChildSpawnSetup.ChildDefinition;
            Assert.That(childDefinition.ProjectileLaunchAimMode, Is.EqualTo(ProjectileLaunchAimMode.NearestHostile));
            Assert.That(childDefinition.ProjectileLaunchAimRange, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void CompilerCopiesLaunchAimPolicyToHitEnergyProjectileOutputOnly()
        {
            ProjectileSkill applicatorSkill = CreateAsset<ProjectileSkill>("Applicator Projectile Skill");
            ProjectileSkill triggeredSkill = CreateAsset<ProjectileSkill>("Triggered Projectile Skill");
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");
            SetTriggerLinkField(trigger, "projectileLaunchAimMode", ProjectileLaunchAimMode.NearestHostile);
            SetTriggerLinkField(trigger, "projectileLaunchAimRange", 5f);
            SkillSet applicatorSet = CreateSkillSet("Applicator Projectile Set", applicatorSkill);
            SkillSet triggeredSet = CreateSkillSet("Triggered Projectile Set", triggeredSkill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(applicatorSet, trigger),
                    new SkillLoadoutNode(triggeredSet),
                },
                0,
                SkillStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var applicator = (RuntimeProjectileDefinition)runtime;
            Assert.That(applicator.ProjectileLaunchAimMode, Is.EqualTo(ProjectileLaunchAimMode.None),
                "Root/active-skill projectile must never inherit a trigger's launch-aim policy.");
            Assert.That(applicator.HitEnergyTrigger, Is.Not.Null);
            Assert.That(applicator.HitEnergyTrigger.TriggeredSkill, Is.TypeOf<RuntimeProjectileDefinition>());
            var triggered = (RuntimeProjectileDefinition)applicator.HitEnergyTrigger.TriggeredSkill;
            Assert.That(triggered.ProjectileLaunchAimMode, Is.EqualTo(ProjectileLaunchAimMode.NearestHostile));
            Assert.That(triggered.ProjectileLaunchAimRange, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void CompilerRetainsContinuousCollisionAlongsideLaunchAimWithoutImpliedTracking()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill childSkill = CreateAsset<ProjectileSkill>("Continuous Child Projectile Skill");
            ((ProjectileDefinition)childSkill.Definition).continuousCollision = true;
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet childSet = CreateSkillSet("Continuous Child Projectile Set", childSkill);
            IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
            SetTriggerLinkField(trigger, "projectileLaunchAimMode", ProjectileLaunchAimMode.NearestHostile);
            SetTriggerLinkField(trigger, "projectileLaunchAimRange", 5f);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(sourceSet, trigger),
                    new SkillLoadoutNode(childSet),
                },
                0,
                SkillStatSnapshot.Identity);

            RuntimeProjectileDefinition childDefinition =
                ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup.ChildDefinition;
            Assert.That(childDefinition.ContinuousCollision, Is.True);
            Assert.That(childDefinition.ProjectileLaunchAimMode, Is.EqualTo(ProjectileLaunchAimMode.NearestHostile));
            Assert.That(childDefinition.ProjectileLaunchAimRange, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(childDefinition.Tracking.Enabled, Is.False,
                "Launch aim must not implicitly enable homing/tracking.");
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

        // TriggerLink.GetType().GetField(...) cannot find projectileLaunchAimMode/Range: they
        // are private fields declared on the abstract TriggerLink base, and reflection's
        // instance+non-public lookup does not surface a base type's private fields through a
        // derived instance's runtime type (verified: only typeof(TriggerLink).GetField finds
        // them, not subclass.GetType().GetField). Query the declaring base type directly instead
        // of widening the shared SetField helper used by every other test in this file.
        private static void SetTriggerLinkField(TriggerLink target, string fieldName, object value)
        {
            FieldInfo field = typeof(TriggerLink).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void AssignHitEnergyAccumulatorId(RuntimeHitEnergyTrigger definition)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "EnsureHitEnergyAccumulatorId",
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

    }
}
