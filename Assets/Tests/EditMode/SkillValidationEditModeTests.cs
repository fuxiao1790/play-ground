using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Mob;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Common;
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

            SkillValidationWarning[] warnings = Validate(new SkillSetSlot { skillSet = set });

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedSupportForSkill));
            Assert.That(warnings[0].Message, Does.Contain("will be ignored"));
        }

        [Test]
        public void ValidatorWarnsWhenChildSpawnTargetsAoeSkill()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
            ChildSpawnTrigger trigger = CreateAsset<ChildSpawnTrigger>("Child Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet });

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedTriggerTarget));
            Assert.That(warnings[0].Message, Does.Contain("will do nothing"));
        }

        [Test]
        public void CompilerIgnoresChildSpawnTargetAoeSkill()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
            ChildSpawnTrigger trigger = CreateAsset<ChildSpawnTrigger>("Child Spawn");
            var chains = new[]
            {
                new TriggerChain
                {
                    causeIndex = 0,
                    link = trigger,
                    effectIndex = 2,
                },
            };
            var slots = new LoadoutSlot[]
            {
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet },
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(slots, 0, chains, PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(((RuntimeProjectileDefinition)runtime).ChildSpawnSetup, Is.Null);
        }

        [Test]
        public void CompilerConvertsChildSpawnJitterPercentToSeconds()
        {
            ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
            ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Child Projectile Skill");
            SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Child Projectile Set", targetSkill);
            ChildSpawnTrigger trigger = CreateAsset<ChildSpawnTrigger>("Child Spawn");
            trigger.intervalSeconds = 2f;
            trigger.intervalJitterPercent = 25f;
            var chains = new[]
            {
                new TriggerChain
                {
                    causeIndex = 0,
                    link = trigger,
                    effectIndex = 2,
                },
            };
            var slots = new LoadoutSlot[]
            {
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet },
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(slots, 0, chains, PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            RuntimeChildSpawnSetup setup = ((RuntimeProjectileDefinition)runtime).ChildSpawnSetup;
            Assert.That(setup, Is.Not.Null);
            Assert.That(setup.IntervalSeconds, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(setup.IntervalJitterSeconds, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void CompilerTreatsRegularAoeAsPulseAoe()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("Regular AOE Skill");
            SkillSet set = CreateSkillSet("Regular AOE Set", skill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

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
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

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
            SetField(stackingSupport, "debuffName", "Volatile Charge");
            SetField(stackingSupport, "cosmeticDebuffStatus", DebuffStatus.Shock);
            SkillSet set = CreateSkillSet("Stacking Support Set", skill, stackingSupport);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeStackingDetonation>());
            var stacking = (RuntimeStackingDetonation)runtime;
            Assert.That(stacking.Detonation, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(stacking.StackThreshold, Is.EqualTo(4));
            Assert.That(stacking.DebuffLifetimeSeconds, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(stacking.StacksPerHit, Is.EqualTo(2));
            Assert.That(stacking.DebuffName, Is.EqualTo("Volatile Charge"));
            Assert.That(stacking.CosmeticDebuffStatus, Is.EqualTo(DebuffStatus.Shock));
            Assert.That(stacking.DebuffKey, Is.EqualTo(-1));
        }

        [Test]
        public void CompilerAppliesAdditiveSupportsToStackingSupportDetonation()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            AddedDamageSupport damageSupport = CreateAsset<AddedDamageSupport>("Added Damage");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet set = CreateSkillSet("Stacking Support Set", skill, damageSupport, stackingSupport);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

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
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

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
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet });

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
            var chains = new[]
            {
                new TriggerChain
                {
                    causeIndex = 0,
                    link = trigger,
                    effectIndex = 2,
                },
            };
            var slots = new LoadoutSlot[]
            {
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet },
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(slots, 0, chains, PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
            var projectile = (RuntimeProjectileDefinition)runtime;
            Assert.That(projectile.StackingDetonation, Is.Not.Null);
            Assert.That(projectile.StackingDetonation.Detonation, Is.TypeOf<RuntimeAoeDefinition>());
        }

        [Test]
        public void CompilerAttachesStackTriggerAfterNormalChildSpawnLink()
        {
            ProjectileSkill rootSkill = CreateAsset<ProjectileSkill>("Root Projectile Skill");
            ProjectileSkill applicatorSkill = CreateAsset<ProjectileSkill>("Applicator Projectile Skill");
            AoeSkill detonationSkill = CreateAsset<AoeSkill>("Stack Detonation Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet rootSet = CreateSkillSet("Root Projectile Set", rootSkill);
            SkillSet applicatorSet = CreateSkillSet("Applicator Projectile Set", applicatorSkill);
            SkillSet detonationSet = CreateSkillSet("Stack Detonation Set", detonationSkill, stackingSupport);
            ChildSpawnTrigger childTrigger = CreateAsset<ChildSpawnTrigger>("Child Spawn");
            StackTrigger stackTrigger = CreateAsset<StackTrigger>("Stack Trigger");
            var chains = new[]
            {
                new TriggerChain
                {
                    causeIndex = 0,
                    link = childTrigger,
                    effectIndex = 2,
                },
                new TriggerChain
                {
                    causeIndex = 2,
                    link = stackTrigger,
                    effectIndex = 4,
                },
            };
            var slots = new LoadoutSlot[]
            {
                new SkillSetSlot { skillSet = rootSet },
                new TriggerLinkSlot { link = childTrigger },
                new SkillSetSlot { skillSet = applicatorSet },
                new TriggerLinkSlot { link = stackTrigger },
                new SkillSetSlot { skillSet = detonationSet },
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(slots, 0, chains, PlayerStatSnapshot.Identity);

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
            var chains = new[]
            {
                new TriggerChain
                {
                    causeIndex = 0,
                    link = impactAoeTrigger,
                    effectIndex = 2,
                },
                new TriggerChain
                {
                    causeIndex = 2,
                    link = firstStackTrigger,
                    effectIndex = 4,
                },
                new TriggerChain
                {
                    causeIndex = 5,
                    link = impactProjectileTrigger,
                    effectIndex = 7,
                },
                new TriggerChain
                {
                    causeIndex = 7,
                    link = secondStackTrigger,
                    effectIndex = 9,
                },
            };
            var slots = new LoadoutSlot[]
            {
                new SkillSetSlot { skillSet = rootSet },
                new TriggerLinkSlot { link = impactAoeTrigger },
                new SkillSetSlot { skillSet = aoeApplicatorSet },
                new TriggerLinkSlot { link = firstStackTrigger },
                new SkillSetSlot { skillSet = firstDetonationSet },
                new SkillSetSlot { skillSet = rootSet },
                new TriggerLinkSlot { link = impactProjectileTrigger },
                new SkillSetSlot { skillSet = projectileApplicatorSet },
                new TriggerLinkSlot { link = secondStackTrigger },
                new SkillSetSlot { skillSet = secondDetonationSet },
            };

            RuntimeSkillDefinition impactAoeRuntime = SkillSetCompiler.Compile(slots, 0, chains, PlayerStatSnapshot.Identity);
            RuntimeSkillDefinition impactProjectileRuntime = SkillSetCompiler.Compile(slots, 5, chains, PlayerStatSnapshot.Identity);

            Assert.That(impactAoeRuntime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(((RuntimeProjectileDefinition)impactAoeRuntime).ImpactAoeDefinition, Is.Not.Null);
            Assert.That(((RuntimeProjectileDefinition)impactAoeRuntime).ImpactAoeDefinition.StackingDetonation, Is.Not.Null);
            Assert.That(impactProjectileRuntime, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(((RuntimeProjectileDefinition)impactProjectileRuntime).ImpactProjectileDefinition, Is.Not.Null);
            Assert.That(((RuntimeProjectileDefinition)impactProjectileRuntime).ImpactProjectileDefinition.StackingDetonation, Is.Not.Null);
        }

        [Test]
        public void DriverDoesNotBindStackingSupportSetAsRoot()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("AOE Skill");
            StackingSupport stackingSupport = CreateAsset<StackingSupport>("Stacking Support");
            SkillSet set = CreateSkillSet("Stacking Set", skill, stackingSupport);
            PlayerLoadout loadout = CreateLoadout("Loadout", new SkillSetSlot { skillSet = set });
            var gameObject = new GameObject("Player Skill Driver Test");
            createdObjects.Add(gameObject);
            PlayerSkillDriver driver = gameObject.AddComponent<PlayerSkillDriver>();
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

            SkillValidationWarning[] warnings = Validate(new SkillSetSlot { skillSet = set });

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
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet });

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
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet });

            Assert.That(HasWarning(warnings, SkillValidationWarningCode.UnsupportedStackingDetonation, 1, "is not a StackTrigger"), Is.True);
        }

        private SkillValidationWarning[] Validate(params LoadoutSlot[] slots)
        {
            var list = new List<LoadoutSlot>(slots);
            var warnings = new List<SkillValidationWarning>();
            SkillLoadoutValidator.Validate(list, warnings);
            return warnings.ToArray();
        }

        private PlayerLoadout CreateLoadout(string name, params LoadoutSlot[] slots)
        {
            PlayerLoadout loadout = CreateAsset<PlayerLoadout>(name);
            SetField(loadout, "slots", new List<LoadoutSlot>(slots));
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
            MethodInfo method = typeof(PlayerSkillDriver).GetMethod(
                "EnsureStackingDetonationDebuffKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { definition });
        }

        private static void CompileAndRegister(PlayerSkillDriver driver)
        {
            MethodInfo method = typeof(PlayerSkillDriver).GetMethod(
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
                PlayerStatSnapshot snapshot)
            {
                WasCompiled = true;
                DefinitionSeen = definition;
                RuntimeSeen = runtime;
                return runtime;
            }
        }
    }
}
