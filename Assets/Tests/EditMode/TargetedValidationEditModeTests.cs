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
            definition.maxTargets = 0;
            definition.count = 0;
            definition.chainDelaySeconds = -1f;
            definition.acquireRadius = 0f;
            SkillSet set = CreateSkillSet(skill);

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(set));
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "maxTargets");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "count");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "chainDelaySeconds");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedConfigurationError, "will not spawn", SkillValidationSeverity.Error);

            RuntimeTargetedDefinition runtime = (RuntimeTargetedDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);
            Assert.That(runtime.MaxTargets, Is.EqualTo(1));
            Assert.That(runtime.Count, Is.EqualTo(1));
            Assert.That(runtime.ChainDelaySeconds, Is.Zero);
            Assert.That(runtime.SpawnBlocked, Is.True);
        }

        [Test]
        public void ValidatorWarnsAndCompilerClampsTargetedUpperBoundAndIntervalTick()
        {
            LingeringTargetedSkill skill = CreateLingeringSkill(lifetime: 1f, tickInterval: 0f, maxTargets: 64, chainDelay: 0f);
            SkillSet set = CreateSkillSet(skill);

            SkillValidationWarning[] warnings = Validate(new SkillLoadoutNode(set));
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "maxTargets");
            AssertWarning(warnings, SkillValidationWarningCode.TargetedParameterClamped, "tickIntervalSeconds");

            RuntimeTargetedDefinition runtime = (RuntimeTargetedDefinition)SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(set) }, 0, SkillStatSnapshot.Identity);
            Assert.That(runtime.MaxTargets, Is.EqualTo(32));
            Assert.That(runtime.TickIntervalSeconds, Is.EqualTo(0.01f).Within(0.0001f));
        }

        [Test]
        public void IntervalTruncationWarningsFireIndependently()
        {
            LingeringTargetedSkill lifetimeSkill = CreateLingeringSkill(lifetime: 0.5f, tickInterval: 2f, maxTargets: 6, chainDelay: 0.2f);
            SkillValidationWarning[] lifetimeWarnings = Validate(new SkillLoadoutNode(CreateSkillSet(lifetimeSkill)));
            AssertWarning(lifetimeWarnings, SkillValidationWarningCode.TargetedIntervalWarning, "lifetimeSeconds");
            AssertNoWarning(lifetimeWarnings, "tickIntervalSeconds; later");

            LingeringTargetedSkill tickSkill = CreateLingeringSkill(lifetime: 2f, tickInterval: 0.5f, maxTargets: 6, chainDelay: 0.2f);
            SkillValidationWarning[] tickWarnings = Validate(new SkillLoadoutNode(CreateSkillSet(tickSkill)));
            AssertWarning(tickWarnings, SkillValidationWarningCode.TargetedIntervalWarning, "tickIntervalSeconds; later");
            AssertNoWarning(tickWarnings, "lifetimeSeconds; the instance");
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
        public void ValidSingleHitWithLongChainHasNoLifetimeWarning()
        {
            TargetedSkill skill = CreateAsset<TargetedSkill>("Targeted");
            TargetedDefinition definition = (TargetedDefinition)skill.Definition;
            definition.prefab = CreateTargetedPrefab(withLinkVfx: true);
            definition.acquireRadius = 4f;
            definition.maxTargets = 32;
            definition.chainRadius = 4f;
            definition.chainDamageFalloff = 1f;
            definition.chainDelaySeconds = 10f;

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

        private LingeringTargetedSkill CreateLingeringSkill(float lifetime, float tickInterval, int maxTargets, float chainDelay)
        {
            LingeringTargetedSkill skill = CreateAsset<LingeringTargetedSkill>("Lingering Targeted");
            LingeringTargetedDefinition definition = (LingeringTargetedDefinition)skill.Definition;
            definition.prefab = CreateTargetedPrefab(withLinkVfx: true);
            definition.acquireRadius = 4f;
            definition.chainRadius = 4f;
            definition.chainDamageFalloff = 1f;
            definition.lifetimeSeconds = lifetime;
            definition.tickIntervalSeconds = tickInterval;
            definition.maxTargets = maxTargets;
            definition.chainDelaySeconds = chainDelay;
            return skill;
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

        private static void AssertNoWarning(SkillValidationWarning[] warnings, string messageFragment)
        {
            for (int i = 0; i < warnings.Length; i++)
                Assert.That(warnings[i].Message, Does.Not.Contain(messageFragment));
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
