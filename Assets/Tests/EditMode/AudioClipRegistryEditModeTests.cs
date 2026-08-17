using System.Collections.Generic;
using NUnit.Framework;
using PlayGround.System.Combat.Audio;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class AudioClipRegistryEditModeTests
    {
        private readonly List<AudioClip> clips = new();
        private GameObject rootObject;

        [TearDown]
        public void TearDown()
        {
            if (rootObject != null)
            {
                Object.DestroyImmediate(rootObject);
            }

            for (int i = 0; i < clips.Count; i++)
            {
                Object.DestroyImmediate(clips[i]);
            }

            clips.Clear();
        }

        [Test]
        public void RegisterNull_ReturnsZero()
        {
            AudioRoot root = CreateRoot();

            int id = root.Register(null);

            Assert.That(id, Is.Zero);
        }

        [Test]
        public void RegisterSameClip_ReturnsStableId()
        {
            AudioRoot root = CreateRoot();
            AudioClip clip = CreateClip("Stable");

            int first = root.Register(clip);
            int second = root.Register(clip);

            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void TryGetClip_RoundTripsRegisteredIdsAndRejectsInvalidIds()
        {
            AudioRoot root = CreateRoot();
            AudioClip firstClip = CreateClip("First");
            AudioClip secondClip = CreateClip("Second");
            int firstId = root.Register(firstClip);
            int secondId = root.Register(secondClip);

            Assert.That(root.TryGetClip(firstId, out AudioClip resolvedFirst), Is.True);
            Assert.That(resolvedFirst, Is.SameAs(firstClip));
            Assert.That(root.TryGetClip(secondId, out AudioClip resolvedSecond), Is.True);
            Assert.That(resolvedSecond, Is.SameAs(secondClip));
            Assert.That(root.TryGetClip(0, out AudioClip zeroClip), Is.False);
            Assert.That(zeroClip, Is.Null);
            Assert.That(root.TryGetClip(-1, out AudioClip negativeClip), Is.False);
            Assert.That(negativeClip, Is.Null);
            Assert.That(root.TryGetClip(secondId + 1, out AudioClip outOfRangeClip), Is.False);
            Assert.That(outOfRangeClip, Is.Null);
        }

        [Test]
        public void Clear_PreservesRegisteredIds()
        {
            AudioRoot root = CreateRoot();
            AudioClip clip = CreateClip("Preserved");
            int id = root.Register(clip);

            root.Clear();

            Assert.That(root.Register(clip), Is.EqualTo(id));
            Assert.That(root.TryGetClip(id, out AudioClip resolved), Is.True);
            Assert.That(resolved, Is.SameAs(clip));
        }

        private AudioRoot CreateRoot()
        {
            rootObject = new GameObject("AudioRootTest");
            rootObject.SetActive(false);
            rootObject.AddComponent<AudioListener>();
            AudioRoot root = rootObject.AddComponent<AudioRoot>();
            root.BindListener(rootObject);
            rootObject.SetActive(true);
            return root;
        }

        private AudioClip CreateClip(string clipName)
        {
            AudioClip clip = AudioClip.Create(clipName, 64, 1, 44100, false);
            clips.Add(clip);
            return clip;
        }
    }
}
