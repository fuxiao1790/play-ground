using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Authoring;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class SkillCastSoundEditModeTests
    {
        private readonly List<Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                    Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void Compiler_CopiesSpawnSoundAndRadiusForAllPrefabKinds()
        {
            AudioClip clip = CreateClip("All Prefab Kinds");
            BasicAttackPrefab projectilePrefab = CreatePrefab<BasicAttackPrefab>("Projectile Prefab");
            BasicAoePrefab aoePrefab = CreatePrefab<BasicAoePrefab>("AOE Prefab");
            LingeringAoePrefab lingeringPrefab = CreatePrefab<LingeringAoePrefab>("Lingering AOE Prefab");
            TargetedPrefab targetedPrefab = CreatePrefab<TargetedPrefab>("Targeted Prefab");
            ConfigureSound(projectilePrefab, clip, 4f);
            ConfigureSound(aoePrefab, clip, 5f);
            ConfigureSound(lingeringPrefab, clip, 6f);
            ConfigureSound(targetedPrefab, clip, 7f);

            ProjectileSkill projectileSkill = CreateAsset<ProjectileSkill>();
            ((ProjectileDefinition)projectileSkill.Definition).prefab = projectilePrefab;
            AoeSkill aoeSkill = CreateAsset<AoeSkill>();
            ((AoeDefinition)aoeSkill.Definition).prefab = aoePrefab;
            LingeringAoeSkill lingeringSkill = CreateAsset<LingeringAoeSkill>();
            ((LingeringAoeDefinition)lingeringSkill.Definition).prefab = lingeringPrefab;
            TargetedSkill targetedSkill = CreateAsset<TargetedSkill>();
            ((TargetedDefinition)targetedSkill.Definition).prefab = targetedPrefab;

            AssertSound(Compile(projectileSkill), clip, 4f);
            AssertSound(Compile(aoeSkill), clip, 5f);
            AssertSound(Compile(lingeringSkill), clip, 6f);
            AssertSound(Compile(targetedSkill), clip, 7f);
        }

        [Test]
        public void CompileAndRegister_AssignsStableIdAndNullPrefabRemainsSilent()
        {
            AudioRoot audioRoot = CreateAudioRoot();
            AudioClip clip = CreateClip("Stable Cast");
            BasicAttackPrefab prefab = CreatePrefab<BasicAttackPrefab>("Audible Projectile Prefab");
            ConfigureSound(prefab, clip, 12f);

            ProjectileSkill audibleSkill = CreateAsset<ProjectileSkill>();
            ((ProjectileDefinition)audibleSkill.Definition).prefab = prefab;
            ProjectileSkill silentSkill = CreateAsset<ProjectileSkill>();
            SkillLoadout loadout = CreateAsset<SkillLoadout>();
            SetField(loadout, "nodes", new List<SkillLoadoutNode>
            {
                new(CreateSkillSet(audibleSkill)),
                new(CreateSkillSet(silentSkill))
            });

            GameObject driverObject = CreateGameObject("Skill Driver", active: false);
            SkillDriver driver = driverObject.AddComponent<SkillDriver>();
            SetField(driver, "audioRoot", audioRoot);
            SetField(driver, "runtimeLoadout", loadout);

            InvokeCompileAndRegister(driver);
            RuntimeSkillDefinition[] firstCompile = GetField<RuntimeSkillDefinition[]>(driver, "compiledSlots");
            int firstId = firstCompile[0].SoundIds.SpawnId;
            Assert.That(firstId, Is.GreaterThan(0));
            Assert.That(firstCompile[0].SpawnSoundRadius, Is.EqualTo(12f));
            Assert.That(firstCompile[1].SoundIds.SpawnId, Is.Zero);

            Assert.DoesNotThrow(() => InvokeCompileAndRegister(driver));
            RuntimeSkillDefinition[] secondCompile = GetField<RuntimeSkillDefinition[]>(driver, "compiledSlots");
            Assert.That(secondCompile[0].SoundIds.SpawnId, Is.EqualTo(firstId));
            Assert.That(secondCompile[1].SoundIds.SpawnId, Is.Zero);
        }

        private RuntimeSkillDefinition Compile(Skill skill)
        {
            return SkillSetCompiler.Compile(
                new[] { new SkillLoadoutNode(CreateSkillSet(skill)) },
                0,
                SkillStatSnapshot.Identity);
        }

        private static void AssertSound(RuntimeSkillDefinition definition, AudioClip clip, float radius)
        {
            Assert.That(definition.SpawnSound, Is.SameAs(clip));
            Assert.That(definition.SpawnSoundRadius, Is.EqualTo(radius));
        }

        private AudioRoot CreateAudioRoot()
        {
            GameObject rootObject = CreateGameObject("Audio Root", active: false);
            rootObject.AddComponent<AudioListener>();
            AudioRoot root = rootObject.AddComponent<AudioRoot>();
            root.BindListener(rootObject);
            rootObject.SetActive(true);
            return root;
        }

        private T CreatePrefab<T>(string objectName) where T : MonoBehaviour
        {
            GameObject gameObject = CreateGameObject(objectName, active: false);
            return gameObject.AddComponent<T>();
        }

        private GameObject CreateGameObject(string objectName, bool active)
        {
            var gameObject = new GameObject(objectName);
            gameObject.SetActive(active);
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private AudioClip CreateClip(string clipName)
        {
            AudioClip clip = AudioClip.Create(clipName, 64, 1, 44100, false);
            createdObjects.Add(clip);
            return clip;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            createdObjects.Add(asset);
            return asset;
        }

        private SkillSet CreateSkillSet(Skill skill)
        {
            SkillSet set = CreateAsset<SkillSet>();
            SetField(set, "skill", skill);
            return set;
        }

        private static void ConfigureSound(object prefab, AudioClip clip, float radius)
        {
            SetField(prefab, "spawnSound", clip);
            SetField(prefab, "spawnSoundRadius", radius);
        }

        private static void InvokeCompileAndRegister(SkillDriver driver)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "CompileAndRegister", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(driver, null);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {name}.");
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {name}.");
            field.SetValue(target, value);
        }
    }
}
