using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
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
        public void BuildStackChainFlattensAoeStackTreeInOrder()
        {
            var impact = new RuntimeAoeDefinition
            {
                TypeId = 30,
                Damage = 7f,
                LifetimeSeconds = 0f,
                TickIntervalSeconds = 0f,
            };
            var lingerB = new RuntimeAoeDefinition
            {
                TypeId = 20,
                Damage = 5f,
                LifetimeSeconds = 2f,
                TickIntervalSeconds = 0.25f,
                StackTriggerSetup = new RuntimeStackTriggerSetup
                {
                    DebuffStatusId = 202,
                    StacksPerHit = 3,
                    StackThreshold = 4,
                    AoeDefinition = impact,
                },
            };
            var lingerA = new RuntimeAoeDefinition
            {
                TypeId = 10,
                StackTriggerSetup = new RuntimeStackTriggerSetup
                {
                    DebuffStatusId = 101,
                    StacksPerHit = 1,
                    StackThreshold = 2,
                    AoeDefinition = lingerB,
                },
            };

            StackChainSnapshot chain = SkillSpawnTranslator.BuildStackChain(
                lingerA,
                CombatFaction.Player,
                0x44);

            Assert.That(chain.Enabled, Is.True);
            Assert.That(chain.Length, Is.EqualTo(2));
            Assert.That(chain.Faction, Is.EqualTo(CombatFaction.Player));
            Assert.That(chain.TargetMask, Is.EqualTo(0x44));
            Assert.That(chain.Stages[0].DebuffStatusId, Is.EqualTo(101));
            Assert.That(chain.Stages[0].AoeTypeId, Is.EqualTo(20));
            Assert.That(chain.Stages[0].AoeDamage, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(chain.Stages[1].DebuffStatusId, Is.EqualTo(202));
            Assert.That(chain.Stages[1].AoeTypeId, Is.EqualTo(30));
            Assert.That(chain.Stages[1].AoeDamage, Is.EqualTo(7f).Within(0.0001f));
        }

        [Test]
        public void ValidatorWarnsWhenStackChainExceedsMaxDepth()
        {
            int depth = CollisionConstants.MaxStackDepth + 1;
            var slots = new LoadoutSlot[depth * 2 + 1];
            for (int i = 0; i < slots.Length; i += 2)
            {
                AoeSkill skill = CreateAsset<AoeSkill>($"Aoe Skill {i}");
                slots[i] = new SkillSetSlot { skillSet = CreateSkillSet($"Aoe Set {i}", skill) };
            }

            for (int i = 1; i < slots.Length; i += 2)
            {
                OnStackTrigger trigger = CreateAsset<OnStackTrigger>($"Stack Trigger {i}");
                slots[i] = new TriggerLinkSlot { link = trigger };
            }

            SkillValidationWarning[] warnings = Validate(slots);

            Assert.That(warnings, Has.Length.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo(SkillValidationWarningCode.StackChainTooDeep));
            Assert.That(warnings[0].Message, Does.Contain($"max supported depth is {CollisionConstants.MaxStackDepth}"));
        }

        private SkillValidationWarning[] Validate(params LoadoutSlot[] slots)
        {
            var list = new List<LoadoutSlot>(slots);
            var warnings = new List<SkillValidationWarning>();
            SkillLoadoutValidator.Validate(list, warnings);
            return warnings.ToArray();
        }

        private SkillSet CreateSkillSet(string name, Skill skill, params AdditiveSupport[] supports)
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
    }
}
