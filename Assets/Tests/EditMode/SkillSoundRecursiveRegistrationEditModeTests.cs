using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Skills;
using PlayGround.Skills.Runtime;
using PlayGround.System.Combat.Audio;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class SkillSoundRecursiveRegistrationEditModeTests
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
        public void RegisterSounds_RecursesAndKeepsSharedClipIdsStable()
        {
            AudioRoot audioRoot = CreateAudioRoot();
            AudioClip sharedClip = CreateClip("Shared Recursive Spawn");
            AudioClip impactClip = CreateClip("Impact Spawn");

            var intervalChild = new RuntimeAoeDefinition
            {
                SpawnSound = sharedClip,
                SpawnSoundRadius = 6f
            };
            var impactChild = new RuntimeProjectileDefinition
            {
                SpawnSound = impactClip,
                SpawnSoundRadius = 8f
            };
            var root = new RuntimeProjectileDefinition
            {
                SpawnSound = sharedClip,
                SpawnSoundRadius = 10f,
                AoeIntervalSpawnSetup = new RuntimeAoeIntervalSpawnSetup
                {
                    ChildDefinition = intervalChild
                },
                ImpactProjectileDefinition = impactChild
            };
            var nullPrefab = new RuntimeProjectileDefinition
            {
                Prefab = null,
                SpawnSound = null
            };
            impactChild.ImpactProjectileDefinition = root;

            SkillDriver driver = CreateDriver(audioRoot, root, nullPrefab);
            Assert.DoesNotThrow(() => InvokeRegisterSounds(driver));

            int rootId = root.SoundIds.SpawnId;
            int intervalId = intervalChild.SoundIds.SpawnId;
            int impactId = impactChild.SoundIds.SpawnId;
            Assert.That(rootId, Is.GreaterThan(0));
            Assert.That(intervalId, Is.EqualTo(rootId), "A shared clip must reuse one registry id.");
            Assert.That(impactId, Is.GreaterThan(0).And.Not.EqualTo(rootId));
            Assert.That(nullPrefab.SoundIds.SpawnId, Is.Zero);
            Assert.That(intervalChild.SpawnSoundRadius, Is.EqualTo(6f));
            Assert.That(impactChild.SpawnSoundRadius, Is.EqualTo(8f));

            Assert.DoesNotThrow(() => InvokeRegisterSounds(driver));
            Assert.That(root.SoundIds.SpawnId, Is.EqualTo(rootId));
            Assert.That(intervalChild.SoundIds.SpawnId, Is.EqualTo(intervalId));
            Assert.That(impactChild.SoundIds.SpawnId, Is.EqualTo(impactId));
        }

        private SkillDriver CreateDriver(
            AudioRoot audioRoot,
            params RuntimeSkillDefinition[] definitions)
        {
            GameObject driverObject = CreateGameObject("Skill Driver", active: false);
            SkillDriver driver = driverObject.AddComponent<SkillDriver>();
            SetField(driver, "audioRoot", audioRoot);
            SetField(driver, "compiledSlots", definitions);
            SetField(driver, "activeSlotCount", definitions.Length);
            return driver;
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

        private static void InvokeRegisterSounds(SkillDriver driver)
        {
            MethodInfo method = typeof(SkillDriver).GetMethod(
                "RegisterSounds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(driver, null);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {name}.");
            field.SetValue(target, value);
        }
    }
}
