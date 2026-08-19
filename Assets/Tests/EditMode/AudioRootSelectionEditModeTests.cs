using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PlayGround.System.Combat.Audio;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class AudioRootSelectionEditModeTests
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
        public void RankPending_NoveltyBeatsRedundancyAtEqualPriority()
        {
            AudioRoot root = CreateRoot();
            int repeatedId = RegisterClip(root, "Repeated");
            int novelId = RegisterClip(root, "Novel");
            Enqueue(root, repeatedId, priority: 0);
            Enqueue(root, repeatedId, priority: 0);
            Enqueue(root, novelId, priority: 0);

            int selected = root.RankPending(10f, float2.zero, 2);

            Assert.That(selected, Is.EqualTo(2));
            Assert.That(SelectedCountForClip(root, repeatedId), Is.EqualTo(1));
            Assert.That(SelectedCountForClip(root, novelId), Is.EqualTo(1));
            Assert.That(root.CulledRedundant, Is.EqualTo(1));
            Assert.That(root.CulledNovel, Is.Zero);
        }

        [Test]
        public void RankPending_NoveltyBeatsPriority()
        {
            AudioRoot root = CreateRoot();
            int repeatedId = RegisterClip(root, "HighPriorityRepeated");
            int novelId = RegisterClip(root, "LowPriorityNovel");
            Enqueue(root, repeatedId, priority: 10);
            Enqueue(root, repeatedId, priority: 10);
            Enqueue(root, novelId, priority: 0);

            root.RankPending(10f, float2.zero, 2);

            Assert.That(SelectedCountForClip(root, repeatedId), Is.EqualTo(1));
            Assert.That(SelectedCountForClip(root, novelId), Is.EqualTo(1));
            Assert.That(root.CulledNovel, Is.Zero);
            Assert.That(root.CulledRedundant, Is.EqualTo(1));
        }

        [Test]
        public void RankPending_CullsRepeatCopiesProportionallyAfterOnePerClip()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "maxCopiesPerClipPerFrame", 10);
            int highVolumeId = RegisterClip(root, "HighVolume");
            int lowVolumeId = RegisterClip(root, "LowVolume");
            for (int i = 0; i < 8; i++)
            {
                Enqueue(root, highVolumeId, priority: 10);
            }

            for (int i = 0; i < 4; i++)
            {
                Enqueue(root, lowVolumeId, priority: 0);
            }

            root.RankPending(10f, float2.zero, 6);

            Assert.That(SelectedCountForClip(root, highVolumeId), Is.EqualTo(4));
            Assert.That(SelectedCountForClip(root, lowVolumeId), Is.EqualTo(2));
            Assert.That(root.CulledNovel, Is.Zero);
            Assert.That(root.CulledRedundant, Is.EqualTo(6));
        }

        [Test]
        public void RankPending_PerClipCapCannotRemoveFirstCopy()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "maxCopiesPerClipPerFrame", 0);
            int firstId = RegisterClip(root, "First");
            int secondId = RegisterClip(root, "Second");
            Enqueue(root, firstId);
            Enqueue(root, firstId);
            Enqueue(root, secondId);
            Enqueue(root, secondId);

            root.RankPending(10f, float2.zero, 10);

            Assert.That(SelectedCountForClip(root, firstId), Is.EqualTo(1));
            Assert.That(SelectedCountForClip(root, secondId), Is.EqualTo(1));
            Assert.That(root.CulledNovel, Is.Zero);
            Assert.That(root.CulledRedundant, Is.EqualTo(2));
        }

        [TestCase(300, 32, 28)]
        [TestCase(24, 32, 24)]
        [TestCase(0, 32, 0)]
        [TestCase(24, 2, 1)]
        public void ClampDuplicateVoiceBudget_LeavesUnityRealVoiceHeadroom(
            int configuredBudget,
            int unityRealVoices,
            int expectedBudget)
        {
            Assert.That(
                AudioRoot.ClampDuplicateVoiceBudget(configuredBudget, unityRealVoices),
                Is.EqualTo(expectedBudget));
        }

        [Test]
        public void RankPending_RecentClipIsSpacingCulledRegardlessOfPriority()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "sameSoundStartSpacingSeconds", 0.2f);
            int recentHighPriorityId = RegisterClip(root, "RecentHighPriority");
            int olderLowPriorityId = RegisterClip(root, "OlderLowPriority");
            SetLastStartTime(root, recentHighPriorityId, 9.9f);
            SetLastStartTime(root, olderLowPriorityId, 5f);
            Enqueue(root, recentHighPriorityId, priority: 10);
            Enqueue(root, olderLowPriorityId, priority: 0);

            root.RankPending(10f, float2.zero, 1);

            Assert.That(SelectedCountForClip(root, recentHighPriorityId), Is.Zero);
            Assert.That(SelectedCountForClip(root, olderLowPriorityId), Is.EqualTo(1));
            Assert.That(root.CulledNovel, Is.Zero);
            Assert.That(root.CulledSpacing, Is.EqualTo(1));
        }

        [Test]
        public void RankPending_SpatialCullRemovesFarEventsBeforeRanking()
        {
            AudioRoot root = CreateRoot();
            int clipId = RegisterClip(root, "Spatial");
            Enqueue(root, clipId, position: new float2(9f, 0f), audibleRadius: 10f);
            Enqueue(root, clipId, position: new float2(11f, 0f), audibleRadius: 10f);

            root.RankPending(10f, float2.zero, 10);

            Assert.That(root.SelectedSounds.Count, Is.EqualTo(1));
            Assert.That(root.SelectedSounds[0].Event.Position.x, Is.EqualTo(9f));
            Assert.That(root.CulledDistance, Is.EqualTo(1));
        }

        [Test]
        public void RankPending_PerEventAudibleRadiusCullsIndependently()
        {
            AudioRoot root = CreateRoot();
            int shortRangeId = RegisterClip(root, "ShortRange");
            int longRangeId = RegisterClip(root, "LongRange");
            Enqueue(root, shortRangeId, position: new float2(6f, 0f), audibleRadius: 5f);
            Enqueue(root, longRangeId, position: new float2(6f, 0f), audibleRadius: 7f);

            root.RankPending(10f, float2.zero, 10);

            Assert.That(SelectedCountForClip(root, shortRangeId), Is.Zero);
            Assert.That(SelectedCountForClip(root, longRangeId), Is.EqualTo(1));
            Assert.That(root.CulledDistance, Is.EqualTo(1));
        }

        [Test]
        public void RankPending_NonPositiveRadiusUsesDefaultAudibleRadius()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "defaultAudibleRadius", 5f);
            int nearId = RegisterClip(root, "DefaultNear");
            int farId = RegisterClip(root, "DefaultFar");
            Enqueue(root, nearId, position: new float2(4f, 0f), audibleRadius: 0f);
            Enqueue(root, farId, position: new float2(6f, 0f), audibleRadius: -1f);

            root.RankPending(10f, float2.zero, 10);

            Assert.That(SelectedCountForClip(root, nearId), Is.EqualTo(1));
            Assert.That(SelectedCountForClip(root, farId), Is.Zero);
            Assert.That(root.CulledDistance, Is.EqualTo(1));
        }

        [Test]
        public void RankPending_PerClipCapHoldsWithAbundantVoices()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "maxCopiesPerClipPerFrame", 2);
            int clipId = RegisterClip(root, "Capped");
            Enqueue(root, clipId);
            Enqueue(root, clipId);
            Enqueue(root, clipId);
            Enqueue(root, clipId);

            root.RankPending(10f, float2.zero, 100);

            Assert.That(root.SelectedSounds.Count, Is.EqualTo(2));
            Assert.That(root.CulledRedundant, Is.EqualTo(2));
        }

        [Test]
        public void RankPending_KeepsOnePerClipEvenWhenVoiceBudgetIsSmaller()
        {
            AudioRoot root = CreateRoot();
            for (int i = 0; i < 5; i++)
            {
                Enqueue(root, RegisterClip(root, $"Clip{i}"));
            }

            int selected = root.RankPending(10f, float2.zero, 3);

            Assert.That(selected, Is.EqualTo(5));
            Assert.That(root.SelectedSounds.Count, Is.EqualTo(5));
            Assert.That(root.CulledNovel, Is.Zero);
        }

        [Test]
        public void RankPending_RepeatFalloffUsesCopyIndexAndLeavesFirstAtFullVolume()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "repeatVolumeFalloff", 0.5f);
            int clipId = RegisterClip(root, "Falloff");
            Enqueue(root, clipId);
            Enqueue(root, clipId);
            Enqueue(root, clipId);

            root.RankPending(10f, float2.zero, 3);

            Assert.That(SelectedVolume(root, 0, 0.5f), Is.EqualTo(1f));
            Assert.That(SelectedVolume(root, 1, 0.5f), Is.EqualTo(0.5f));
            Assert.That(SelectedVolume(root, 2, 0.5f), Is.EqualTo(0.25f));
        }

        [Test]
        public void RankPending_CulledNovelIsZeroWhenDistinctKindsFit()
        {
            AudioRoot root = CreateRoot();
            int repeatedId = RegisterClip(root, "Repeated");
            int secondId = RegisterClip(root, "Second");
            int thirdId = RegisterClip(root, "Third");
            Enqueue(root, repeatedId);
            Enqueue(root, repeatedId);
            Enqueue(root, secondId);
            Enqueue(root, thirdId);

            root.RankPending(10f, float2.zero, 3);

            Assert.That(root.SelectedSounds.Count, Is.EqualTo(3));
            Assert.That(root.CulledNovel, Is.Zero);
            Assert.That(root.CulledRedundant, Is.EqualTo(1));
        }

        [Test]
        public void RankPending_EmptyBatchProducesNoOutputOrCounts()
        {
            AudioRoot root = CreateRoot();

            Assert.That(root.RankPending(10f, float2.zero, 3), Is.Zero);
            AssertNoCounts(root);
        }

        [Test]
        public void RankPending_ZeroVoiceBudgetStillKeepsOneUniqueOccurrence()
        {
            AudioRoot root = CreateRoot();
            int clipId = RegisterClip(root, "Unique");

            Enqueue(root, clipId);

            Assert.That(root.RankPending(10f, float2.zero, 0), Is.EqualTo(1));
            Assert.That(root.SelectedSounds[0].Event.ClipId, Is.EqualTo(clipId));
            AssertNoCounts(root);
        }

        [Test]
        public void RankPending_SameFrameDuplicatesAreNotSpacingCulled()
        {
            AudioRoot root = CreateRoot();
            SetPrivateField(root, "sameSoundStartSpacingSeconds", 100f);
            int clipId = RegisterClip(root, "SameFrame");
            Enqueue(root, clipId);
            Enqueue(root, clipId);
            Enqueue(root, clipId);

            root.RankPending(10f, float2.zero, 3);

            Assert.That(root.SelectedSounds.Count, Is.EqualTo(3));
            Assert.That(root.CulledSpacing, Is.Zero);
        }

        [Test]
        public void RankPending_InvalidAndUnknownClipIdsAreRejected()
        {
            AudioRoot root = CreateRoot();
            int validId = RegisterClip(root, "Valid");
            Enqueue(root, 0);
            Enqueue(root, -1);
            Enqueue(root, validId + 1);
            Enqueue(root, validId);

            root.RankPending(10f, float2.zero, 10);

            Assert.That(root.SelectedSounds.Count, Is.EqualTo(1));
            Assert.That(root.Rejected, Is.EqualTo(3));
        }

        private AudioRoot CreateRoot()
        {
            rootObject = new GameObject("AudioRootSelectionTest");
            rootObject.SetActive(false);
            rootObject.AddComponent<AudioListener>();
            AudioRoot root = rootObject.AddComponent<AudioRoot>();
            root.BindListener(rootObject);
            rootObject.SetActive(true);
            return root;
        }

        private int RegisterClip(AudioRoot root, string clipName)
        {
            AudioClip clip = AudioClip.Create(clipName, 64, 1, 44100, false);
            clips.Add(clip);
            return root.Register(clip);
        }

        private void Enqueue(
            AudioRoot root,
            int clipId,
            short priority = 0,
            float2 position = default,
            float audibleRadius = 10f)
        {
            var soundEvent = new SoundEvent
            {
                ClipId = clipId,
                Position = position,
                AudibleRadius = audibleRadius,
                Category = SoundCategory.Cast,
                Priority = priority
            };
            root.Enqueue(in soundEvent);
        }

        private int SelectedCountForClip(AudioRoot root, int clipId)
        {
            int count = 0;
            for (int i = 0; i < root.SelectedSounds.Count; i++)
            {
                if (root.SelectedSounds[i].Event.ClipId == clipId)
                {
                    count++;
                }
            }

            return count;
        }

        private static float SelectedVolume(AudioRoot root, int selectionIndex, float falloff)
        {
            return math.pow(falloff, root.SelectedSounds[selectionIndex].CopyIndex);
        }

        private static void AssertNoCounts(AudioRoot root)
        {
            Assert.That(root.Accepted, Is.Zero);
            Assert.That(root.CulledDistance, Is.Zero);
            Assert.That(root.CulledRedundant, Is.Zero);
            Assert.That(root.CulledNovel, Is.Zero);
            Assert.That(root.CulledSpacing, Is.Zero);
            Assert.That(root.Rejected, Is.Zero);
        }

        private static void SetPrivateField<T>(AudioRoot root, string fieldName, T value)
        {
            FieldInfo field = typeof(AudioRoot).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(root, value);
        }

        private static void SetLastStartTime(AudioRoot root, int clipId, float lastStart)
        {
            FieldInfo field = typeof(AudioRoot).GetField(
                "lastStartTimeByClipId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing last-start history.");
            var history = (Dictionary<int, float>)field.GetValue(root);
            history[clipId] = lastStart;
        }
    }
}
