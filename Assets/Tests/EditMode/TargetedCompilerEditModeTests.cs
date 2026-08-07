using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targeted;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedCompilerEditModeTests
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
        public void CompilerMapsSingleAndLingeringTargetedValues()
        {
            TargetedSkill singleSkill = CreateAsset<TargetedSkill>("Targeted Skill");
            TargetedDefinition single = (TargetedDefinition)singleSkill.Definition;
            single.count = 3;
            single.acquireRadius = 2f;
            single.maxTargets = 6;
            single.chainRadius = 4f;
            single.chainDamageFalloff = 0.8f;
            single.chainDelaySeconds = 0.2f;
            single.damage = 10f;
            single.armSeconds = 0.1f;
            single.prefab = CreatePrefab();

            RuntimeTargetedDefinition compiledSingle = Compile(singleSkill);

            Assert.That(compiledSingle.Count, Is.EqualTo(3));
            Assert.That(compiledSingle.AcquireRadius, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(compiledSingle.ChainRadius, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(compiledSingle.LifetimeSeconds, Is.Zero);
            Assert.That(compiledSingle.TickIntervalSeconds, Is.Zero);
            Assert.That(TargetedVariant.ChildKindFor(compiledSingle.LifetimeSeconds), Is.EqualTo(IntervalChildKind.Targeted));

            LingeringTargetedSkill lingeringSkill = CreateAsset<LingeringTargetedSkill>("Lingering Targeted Skill");
            LingeringTargetedDefinition lingering = (LingeringTargetedDefinition)lingeringSkill.Definition;
            lingering.lifetimeSeconds = 3f;
            lingering.tickIntervalSeconds = 0.4f;
            lingering.prefab = CreatePrefab();

            RuntimeTargetedDefinition compiledLingering = Compile(lingeringSkill);

            Assert.That(compiledLingering.LifetimeSeconds, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(compiledLingering.TickIntervalSeconds, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(TargetedVariant.ChildKindFor(compiledLingering.LifetimeSeconds), Is.EqualTo(IntervalChildKind.LingeringTargeted));
        }

        [Test]
        public void CompilerFoldsAreaDamageAndRateWithoutScalingVisualSize()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted Skill");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.acquireRadius = 2f;
            definition.chainRadius = 3f;
            definition.damage = 10f;
            definition.prefab = CreatePrefab();

            AddedDamageSupport damageSupport = CreateAsset<AddedDamageSupport>("Added Damage");
            SetField(damageSupport, "addedDamage", 5f);
            IncreasedRateSupport rateSupport = CreateAsset<IncreasedRateSupport>("Increased Rate");
            SetField(rateSupport, "increasedRatePercent", 50f);
            SkillSet set = CreateSkillSet(skill, damageSupport, rateSupport);

            RuntimeTargetedDefinition runtime = (RuntimeTargetedDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                new SkillStatSnapshot(0f, 1f, 0f, 1.5f, areaSizeMultiplier: 2f));

            Assert.That(runtime.Damage, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(runtime.AcquireRadius, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(runtime.ChainRadius, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(runtime.RecoveryTime, Is.EqualTo(1f / 7.5f).Within(0.0001f));
            Assert.That(runtime.VfxSize.EffectSize, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(runtime.VfxSize.LinkWidth, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SpriteLessTargetedDefinitionStartsWithoutRenderOrVfxIds()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted Skill");
            RuntimeTargetedDefinition runtime = Compile(skill);

            Assert.That(runtime.RenderId, Is.Zero);
            Assert.That(runtime.VfxIds.SpawnId, Is.Zero);
            Assert.That(runtime.VfxIds.HitId, Is.Zero);
            Assert.That(runtime.VfxIds.ExpireId, Is.Zero);
            Assert.That(runtime.VfxIds.LinkId, Is.Zero);
            Assert.That(runtime.VfxIds.ArmingId, Is.Zero);
        }

        private RuntimeTargetedDefinition Compile(Skill skill)
        {
            SkillSet set = CreateSkillSet(skill);
            return (RuntimeTargetedDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);
        }

        private TargetedPrefab CreatePrefab()
        {
            GameObject gameObject = new("Targeted Prefab");
            createdObjects.Add(gameObject);
            return gameObject.AddComponent<TargetedPrefab>();
        }

        private SkillSet CreateSkillSet(Skill skill, params SkillSupport[] supports)
        {
            SkillSet set = CreateAsset<SkillSet>("Targeted Set");
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
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
