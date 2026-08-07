using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Tests.EditMode
{
    public sealed class TargetedValidationEditModeTests
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
        public void ValidatorWarnsAndCompilerClampsInvalidTargetedParameters()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.prefab = CreateTargetedPrefab(withLinkVfx: true);
            definition.chainCount = 0;
            definition.echoCount = 0;
            definition.chainDelay = -1f;
            definition.chainDistance = 0f;
            SkillSet set = CreateSkillSet(skill);

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(set));
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "chainCount");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "echoCount");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "chainDelay");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedConfigurationError, "will not spawn", SkillValidationSeverity.Error);

            RuntimeTargetedDefinition runtime = (RuntimeTargetedDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);
            Assert.That(runtime.ChainCount, Is.EqualTo(1));
            Assert.That(runtime.EchoCount, Is.EqualTo(1));
            Assert.That(runtime.ChainDelay, Is.Zero);
            Assert.That(runtime.SpawnBlocked, Is.True);
        }

        [Test]
        public void ValidatorWarnsAndCompilerClampsChainCountUpperBound()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.prefab = CreateTargetedPrefab(withLinkVfx: true);
            definition.chainDistance = 4f;
            definition.chainDamageFalloff = 1f;
            definition.chainCount = 64;
            SkillSet set = CreateSkillSet(skill);

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(set));
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "chainCount");

            RuntimeTargetedDefinition runtime = (RuntimeTargetedDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);
            Assert.That(runtime.ChainCount, Is.EqualTo(32));
        }

        [Test]
        public void ValidatorWarnsWhenTargetedIntervalChildCannotAccrueEnoughEnergy()
        {
            ProjectileSkill source = CreateAsset<ProjectileSkill>("Source");
            ((ProjectileDefinition)source.Definition).lifetime = 0.5f;
            TargetedSkill child = CreateAsset<TargetedSkill>("Child");
            TargetedDefinition childDefinition = (TargetedDefinition)child.Definition;
            childDefinition.prefab = CreateTargetedPrefab(withLinkVfx: true);
            childDefinition.manaCost = 1f;
            TargetedIntervalSpawnTrigger trigger = CreateAsset<TargetedIntervalSpawnTrigger>("Targeted Interval");
            trigger.energyPerSecond = 1f;

            SkillValidationWarning[] warnings = Validate(
                new SkillLoadoutNode(CreateSkillSet(source), trigger),
                new SkillLoadoutNode(CreateSkillSet(child)));

            AssertWarning(warnings, SkillValidationWarningCode.TargetedIntervalWarning, "energy threshold exceeds");
        }

        [Test]
        public void LongSlowChainProducesNoWarnings()
        {
            // The instance lives exactly as long as its walk, so no combination of chainCount and
            // chainDelay can truncate it. There is nothing left here to warn about.
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.prefab = CreateTargetedPrefab(withLinkVfx: true);
            definition.chainCount = 32;
            definition.chainDistance = 4f;
            definition.chainDamageFalloff = 1f;
            definition.chainDelay = 10f;

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(CreateSkillSet(skill)));

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void ValidatorReportsTargetedVisualFailures()
        {
            TargetedSkill missingLinkSkill = CreateAsset<TargetedSkill>("Missing Link");
            TargetedDefinition missingLink = (TargetedDefinition)missingLinkSkill.Definition;
            missingLink.prefab = CreateTargetedPrefab(withLinkVfx: false);
            AssertWarning(
                Validate(new SkillLoadoutNode(CreateSkillSet(missingLinkSkill))),
                SkillValidationWarningCode.TargetedVisualWarning,
                "link VFX is missing");

            TargetedSkill hurtboxSkill = CreateAsset<TargetedSkill>("Hurtbox");
            TargetedDefinition hurtbox = (TargetedDefinition)hurtboxSkill.Definition;
            TargetedPrefab hurtboxPrefab = CreateTargetedPrefab(withLinkVfx: true);
            new GameObject("Hurtbox").transform.SetParent(hurtboxPrefab.transform);
            hurtbox.prefab = hurtboxPrefab;
            AssertWarning(
                Validate(new SkillLoadoutNode(CreateSkillSet(hurtboxSkill))),
                SkillValidationWarningCode.TargetedVisualWarning,
                "Hurtbox",
                SkillValidationSeverity.Error);

            TargetedSkill sizesSkill = CreateAsset<TargetedSkill>("Invalid Sizes");
            TargetedDefinition sizes = (TargetedDefinition)sizesSkill.Definition;
            sizes.prefab = CreateTargetedPrefab(withLinkVfx: true, effectSize: 0f, linkWidth: 0f, withSpawnVfx: true);
            SkillValidationWarning[] sizeWarnings = Validate(new SkillLoadoutNode(CreateSkillSet(sizesSkill)));
            AssertWarning(sizeWarnings, SkillValidationWarningCode.TargetedVisualWarning, "vfxEffectSize");
            AssertWarning(sizeWarnings, SkillValidationWarningCode.TargetedVisualWarning, "linkWidth");
        }

        private TargetedPrefab CreateTargetedPrefab(
            bool withLinkVfx,
            float effectSize = 1f,
            float linkWidth = 1f,
            bool withSpawnVfx = false)
        {
            GameObject gameObject = new("Targeted Prefab");
            createdObjects.Add(gameObject);
            TargetedPrefab prefab = gameObject.AddComponent<TargetedPrefab>();
            VisualEffectAsset vfx = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Vfx/Aoe/Lingering/Vortex.vfx");
            Assert.That(vfx, Is.Not.Null);
            prefab.Configure(
                renderer: null,
                spawnVisualEffect: withSpawnVfx ? vfx : null,
                linkVisualEffect: withLinkVfx ? vfx : null,
                linkShape: VfxDataShape.LineSegment,
                effectSize: effectSize,
                authoredLinkWidth: linkWidth);
            return prefab;
        }

        private SkillSet CreateSkillSet(Skill skill)
        {
            SkillSet set = CreateAsset<SkillSet>("Targeted Set");
            SetField(set, "skill", skill);
            SetField(set, "supports", global::System.Array.Empty<SkillSupport>());
            return set;
        }

        private SkillValidationWarning[] Validate(params SkillLoadoutNode[] nodes)
        {
            var warnings = new List<SkillValidationWarning>();
            SkillLoadoutValidator.Validate(nodes, warnings);
            return warnings.ToArray();
        }

        private T CreateAsset<T>(string name) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            createdObjects.Add(asset);
            return asset;
        }

        private static void AssertWarning(
            SkillValidationWarning[] warnings,
            SkillValidationWarningCode code,
            string messageFragment,
            SkillValidationSeverity severity = SkillValidationSeverity.Warning)
        {
            for (int i = 0; i < warnings.Length; i++)
            {
                if (warnings[i].Code == code
                    && warnings[i].Severity == severity
                    && warnings[i].Message.Contains(messageFragment))
                    return;
            }

            Assert.Fail($"Expected {severity} {code} containing '{messageFragment}'.");
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
