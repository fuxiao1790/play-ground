using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class CritEditModeTests
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
        public void DamageSnapshot_IsCritFlag_RoundTrips()
        {
            var crit = new DamageSnapshot(20f, isCrit: true);
            var normal = new DamageSnapshot(5f);

            Assert.That(crit.Amount, Is.EqualTo(20f));
            Assert.That(crit.IsCrit, Is.True);
            Assert.That(normal.Amount, Is.EqualTo(5f));
            Assert.That(normal.IsCrit, Is.False);
        }

        [Test]
        public void Compiler_CopiesCritFromSnapshot_IntoRuntimeProjectile()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SkillSet set = CreateSkillSet("Set", skill);
            var snapshot = new PlayerStatSnapshot(1f, 1f, critChance: 0.3f, critMultiplier: 2.5f);

            RuntimeSkillDefinition result = SkillSetCompiler.Compile(set, global::System.Array.Empty<TriggerChain>(), snapshot);

            Assert.That(result, Is.TypeOf<RuntimeProjectileDefinition>());
            Assert.That(result.CritChance, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(result.CritMultiplier, Is.EqualTo(2.5f).Within(0.0001f));
        }

        [Test]
        public void Compiler_CopiesCritFromSnapshot_IntoRuntimeAoe()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("Aoe Skill");
            SkillSet set = CreateSkillSet("Set", skill);
            var snapshot = new PlayerStatSnapshot(1f, 1f, critChance: 0.15f, critMultiplier: 3f);

            RuntimeSkillDefinition result = SkillSetCompiler.Compile(set, global::System.Array.Empty<TriggerChain>(), snapshot);

            Assert.That(result, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(result.CritChance, Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(result.CritMultiplier, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void Compiler_AppliesAreaSizeMultiplier_IntoRuntimeAoe()
        {
            AoeSkill skill = CreateAsset<AoeSkill>("Aoe Skill");
            ((AoeDefinition)skill.Definition).baseAreaSize = 1.25f;
            SkillSet set = CreateSkillSet("Set", skill);
            var snapshot = new PlayerStatSnapshot(1f, 1f, critChance: 0f, critMultiplier: 1.5f, areaSizeMultiplier: 2f);

            RuntimeSkillDefinition result = SkillSetCompiler.Compile(set, global::System.Array.Empty<TriggerChain>(), snapshot);

            Assert.That(result, Is.TypeOf<RuntimeAoeDefinition>());
            Assert.That(((RuntimeAoeDefinition)result).AreaSize, Is.EqualTo(2.5f).Within(0.0001f));
        }

        private SkillSet CreateSkillSet(string name, Skill skill)
        {
            SkillSet set = CreateAsset<SkillSet>(name);
            SetField(set, "skill", skill);
            SetField(set, "supports", global::System.Array.Empty<AdditiveSupport>());
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
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }
    }
}
