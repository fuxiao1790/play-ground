using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Modifiers;
using PlayGround.Skills.Runtime;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class ModifierFoldEditModeTests
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
        public void IncreasedAndMultiplierAreaSupportsAreOrderIndependent()
        {
            AoeSkill firstSkill = CreateAoeSkill("First AOE Skill", areaSize: 12f);
            IncreasedAoeSupport increased = CreateAsset<IncreasedAoeSupport>("Increased AOE");
            SetField(increased, "areaSizeMultiplier", 1.5f);
            ConcentratedEffectSupport multiplier = CreateAsset<ConcentratedEffectSupport>("Concentrated Effect");
            SetField(multiplier, "areaSizeMultiplier", 0.5f);
            SkillSet firstSet = CreateSkillSet("First Set", firstSkill, increased, multiplier);

            AoeSkill secondSkill = CreateAoeSkill("Second AOE Skill", areaSize: 12f);
            SkillSet secondSet = CreateSkillSet("Second Set", secondSkill, multiplier, increased);

            var first = (RuntimeAoeDefinition)Compile(firstSet);
            var second = (RuntimeAoeDefinition)Compile(secondSet);

            Assert.That(first.AreaSize, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(second.AreaSize, Is.EqualTo(first.AreaSize).Within(0.0001f));
        }

        [Test]
        public void MultiplierSupportsTakeProduct()
        {
            AoeSkill skill = CreateAoeSkill("AOE Skill", areaSize: 4f);
            ConcentratedEffectSupport first = CreateAsset<ConcentratedEffectSupport>("First Multiplier");
            SetField(first, "areaSizeMultiplier", 1.5f);
            ConcentratedEffectSupport second = CreateAsset<ConcentratedEffectSupport>("Second Multiplier");
            SetField(second, "areaSizeMultiplier", 2f);
            SkillSet set = CreateSkillSet("Set", skill, first, second);

            var runtime = (RuntimeAoeDefinition)Compile(set);

            Assert.That(runtime.AreaSize, Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void AddedIncreasedAndMultiplierComposePerFormula()
        {
            AoeSkill skill = CreateAoeSkill("AOE Skill", damage: 10f);
            AddedDamageSupport added = CreateAsset<AddedDamageSupport>("Added Damage");
            SetField(added, "addedDamage", 5f);
            TestIncreasedSupport increased = CreateIncreasedSupport("Increased Damage", SkillStat.Damage, 1f);
            TestMultiplierSupport multiplier = CreateMultiplierSupport("More Damage", SkillStat.Damage, 2f, MultiplierTiming.Post);
            SkillSet set = CreateSkillSet("Set", skill, added, increased, multiplier);

            var runtime = (RuntimeAoeDefinition)Compile(set);

            Assert.That(runtime.Damage, Is.EqualTo(60f).Within(0.0001f));
        }

        [Test]
        public void PreAndPostMultipliersDifferOnlyWithFlatAdds()
        {
            ProjectileSkill preSkill = CreateProjectileSkill("Pre Projectile Skill", damage: 5f);
            TestAddedSupport preAdded = CreateAddedSupport("Pre Added Damage", SkillStat.Damage, 10f);
            TestMultiplierSupport preMultiplier = CreateMultiplierSupport("Pre More Damage", SkillStat.Damage, 2f, MultiplierTiming.Pre);
            SkillSet preSet = CreateSkillSet("Pre Set", preSkill, preAdded, preMultiplier);

            ProjectileSkill postSkill = CreateProjectileSkill("Post Projectile Skill", damage: 5f);
            TestAddedSupport postAdded = CreateAddedSupport("Post Added Damage", SkillStat.Damage, 10f);
            TestMultiplierSupport postMultiplier = CreateMultiplierSupport("Post More Damage", SkillStat.Damage, 2f, MultiplierTiming.Post);
            SkillSet postSet = CreateSkillSet("Post Set", postSkill, postAdded, postMultiplier);

            var preRuntime = (RuntimeProjectileDefinition)Compile(preSet);
            var postRuntime = (RuntimeProjectileDefinition)Compile(postSet);

            Assert.That(preRuntime.Damage, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(postRuntime.Damage, Is.EqualTo(30f).Within(0.0001f));
        }

        [Test]
        public void RecoverySpeedFoldCombinesWithCastSpeedTimeMultiplier()
        {
            ProjectileSkill skill = CreateProjectileSkill("Projectile Skill");
            SetField(skill, "baseRecoveryTime", 0.5f);
            IncreasedRecoverySpeedSupport support = CreateAsset<IncreasedRecoverySpeedSupport>("Increased Recovery Speed");
            SetField(support, "recoverySpeedMultiplier", 2f);
            SkillSet set = CreateSkillSet("Set", skill, support);
            var snapshot = new PlayerStatSnapshot(
                castSpeedMultiplier: 0.5f,
                damageMultiplier: 1f,
                critChance: 0f,
                critMultiplier: 1.5f);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                Slots(set),
                0,
                global::System.Array.Empty<TriggerChain>(),
                snapshot);

            Assert.That(runtime.RecoveryTime, Is.EqualTo(0.125f).Within(0.0001f));
        }

        [Test]
        public void PierceCountAddsAcrossSupports()
        {
            ProjectileSkill skill = CreateProjectileSkill("Projectile Skill", pierceCount: 1);
            PiercingSupport first = CreateAsset<PiercingSupport>("First Pierce");
            SetField(first, "pierceCount", 2);
            PiercingSupport second = CreateAsset<PiercingSupport>("Second Pierce");
            SetField(second, "pierceCount", 3);
            SkillSet set = CreateSkillSet("Set", skill, first, second);

            var runtime = (RuntimeProjectileDefinition)Compile(set);

            Assert.That(runtime.PierceCount, Is.EqualTo(6));
        }

        [Test]
        public void PiercingSupportStillAppliesRepeatHitCooldownBehavior()
        {
            ProjectileSkill skill = CreateProjectileSkill("Projectile Skill");
            PiercingSupport support = CreateAsset<PiercingSupport>("Pierce");
            SetField(support, "repeatHitCooldown", 0.75f);
            SkillSet set = CreateSkillSet("Set", skill, support);

            var runtime = (RuntimeProjectileDefinition)Compile(set);

            Assert.That(runtime.RepeatHitCooldown, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void ProjectileBehaviorContextDoesNotExposeNumericBaseSetters()
        {
            BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;

            Assert.That(typeof(ProjectileBehaviorContext).GetProperty("Damage", publicInstance), Is.Null);
            Assert.That(typeof(ProjectileBehaviorContext).GetProperty("Speed", publicInstance), Is.Null);
            Assert.That(typeof(ProjectileBehaviorContext).GetProperty("Lifetime", publicInstance), Is.Null);
            Assert.That(typeof(ProjectileBehaviorContext).GetProperty("PierceCount", publicInstance), Is.Null);
        }

        private RuntimeSkillDefinition Compile(SkillSet set) =>
            SkillSetCompiler.Compile(
                Slots(set),
                0,
                global::System.Array.Empty<TriggerChain>(),
                PlayerStatSnapshot.Identity);

        private AoeSkill CreateAoeSkill(string name, float areaSize = 1f, float damage = 10f)
        {
            AoeSkill skill = CreateAsset<AoeSkill>(name);
            var definition = (AoeDefinition)skill.Definition;
            definition.baseAreaSize = areaSize;
            definition.damage = damage;
            return skill;
        }

        private ProjectileSkill CreateProjectileSkill(string name, float damage = 10f, int pierceCount = 0)
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>(name);
            var definition = (ProjectileDefinition)skill.Definition;
            definition.damage = damage;
            definition.pierceCount = pierceCount;
            return skill;
        }

        private TestAddedSupport CreateAddedSupport(string name, SkillStat stat, float amount)
        {
            TestAddedSupport support = CreateAsset<TestAddedSupport>(name);
            support.Stat = stat;
            support.Amount = amount;
            return support;
        }

        private TestIncreasedSupport CreateIncreasedSupport(string name, SkillStat stat, float percent)
        {
            TestIncreasedSupport support = CreateAsset<TestIncreasedSupport>(name);
            support.Stat = stat;
            support.Percent = percent;
            return support;
        }

        private TestMultiplierSupport CreateMultiplierSupport(
            string name,
            SkillStat stat,
            float multiplier,
            MultiplierTiming timing)
        {
            TestMultiplierSupport support = CreateAsset<TestMultiplierSupport>(name);
            support.Stat = stat;
            support.Multiplier = multiplier;
            support.Timing = timing;
            return support;
        }

        private SkillSet CreateSkillSet(string name, Skill skill, params SkillSupport[] supports)
        {
            SkillSet set = CreateAsset<SkillSet>(name);
            SetField(set, "skill", skill);
            SetField(set, "supports", supports ?? global::System.Array.Empty<SkillSupport>());
            return set;
        }

        private static LoadoutSlot[] Slots(SkillSet set)
        {
            return new LoadoutSlot[] { new SkillSetSlot { skillSet = set } };
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
            FieldInfo field = null;
            for (global::System.Type type = target.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        private sealed class TestAddedSupport : StatModifierSupport, IBaseValueModifier
        {
            public SkillStat Stat { get; set; }
            public float Amount { get; set; }
            public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

            public void CollectAdded(AddedSink sink)
            {
                sink.Add(Stat, Amount);
            }
        }

        private sealed class TestIncreasedSupport : StatModifierSupport, IIncreasedModifier
        {
            public SkillStat Stat { get; set; }
            public float Percent { get; set; }
            public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

            public void CollectIncreases(IncreasedSink sink)
            {
                sink.Add(Stat, Percent);
            }
        }

        private sealed class TestMultiplierSupport : StatModifierSupport, IMultiplierModifier
        {
            public SkillStat Stat { get; set; }
            public float Multiplier { get; set; } = 1f;
            public MultiplierTiming Timing { get; set; } = MultiplierTiming.Post;
            public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

            public void CollectMultipliers(MultiplierSink sink)
            {
                sink.Add(Stat, Multiplier, Timing);
            }
        }
    }
}
