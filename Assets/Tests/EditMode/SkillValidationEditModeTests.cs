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
        public void CompilerKeepsSameAssetStackSlotsIndependent()
        {
            LingeringAoeSkill repeatedSkill = CreateAsset<LingeringAoeSkill>("Repeated Lingering AOE Skill");
            AoeSkill impactSkill = CreateAsset<AoeSkill>("Impact AOE Skill");
            SkillSet repeatedSet = CreateSkillSet("Repeated AOE Set", repeatedSkill);
            SkillSet impactSet = CreateSkillSet("Impact AOE Set", impactSkill);
            OnStackTrigger firstStack = CreateAsset<OnStackTrigger>("First Stack");
            firstStack.debuffStatus = DebuffStatus.Poison;
            firstStack.stacksPerHit = 1;
            firstStack.stackThreshold = 2;
            OnStackTrigger secondStack = CreateAsset<OnStackTrigger>("Second Stack");
            secondStack.debuffStatus = DebuffStatus.Burning;
            secondStack.stacksPerHit = 3;
            secondStack.stackThreshold = 4;
            var slots = new LoadoutSlot[]
            {
                new SkillSetSlot { skillSet = repeatedSet },
                new TriggerLinkSlot { link = firstStack },
                new SkillSetSlot { skillSet = repeatedSet },
                new TriggerLinkSlot { link = secondStack },
                new SkillSetSlot { skillSet = impactSet },
            };
            var chains = new[]
            {
                new TriggerChain { causeIndex = 0, link = firstStack, effectIndex = 2 },
                new TriggerChain { causeIndex = 2, link = secondStack, effectIndex = 4 },
            };

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(slots, 0, chains, PlayerStatSnapshot.Identity);

            Assert.That(runtime, Is.TypeOf<RuntimeAoeDefinition>());
            var root = (RuntimeAoeDefinition)runtime;
            Assert.That(root.StackTriggerSetup, Is.Not.Null);
            Assert.That(root.StackTriggerSetup.DebuffStatusId, Is.EqualTo((int)DebuffStatus.Poison));
            Assert.That(root.StackTriggerSetup.AoeDefinition, Is.Not.Null);
            Assert.That(root.StackTriggerSetup.AoeDefinition, Is.Not.SameAs(root));

            RuntimeAoeDefinition second = root.StackTriggerSetup.AoeDefinition;
            Assert.That(second.StackTriggerSetup, Is.Not.Null);
            Assert.That(second.StackTriggerSetup.DebuffStatusId, Is.EqualTo((int)DebuffStatus.Burning));
            Assert.That(second.StackTriggerSetup.StacksPerHit, Is.EqualTo(3));
            Assert.That(second.StackTriggerSetup.StackThreshold, Is.EqualTo(4));
            Assert.That(second.StackTriggerSetup.AoeDefinition, Is.Not.Null);
            Assert.That(second.StackTriggerSetup.AoeDefinition.StackTriggerSetup, Is.Null);
        }

        [Test]
        public void BuildStackChainTruncatesPastMaxDepth()
        {
            RuntimeAoeDefinition root = ChainRoot(CollisionConstants.MaxStackDepth + 1);

            StackChainSnapshot chain = SkillSpawnTranslator.BuildStackChain(
                root,
                CombatFaction.Player,
                0x22);

            Assert.That(chain.Length, Is.EqualTo(CollisionConstants.MaxStackDepth));
            Assert.That(chain.Stages[0].AoeTypeId, Is.EqualTo(1));
            Assert.That(
                chain.Stages[CollisionConstants.MaxStackDepth - 1].AoeTypeId,
                Is.EqualTo(CollisionConstants.MaxStackDepth));
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

        private static RuntimeAoeDefinition ChainRoot(int depth)
        {
            var root = new RuntimeAoeDefinition { TypeId = 0 };
            RuntimeAoeDefinition current = root;
            for (int i = 0; i < depth; i++)
            {
                var spawned = new RuntimeAoeDefinition
                {
                    TypeId = i + 1,
                    Damage = i + 1,
                };
                current.StackTriggerSetup = new RuntimeStackTriggerSetup
                {
                    DebuffStatusId = (int)DebuffStatus.Volatile,
                    StacksPerHit = 1,
                    StackThreshold = 2,
                    AoeDefinition = spawned,
                };
                current = spawned;
            }

            return root;
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
    }
}
