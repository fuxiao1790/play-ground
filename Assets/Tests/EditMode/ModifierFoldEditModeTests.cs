using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common.Modifiers;
using PlayGround.Common.Stats;
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
        public void MultipleAoesSupportAddsEchoAndScatter()
        {
            AoeSkill skill = CreateAoeSkill("AOE Skill");
            MultipleAoesSupport support = CreateAsset<MultipleAoesSupport>("Multiple AOEs");
            SetField(support, "echoCount", 4);
            SetField(support, "scatterRadius", 2.25f);
            SkillSet set = CreateSkillSet("Set", skill, support);

            var runtime = (RuntimeAoeDefinition)Compile(set);

            Assert.That(runtime.EchoCount, Is.EqualTo(5));
            Assert.That(runtime.ScatterRadius, Is.EqualTo(2.25f).Within(0.0001f));
        }

        [Test]
        public void MultipleAoesSupportsStackByAddingBehaviorValues()
        {
            AoeSkill skill = CreateAoeSkill("AOE Skill");
            MultipleAoesSupport first = CreateAsset<MultipleAoesSupport>("First Multiple AOEs");
            SetField(first, "echoCount", 4);
            SetField(first, "scatterRadius", 2.25f);
            MultipleAoesSupport second = CreateAsset<MultipleAoesSupport>("Second Multiple AOEs");
            SetField(second, "echoCount", 6);
            SetField(second, "scatterRadius", 3.5f);
            SkillSet set = CreateSkillSet("Set", skill, first, second);

            var runtime = (RuntimeAoeDefinition)Compile(set);

            Assert.That(runtime.EchoCount, Is.EqualTo(11));
            Assert.That(runtime.ScatterRadius, Is.EqualTo(5.75f).Within(0.0001f));
        }

        [Test]
        public void MultipleProjectilesSupportsStackByAddingBehaviorValues()
        {
            ProjectileSkill skill = CreateProjectileSkill("Projectile Skill");
            MultipleProjectilesSupport first = CreateAsset<MultipleProjectilesSupport>("First Multiple Projectiles");
            SetField(first, "count", 3);
            SetField(first, "spreadDegrees", 20f);
            MultipleProjectilesSupport second = CreateAsset<MultipleProjectilesSupport>("Second Multiple Projectiles");
            SetField(second, "count", 5);
            SetField(second, "spreadDegrees", 35f);
            SkillSet set = CreateSkillSet("Set", skill, first, second);

            var runtime = (RuntimeProjectileDefinition)Compile(set);

            Assert.That(runtime.Count, Is.EqualTo(9));
            Assert.That(runtime.SpreadDegrees, Is.EqualTo(55f).Within(0.0001f));
        }

        [Test]
        public void SharedStatFoldUsesBaseAddedIncreasedAndMultiplier()
        {
            Assert.That(StatFold.Resolve(
                baseValue: 10f,
                addedBase: 5f,
                increased: 2f,
                multiplier: 3f), Is.EqualTo(90f).Within(0.0001f));
        }

        [Test]
        public void AddedIncreasedAndMultiplierComposePerFormula()
        {
            var accumulator = new StatModifierAccumulator();
            accumulator.AddAdded(SkillStat.Damage, 5f);
            accumulator.AddIncreasedFactor(SkillStat.Damage, 2f);
            accumulator.AddMultiplier(SkillStat.Damage, 2f);

            Assert.That(accumulator.Resolve(SkillStat.Damage, 10f), Is.EqualTo(60f).Within(0.0001f));
        }

        [Test]
        public void RateFoldCombinesSupportAndPlayerIncreases()
        {
            ProjectileSkill skill = CreateProjectileSkill("Projectile Skill");
            SetField(skill, "baseRate", 2.5f);
            IncreasedRateSupport support = CreateAsset<IncreasedRateSupport>("Increased Rate");
            SetField(support, "increasedRatePercent", 50f);
            SkillSet set = CreateSkillSet("Set", skill, support);
            var snapshot = new SkillStatSnapshot(
                increasedRatePercent: 0.5f,
                damageMultiplier: 1f,
                critChance: 0f,
                critMultiplier: 1.5f);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                Nodes(set),
                0,
                snapshot);

            Assert.That(runtime.RecoveryTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void UnitStatSheetRateIncreaseUsesAuthoredPercentPoints()
        {
            UnitStatSheet sheet = CreateAsset<UnitStatSheet>("Player Stats");
            SetField(sheet, "increasedRatePercent", 15f);
            SkillStatSnapshot snapshot = SkillStatAggregator.Aggregate(null, sheet);

            ProjectileSkill skill = CreateProjectileSkill("Projectile Skill");
            SetField(skill, "baseRate", 1f);
            IncreasedRateSupport support = CreateAsset<IncreasedRateSupport>("Increased Rate");
            SetField(support, "increasedRatePercent", 15f);
            SkillSet set = CreateSkillSet("Set", skill, support);

            RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                Nodes(set),
                0,
                snapshot);

            Assert.That(snapshot.IncreasedRatePercent, Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(runtime.RecoveryTime, Is.EqualTo(1f / 1.3f).Within(0.0001f));
        }

        [Test]
        public void UnitStatSheetEnergyGainFieldsAggregateWithNeutralDefaultMultipliers()
        {
            UnitStatSheet sheet = CreateAsset<UnitStatSheet>("Player Stats");
            SetField(sheet, "increasedEnergyGain", 1.5f);
            SetField(sheet, "baseEnergyGain", 1.5f);
            SetField(sheet, "energyGainMultiplier", 2f);

            SkillStatSnapshot snapshot = SkillStatAggregator.Aggregate(null, sheet);
            UnitStatSheet freshSheet = CreateAsset<UnitStatSheet>("Fresh Player Stats");
            SkillStatSnapshot freshSnapshot = SkillStatAggregator.Aggregate(null, freshSheet);

            Assert.That(snapshot.IncreasedEnergyGain, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(snapshot.BaseEnergyGain, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(snapshot.EnergyGainMultiplier, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(freshSheet.IncreasedEnergyGain, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(freshSheet.EnergyGainMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(freshSnapshot.IncreasedEnergyGain, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(freshSnapshot.EnergyGainMultiplier, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void IntervalEnergyGainFoldAppliesBaseIncreaseAndMultiplier()
        {
            ProjectileIntervalSpawnTrigger trigger = CreateAsset<ProjectileIntervalSpawnTrigger>("Projectile Interval Spawn");
            trigger.energyPerSecond = 2f;
            var snapshot = new SkillStatSnapshot(
                increasedRatePercent: 0f,
                damageMultiplier: 1f,
                critChance: 0f,
                critMultiplier: 1.5f,
                baseEnergyGain: 1f,
                increasedEnergyGain: 0.5f,
                energyGainMultiplier: 2f);

            Assert.That(trigger.ResolveEnergyPerSecond(snapshot), Is.EqualTo(3f).Within(0.0001f));
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
                Nodes(set),
                0,
                SkillStatSnapshot.Identity);

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

        private SkillSet CreateSkillSet(string name, Skill skill, params SkillSupport[] supports)
        {
            SkillSet set = CreateAsset<SkillSet>(name);
            SetField(set, "skill", skill);
            SetField(set, "supports", supports ?? global::System.Array.Empty<SkillSupport>());
            return set;
        }

        private static SkillLoadoutNode[] Nodes(SkillSet set)
        {
            return new[] { new SkillLoadoutNode(set) };
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

    }
}
