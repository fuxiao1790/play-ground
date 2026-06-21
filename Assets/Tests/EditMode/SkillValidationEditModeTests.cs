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
        public void CompilerBuildsStackingSkillRuntime()
        {
            StackingSkill skill = CreateAsset<StackingSkill>("Stacking Skill");
            var definition = (StackingSkillDefinition)skill.Definition;
            definition.applicatorKind = StackingSkillApplicatorKind.LingeringAoe;
            definition.detonationKind = StackingSkillDetonationKind.Aoe;
            definition.stackThreshold = 4;
            definition.debuffLifetimeSeconds = 6f;
            definition.debuffName = "Volatile Charge";
            definition.cosmeticDebuffStatus = DebuffStatus.Shock;
            definition.lingeringAoeApplicator.lifetimeSeconds = 2.5f;
            definition.lingeringAoeApplicator.tickIntervalSeconds = 0.5f;
            SkillSet set = CreateSkillSet("Stacking Set", skill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeStackingSkillDefinition>());
            var stacking = (RuntimeStackingSkillDefinition)runtime;
            Assert.That(stacking.ApplicatorDefinition, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(stacking.DetonationDefinition, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(stacking.ApplicatorKind, Is.EqualTo(RuntimeStackingSkillEffectKind.Aoe));
            Assert.That(stacking.DetonationKind, Is.EqualTo(RuntimeStackingSkillEffectKind.Aoe));
            Assert.That(stacking.StackThreshold, Is.EqualTo(4));
            Assert.That(stacking.DebuffLifetimeSeconds, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(stacking.DebuffName, Is.EqualTo("Volatile Charge"));
            Assert.That(stacking.CosmeticDebuffStatus, Is.EqualTo(DebuffStatus.Shock));
            Assert.That(stacking.DebuffKey, Is.EqualTo(-1));

            var applicator = (RuntimeAoeDefinition)stacking.ApplicatorDefinition;
            Assert.That(applicator.LifetimeSeconds, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(applicator.TickIntervalSeconds, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ValidatorDoesNotWarnForProjectileStackDetonation()
        {
            StackingSkill skill = CreateAsset<StackingSkill>("Stacking Skill");
            var definition = (StackingSkillDefinition)skill.Definition;
            definition.detonationKind = StackingSkillDetonationKind.Projectile;
            SkillSet set = CreateSkillSet("Stacking Set", skill);

            SkillValidationWarning[] warnings = Validate(new SkillSetSlot { skillSet = set });

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void CompilerBuildsProjectileStackDetonationRuntime()
        {
            StackingSkill skill = CreateAsset<StackingSkill>("Stacking Skill");
            var definition = (StackingSkillDefinition)skill.Definition;
            definition.detonationKind = StackingSkillDetonationKind.Projectile;
            SkillSet set = CreateSkillSet("Stacking Set", skill);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeStackingSkillDefinition>());
            var stacking = (RuntimeStackingSkillDefinition)runtime;
            Assert.That(stacking.DetonationDefinition, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(stacking.DetonationKind, Is.EqualTo(RuntimeStackingSkillEffectKind.Projectile));
        }

        [Test]
        public void CompilerAppliesStackingSkillSupportsToApplicatorAndDetonation()
        {
            StackingSkill skill = CreateAsset<StackingSkill>("Stacking Skill");
            var definition = (StackingSkillDefinition)skill.Definition;
            definition.applicatorKind = StackingSkillApplicatorKind.Aoe;
            definition.detonationKind = StackingSkillDetonationKind.Aoe;
            AddedDamageSupport support = CreateAsset<AddedDamageSupport>("Added Damage");
            SkillSet set = CreateSkillSet("Stacking Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                new LoadoutSlot[] { new SkillSetSlot { skillSet = set } },
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

            var stacking = (RuntimeStackingSkillDefinition)runtime;
            Assert.That(((RuntimeAoeDefinition)stacking.ApplicatorDefinition).Damage, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(((RuntimeAoeDefinition)stacking.DetonationDefinition).Damage, Is.EqualTo(15f).Within(0.0001f));
        }

        [Test]
        public void RegistrationAssignsDistinctStackingDebuffKeys()
        {
            var first = new RuntimeStackingSkillDefinition();
            var second = new RuntimeStackingSkillDefinition();

            AssignStackingDebuffKey(first);
            AssignStackingDebuffKey(second);
            int firstKey = first.DebuffKey;

            AssignStackingDebuffKey(first);

            Assert.That(firstKey, Is.GreaterThan(0));
            Assert.That(second.DebuffKey, Is.GreaterThan(0));
            Assert.That(second.DebuffKey, Is.Not.EqualTo(firstKey));
            Assert.That(first.DebuffKey, Is.EqualTo(firstKey));
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
        public void ValidatorDoesNotWarnForAoeHitSpawnToAoeApplicatorStackingSkill()
        {
            AoeSkill sourceSkill = CreateAsset<AoeSkill>("AOE Skill");
            StackingSkill targetSkill = CreateAsset<StackingSkill>("Stacking Skill");
            var targetDefinition = (StackingSkillDefinition)targetSkill.Definition;
            targetDefinition.applicatorKind = StackingSkillApplicatorKind.Aoe;
            targetDefinition.detonationKind = StackingSkillDetonationKind.Aoe;
            SkillSet sourceSet = CreateSkillSet("AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Stacking Set", targetSkill);
            OnAoeHitSpawnTrigger trigger = CreateAsset<OnAoeHitSpawnTrigger>("AOE Hit Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet });

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void ValidatorWarnsWhenAoeHitSpawnTargetsProjectileApplicatorStackingSkill()
        {
            AoeSkill sourceSkill = CreateAsset<AoeSkill>("AOE Skill");
            StackingSkill targetSkill = CreateAsset<StackingSkill>("Stacking Skill");
            var targetDefinition = (StackingSkillDefinition)targetSkill.Definition;
            targetDefinition.applicatorKind = StackingSkillApplicatorKind.Projectile;
            targetDefinition.detonationKind = StackingSkillDetonationKind.Aoe;
            SkillSet sourceSet = CreateSkillSet("AOE Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Stacking Set", targetSkill);
            OnAoeHitSpawnTrigger trigger = CreateAsset<OnAoeHitSpawnTrigger>("AOE Hit Spawn");

            SkillValidationWarning[] warnings = Validate(
                new SkillSetSlot { skillSet = sourceSet },
                new TriggerLinkSlot { link = trigger },
                new SkillSetSlot { skillSet = targetSet });

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.UnsupportedTriggerTarget));
            Assert.That(warnings[0].Message, Does.Contain("will do nothing"));
        }

        [Test]
        public void CompilerAttachesAoeHitSpawnLinkToStackingDetonationAoe()
        {
            StackingSkill sourceSkill = CreateAsset<StackingSkill>("Source Stacking Skill");
            var sourceDefinition = (StackingSkillDefinition)sourceSkill.Definition;
            sourceDefinition.applicatorKind = StackingSkillApplicatorKind.Aoe;
            sourceDefinition.detonationKind = StackingSkillDetonationKind.Aoe;
            StackingSkill targetSkill = CreateAsset<StackingSkill>("Target Stacking Skill");
            var targetDefinition = (StackingSkillDefinition)targetSkill.Definition;
            targetDefinition.applicatorKind = StackingSkillApplicatorKind.Aoe;
            targetDefinition.detonationKind = StackingSkillDetonationKind.Aoe;
            SkillSet sourceSet = CreateSkillSet("Source Stacking Set", sourceSkill);
            SkillSet targetSet = CreateSkillSet("Target Stacking Set", targetSkill);
            OnAoeHitSpawnTrigger trigger = CreateAsset<OnAoeHitSpawnTrigger>("AOE Hit Spawn");
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

            Assert.That(runtime, Is.TypeOf<RuntimeStackingSkillDefinition>());
            var stacking = (RuntimeStackingSkillDefinition)runtime;
            var detonation = (RuntimeAoeDefinition)stacking.DetonationDefinition;
            Assert.That(detonation.OnHitAoeSpawnDefinition, Is.TypeOf<RuntimeStackingSkillDefinition>());
        }

        private SkillValidationWarning[] Validate(params LoadoutSlot[] slots)
        {
            var list = new List<LoadoutSlot>(slots);
            var warnings = new List<SkillValidationWarning>();
            SkillLoadoutValidator.Validate(list, warnings);
            return warnings.ToArray();
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

        private static void AssignStackingDebuffKey(RuntimeStackingSkillDefinition definition)
        {
            MethodInfo method = typeof(PlayerSkillDriver).GetMethod(
                "EnsureStackingDebuffKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { definition });
        }

        private static void AssignStackingDetonationDebuffKey(RuntimeStackingDetonation definition)
        {
            MethodInfo method = typeof(PlayerSkillDriver).GetMethod(
                "EnsureStackingDetonationDebuffKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { definition });
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
