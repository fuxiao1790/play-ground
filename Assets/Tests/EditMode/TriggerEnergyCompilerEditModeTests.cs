using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;
using Object = UnityEngine.Object;

namespace PlayGround.Tests.EditMode
{
    public sealed class TriggerEnergyCompilerEditModeTests
    {
        private const float MinimumTriggerEnergy = 1e-3f;
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
        public void CompilerCopiesFractionalTriggerEnergyToRuntime()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SetField(skill, "triggerEnergy", 0.375f);

            RuntimeSkillDefinition runtime = Compile(skill);

            Assert.That(runtime.TriggerEnergy, Is.EqualTo(0.375f).Within(0.0001f));
        }

        [Test]
        public void CompilerCreatesIndependentRuntimeNodesForRepeatedSkillAsset()
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Repeated Projectile Skill");
            SetField(skill, "triggerEnergy", 0.625f);
            SkillSet set = CreateSkillSet(skill);
            HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>("Hit Energy Trigger");

            var runtime = (RuntimeProjectileDefinition)SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(set, trigger),
                    new SkillLoadoutNode(set),
                },
                0,
                SkillStatSnapshot.Identity);
            RuntimeSkillDefinition triggeredRuntime = runtime.HitEnergyTrigger.TriggeredSkill;

            Assert.That(triggeredRuntime, Is.Not.SameAs(runtime));
            Assert.That(runtime.TriggerEnergy, Is.EqualTo(0.625f).Within(0.0001f));
            Assert.That(triggeredRuntime.TriggerEnergy, Is.EqualTo(0.625f).Within(0.0001f));
        }

        [Test]
        public void CompilerAttachesHitEnergyCompositionForEverySourceSkillKind()
        {
            Skill[] sources =
            {
                CreateAsset<ProjectileSkill>("Projectile"),
                CreateAsset<AoeSkill>("Impact AOE"),
                CreateAsset<LingeringAoeSkill>("Lingering AOE"),
                CreateAsset<TargetedSkill>("Targeted")
            };

            for (int i = 0; i < sources.Length; i++)
            {
                AoeSkill output = CreateAsset<AoeSkill>($"Output {i}");
                HitEnergyTrigger trigger = CreateAsset<HitEnergyTrigger>($"Trigger {i}");
                RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(
                    new[]
                    {
                        new SkillLoadoutNode(CreateSkillSet(sources[i]), trigger),
                        new SkillLoadoutNode(CreateSkillSet(output))
                    },
                    0,
                    SkillStatSnapshot.Identity);

                RuntimeHitEnergyTrigger compiled = HitEnergyTriggerFor(runtime);
                Assert.That(compiled, Is.Not.Null, sources[i].GetType().Name);
                Assert.That(compiled.TriggeredSkill, Is.TypeOf<RuntimeAoeDefinition>(), sources[i].GetType().Name);
            }
        }

        [Test]
        public void PayloadUsesIndependentContributionAndRequirementMultipliers()
        {
            var output = new RuntimeAoeDefinition
            {
                TypeId = 2,
                TriggerEnergy = 4f,
                SpawnTemplateKey = new Hash128(2u, 0u, 0u, 0u)
            };
            var source = new RuntimeAoeDefinition
            {
                TriggerEnergy = 2f,
                HitEnergyTrigger = new RuntimeHitEnergyTrigger
                {
                    TriggeredSkill = output,
                    EnergyContributionMultiplier = 0.5f,
                    EnergyRequirementMultiplier = 1.5f,
                    RetentionSeconds = 3f,
                    AccumulatorId = 17
                }
            };

            HitEnergyPayload payload = BuildHitEnergyPayload(source);

            Assert.That(payload.EnergyPerHit, Is.EqualTo(1f).Within(0.0001f), "2 * 0.5");
            Assert.That(payload.EnergyRequired, Is.EqualTo(6f).Within(0.0001f), "4 * 1.5");
            Assert.That(payload.RetentionSeconds, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(payload.AccumulatorId, Is.EqualTo(17));
        }

        [Test]
        public void SameAssetThreeNodeChainHasDistinctAdjacentEdgesAndNoTransitivePayload()
        {
            AoeSkill repeated = CreateAsset<AoeSkill>("Repeated AOE");
            SkillSet repeatedSet = CreateSkillSet(repeated);
            HitEnergyTrigger firstLink = CreateAsset<HitEnergyTrigger>("First Link");
            HitEnergyTrigger secondLink = CreateAsset<HitEnergyTrigger>("Second Link");
            SetField(firstLink, "retentionSeconds", 10f);
            SetField(secondLink, "retentionSeconds", 10f);
            var root = (RuntimeAoeDefinition)SkillSetCompiler.Compile(
                new[]
                {
                    new SkillLoadoutNode(repeatedSet, firstLink),
                    new SkillLoadoutNode(repeatedSet, secondLink),
                    new SkillLoadoutNode(repeatedSet)
                },
                0,
                SkillStatSnapshot.Identity);
            var middle = (RuntimeAoeDefinition)root.HitEnergyTrigger.TriggeredSkill;
            var final = (RuntimeAoeDefinition)middle.HitEnergyTrigger.TriggeredSkill;

            Assert.That(middle, Is.Not.SameAs(root));
            Assert.That(final, Is.Not.SameAs(root));
            Assert.That(final, Is.Not.SameAs(middle));
            Assert.That(final.HitEnergyTrigger, Is.Null);

            AssignHitEnergyAccumulatorId(root.HitEnergyTrigger);
            AssignHitEnergyAccumulatorId(middle.HitEnergyTrigger);
            Assert.That(root.HitEnergyTrigger.AccumulatorId, Is.GreaterThan(0));
            Assert.That(middle.HitEnergyTrigger.AccumulatorId, Is.GreaterThan(0));
            Assert.That(middle.HitEnergyTrigger.AccumulatorId, Is.Not.EqualTo(root.HitEnergyTrigger.AccumulatorId));

            middle.TypeId = 20;
            middle.SpawnTemplateKey = new Hash128(20u, 0u, 0u, 0u);
            final.TypeId = 30;
            final.SpawnTemplateKey = new Hash128(30u, 0u, 0u, 0u);
            HitEnergyPayload rootPayload = BuildHitEnergyPayload(root);
            HitEnergyPayload middlePayload = BuildHitEnergyPayload(middle);
            AoeSpawnCommand middleTemplate = BuildAoeTemplate(middle, middlePayload);

            Assert.That(rootPayload.AccumulatorId, Is.EqualTo(root.HitEnergyTrigger.AccumulatorId));
            Assert.That(rootPayload.AccumulatorId, Is.Not.EqualTo(middle.HitEnergyTrigger.AccumulatorId));
            Assert.That(rootPayload.Spawn.TemplateKey, Is.EqualTo(middle.SpawnTemplateKey));
            Assert.That(middleTemplate.HitPayload.HitEnergy.AccumulatorId,
                Is.EqualTo(middle.HitEnergyTrigger.AccumulatorId));
            Assert.That(middleTemplate.HitPayload.HitEnergy.Spawn.TemplateKey, Is.EqualTo(final.SpawnTemplateKey));
        }

        [TestCase(0f)]
        [TestCase(-2f)]
        public void CompilerClampsNonPositiveTriggerEnergyToPositiveFloor(float authoredValue)
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SetField(skill, "triggerEnergy", authoredValue);

            RuntimeSkillDefinition runtime = Compile(skill);

            Assert.That(runtime.TriggerEnergy, Is.EqualTo(MinimumTriggerEnergy));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void CompilerResolvesNonFiniteTriggerEnergyToPositiveFloor(float authoredValue)
        {
            ProjectileSkill skill = CreateAsset<ProjectileSkill>("Projectile Skill");
            SetField(skill, "triggerEnergy", authoredValue);

            RuntimeSkillDefinition runtime = Compile(skill);

            Assert.That(runtime.TriggerEnergy, Is.EqualTo(MinimumTriggerEnergy));
        }

        private RuntimeSkillDefinition Compile(Skill skill)
        {
            SkillSet set = CreateSkillSet(skill);
            return SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) },
                0,
                SkillStatSnapshot.Identity);
        }

        private static RuntimeHitEnergyTrigger HitEnergyTriggerFor(RuntimeSkillDefinition runtime)
        {
            if (runtime is RuntimeProjectileDefinition projectile)
                return projectile.HitEnergyTrigger;
            if (runtime is RuntimeAoeDefinition aoe)
                return aoe.HitEnergyTrigger;
            if (runtime is RuntimeTargetedDefinition targeted)
                return targeted.HitEnergyTrigger;
            return null;
        }

        private static HitEnergyPayload BuildHitEnergyPayload(RuntimeSkillDefinition runtime)
        {
            Type builder = typeof(SkillDriver).Assembly.GetType("PlayGround.Skills.SkillIntervalTemplateBuilder");
            Assert.That(builder, Is.Not.Null);
            MethodInfo method = builder.GetMethod("BuildApplicatorHitEnergyPayload", BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            return (HitEnergyPayload)method.Invoke(null, new object[] { runtime, default(HitEnergyPayload) });
        }

        private static AoeSpawnCommand BuildAoeTemplate(RuntimeAoeDefinition runtime, HitEnergyPayload hitEnergy)
        {
            Type builder = typeof(SkillDriver).Assembly.GetType("PlayGround.Skills.SkillIntervalTemplateBuilder");
            Assert.That(builder, Is.Not.Null);
            MethodInfo method = builder.GetMethod("BuildAoeTemplate", BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            return (AoeSpawnCommand)method.Invoke(
                null,
                new object[] { runtime, null, hitEnergy, default(TimedSpawnComponent) });
        }

        private static void AssignHitEnergyAccumulatorId(RuntimeHitEnergyTrigger trigger)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "EnsureHitEnergyAccumulatorId",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { trigger });
        }

        private SkillSet CreateSkillSet(Skill skill)
        {
            SkillSet set = CreateAsset<SkillSet>("Skill Set");
            SetField(set, "skill", skill);
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
            FieldInfo field = null;
            for (global::System.Type type = target.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }
    }
}
