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
        public void CompilerMapsAuthoredTargetedValues()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted Skill");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.echoCount = 3;
            definition.chainCount = 6;
            definition.chainDistance = 4f;
            definition.chainDamageFalloff = 0.8f;
            definition.chainDelay = 0.2f;
            definition.damage = 10f;
            definition.armSeconds = 0.1f;
            definition.prefab = CreatePrefab();

            RuntimeTargetedDefinition compiled = Compile(skill);

            Assert.That(compiled.EchoCount, Is.EqualTo(3));
            Assert.That(compiled.ChainCount, Is.EqualTo(6));
            Assert.That(compiled.ChainDistance, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(compiled.ChainDamageFalloff, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(compiled.ChainDelay, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void CompiledLifetimeCoversTheWholeWalkAndIsNeverAuthored()
        {
            // Lifetime is a fail-safe derived from the walk, so it must always outlast the walk
            // itself; the resolve is what actually expires the instance when the last link lands.
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted Skill");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.chainCount = 6;
            definition.chainDelay = 0.2f;
            definition.chainDistance = 4f;
            definition.prefab = CreatePrefab();

            RuntimeTargetedDefinition compiled = Compile(skill);

            Assert.That(compiled.LifetimeSeconds, Is.GreaterThan(6 * 0.2f));

            definition.chainDelay = 0f;
            RuntimeTargetedDefinition instant = Compile(skill);
            Assert.That(instant.LifetimeSeconds, Is.GreaterThan(0f));
        }

        [Test]
        public void CompilerFoldsAreaDamageAndRateWithoutScalingVisualSize()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted Skill");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.chainDistance = 3f;
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
            Assert.That(runtime.ChainDistance, Is.EqualTo(6f).Within(0.0001f));
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
