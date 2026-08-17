using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

[assembly: InternalsVisibleTo("PlayGround.Tests.EditMode")]

namespace PlayGround.System.Combat.Audio
{
    public sealed class AudioRoot : MonoBehaviour
    {
        public static AudioRoot Instance { get; private set; }

        public int ActiveCount
        {
            get
            {
                PruneInactive(Time.time);
                return activeSounds.Count;
            }
        }
        public long Accepted => accepted;
        public long CulledDistance => culledDistance;
        public long CulledRedundant => culledRedundant;
        public long CulledNovel => culledNovel;
        public long CulledSpacing => culledSpacing;
        public long Rejected => rejected;

        internal IReadOnlyList<int> SelectedIndices => selectedIndices;

        [SerializeField, Min(0)]
        [Tooltip("Maximum number of sounds that may play concurrently. Lower-ranked events are culled when every voice is occupied.")]
        private int maxActiveSources = 24;

        [SerializeField, Min(0)]
        [Tooltip("Number of pooled AudioSources created during setup. Clamped to Max Active Sources; remaining voices are created lazily.")]
        private int prewarmSources = 8;

        [SerializeField, Min(0f)]
        [Tooltip("Minimum time between starts of the same clip across frames. Same-frame copies use the per-frame copy cap instead.")]
        private float sameSoundStartSpacingSeconds = 0.04f;

        [SerializeField, Min(0f)]
        [Tooltip("Audible radius used when a SoundEvent supplies a radius of zero or less. Also becomes that voice's maximum rolloff distance.")]
        private float defaultAudibleRadius = 20f;

        [SerializeField, Min(0)]
        [Tooltip("Maximum copies of one clip eligible to play from a single frame's batch, regardless of available voices.")]
        private int maxCopiesPerClipPerFrame = 3;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Volume multiplier applied once per repeat index: first copy is 1, second is this value, third is this value squared.")]
        private float repeatVolumeFalloff = 0.8f;

        [SerializeField, Min(0f)]
        [Tooltip("Maximum random pitch offset above or below 1 for each played voice. Uses AudioRoot's private seeded random stream.")]
        private float pitchJitterRange = 0.06f;

        [SerializeField, Min(0f)]
        [Tooltip("Maximum random start delay, in seconds, for second and later copies of a clip. The first copy always starts immediately.")]
        private float repeatStartDelayMaxSeconds = 0.02f;

        [SerializeField]
        [Tooltip("GameObject whose transform supplies the listening position. It must carry the scene's AudioListener; use BindListener when it changes at runtime.")]
        private GameObject listenerObject;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Spatialization mix assigned to pooled voices: 0 is fully 2D and 1 is fully 3D.")]
        private float spatialBlend = 0.7f;

        [SerializeField]
        [Tooltip("Distance attenuation curve assigned to every pooled AudioSource.")]
        private AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

        [SerializeField, Min(0f)]
        [Tooltip("Distance within which a spatial voice remains at full volume. Clamped to the event's resolved audible radius.")]
        private float minDistance = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Initial maximum distance assigned when a pooled source is created. Each playback replaces it with the event's resolved audible radius.")]
        private float maxDistance = 20f;

        private readonly Dictionary<AudioClip, int> idsByClip = new();
        private readonly List<AudioClip> clipsById = new() { null };
        private readonly List<SoundEvent> pending = new();
        private readonly List<AudioSource> sources = new();
        private readonly List<ActiveSound> activeSounds = new();
        private readonly Dictionary<int, float> lastStartTimeByClipId = new();
        private readonly Dictionary<int, float> batchLastStartTimeByClipId = new();
        private readonly List<int> groupingIndices = new();
        private readonly List<int> copyIndexByPendingIndex = new();
        private readonly List<int> selectedIndices = new();
        private Transform listenerTransform;
        private Unity.Mathematics.Random random = new(0x6E624EB7u);
        private bool listenerSetupErrorLogged;
        private long accepted;
        private long culledDistance;
        private long culledRedundant;
        private long culledNovel;
        private long culledSpacing;
        private long rejected;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    $"{nameof(AudioRoot)} on '{name}' rejected: '{Instance.name}' is already the "
                    + "active audio root.");
                enabled = false;
                return;
            }

            Instance = this;

            int count = Mathf.Min(Mathf.Max(0, prewarmSources), Mathf.Max(0, maxActiveSources));
            for (int i = sources.Count; i < count; i++)
            {
                CreateSource();
            }
        }

        private void OnEnable()
        {
            CacheListener();
        }

        private void LateUpdate()
        {
            float now = Time.time;
            PruneInactive(now);
            selectedIndices.Clear();
            batchLastStartTimeByClipId.Clear();

            if (pending.Count == 0)
            {
                pending.Clear();
                return;
            }

            if (listenerTransform == null)
            {
                culledDistance += pending.Count;
                pending.Clear();
                return;
            }

            int freeVoices = math.max(0, math.max(0, maxActiveSources) - activeSounds.Count);
            if (freeVoices == 0)
            {
                pending.Clear();
                return;
            }

            RankPending(
                now,
                new float2(listenerTransform.position.x, listenerTransform.position.y),
                freeVoices);
            for (int i = 0; i < selectedIndices.Count; i++)
            {
                int pendingIndex = selectedIndices[i];
                SoundEvent soundEvent = pending[pendingIndex];
                if (!TryGetClip(soundEvent.ClipId, out AudioClip clip))
                {
                    rejected++;
                    continue;
                }

                AudioSource source = AvailableSource();
                if (source == null)
                {
                    CountCapacityCull(copyIndexByPendingIndex[pendingIndex]);
                    continue;
                }

                int copyIndex = copyIndexByPendingIndex[pendingIndex];
                float volume = math.pow(math.saturate(repeatVolumeFalloff), copyIndex);
                float jitterRange = math.max(0f, pitchJitterRange);
                float pitch = math.max(
                    0.01f,
                    1f + random.NextFloat(-jitterRange, jitterRange));
                float delay = 0f;
                if (copyIndex > 0)
                {
                    float maxDelay = math.max(0f, repeatStartDelayMaxSeconds);
                    if (maxDelay > 0f)
                    {
                        delay = math.max(float.Epsilon, random.NextFloat(0f, maxDelay));
                    }
                }

                float audibleRadius = ResolveAudibleRadius(soundEvent.AudibleRadius);
                source.transform.position = new Vector3(
                    soundEvent.Position.x,
                    soundEvent.Position.y,
                    0f);
                source.clip = clip;
                source.volume = volume;
                source.pitch = pitch;
                source.priority = Mathf.Clamp(128 - soundEvent.Priority, 0, 256);
                source.spatialBlend = spatialBlend;
                source.rolloffMode = rolloffMode;
                float rolloffMaxDistance = math.max(0.01f, audibleRadius);
                source.minDistance = math.min(math.max(0f, minDistance), rolloffMaxDistance);
                source.maxDistance = rolloffMaxDistance;

                if (copyIndex == 0)
                {
                    source.Play();
                }
                else
                {
                    source.PlayDelayed(delay);
                }

                float startTime = now + delay;
                float endTime = startTime + clip.length / math.max(0.01f, math.abs(pitch));
                activeSounds.Add(new ActiveSound(source, clip, endTime));
                if (!batchLastStartTimeByClipId.TryGetValue(soundEvent.ClipId, out float batchStart)
                    || startTime > batchStart)
                {
                    batchLastStartTimeByClipId[soundEvent.ClipId] = startTime;
                }

                accepted++;
            }

            foreach (KeyValuePair<int, float> pair in batchLastStartTimeByClipId)
            {
                lastStartTimeByClipId[pair.Key] = pair.Value;
            }

            pending.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Clear();
            sources.Clear();
            clipsById.Clear();
            idsByClip.Clear();
            listenerTransform = null;
        }

        public int Register(AudioClip clip)
        {
            if (clip == null)
            {
                return 0;
            }

            if (idsByClip.TryGetValue(clip, out int existingId))
            {
                return existingId;
            }

            int id = clipsById.Count;
            clipsById.Add(clip);
            idsByClip.Add(clip, id);
            return id;
        }

        public bool TryGetClip(int clipId, out AudioClip clip)
        {
            if (clipId <= 0 || clipId >= clipsById.Count)
            {
                clip = null;
                return false;
            }

            clip = clipsById[clipId];
            return clip != null;
        }

        public void Enqueue(in SoundEvent soundEvent)
        {
            pending.Add(soundEvent);
        }

        public void BindListener(GameObject listener)
        {
            listenerObject = listener;
            CacheListener();
        }

        public string StatsText()
        {
            PruneInactive(Time.time);
            return string.Format(
                global::System.Globalization.CultureInfo.InvariantCulture,
                "AudioRoot: active:{0} pool:{1}/{2} accepted:{3} culled_distance:{4} "
                + "culled_redundant:{5} culled_novel:{6} culled_spacing:{7} rejected:{8}",
                activeSounds.Count,
                sources.Count,
                math.max(0, maxActiveSources),
                accepted,
                culledDistance,
                culledRedundant,
                culledNovel,
                culledSpacing,
                rejected);
        }

        public void Clear()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                AudioSource source = sources[i];
                if (source == null)
                {
                    continue;
                }

                source.Stop();
                source.clip = null;
            }

            pending.Clear();
            groupingIndices.Clear();
            copyIndexByPendingIndex.Clear();
            selectedIndices.Clear();
            activeSounds.Clear();
            lastStartTimeByClipId.Clear();
            batchLastStartTimeByClipId.Clear();
            accepted = 0;
            culledDistance = 0;
            culledRedundant = 0;
            culledNovel = 0;
            culledSpacing = 0;
            rejected = 0;
        }

        internal int RankPending(float now, float2 listenerPosition, int freeVoices)
        {
            groupingIndices.Clear();
            selectedIndices.Clear();
            ResizeCopyIndexScratch(pending.Count);

            if (pending.Count == 0 || freeVoices <= 0)
            {
                return 0;
            }

            for (int i = 0; i < pending.Count; i++)
            {
                SoundEvent soundEvent = pending[i];
                float audibleRadius = ResolveAudibleRadius(soundEvent.AudibleRadius);
                if (math.distancesq(soundEvent.Position, listenerPosition)
                    > audibleRadius * audibleRadius)
                {
                    culledDistance++;
                    continue;
                }

                if (soundEvent.ClipId <= 0 || soundEvent.ClipId >= clipsById.Count)
                {
                    rejected++;
                    continue;
                }

                groupingIndices.Add(i);
            }

            SortGroupingIndices();
            int currentClipId = 0;
            int copyIndex = -1;
            int allowedCopies = math.max(0, maxCopiesPerClipPerFrame);
            float spacing = math.max(0f, sameSoundStartSpacingSeconds);
            for (int i = 0; i < groupingIndices.Count; i++)
            {
                int pendingIndex = groupingIndices[i];
                SoundEvent soundEvent = pending[pendingIndex];
                if (soundEvent.ClipId != currentClipId)
                {
                    currentClipId = soundEvent.ClipId;
                    copyIndex = 0;
                }
                else
                {
                    copyIndex++;
                }

                copyIndexByPendingIndex[pendingIndex] = copyIndex;
                if (copyIndex >= allowedCopies)
                {
                    culledRedundant++;
                    continue;
                }

                if (spacing > 0f
                    && lastStartTimeByClipId.TryGetValue(soundEvent.ClipId, out float lastStart)
                    && now - lastStart < spacing)
                {
                    culledSpacing++;
                    continue;
                }

                selectedIndices.Add(pendingIndex);
            }

            SortSelectedIndices();
            int selectedCount = math.min(selectedIndices.Count, freeVoices);
            for (int i = selectedCount; i < selectedIndices.Count; i++)
            {
                CountCapacityCull(copyIndexByPendingIndex[selectedIndices[i]]);
            }

            if (selectedCount < selectedIndices.Count)
            {
                selectedIndices.RemoveRange(selectedCount, selectedIndices.Count - selectedCount);
            }

            return selectedIndices.Count;
        }

        internal int CopyIndexForPendingIndex(int pendingIndex) =>
            copyIndexByPendingIndex[pendingIndex];

        private void CacheListener()
        {
            listenerTransform = null;
            if (listenerObject == null)
            {
                LogListenerSetupError("listenerObject is not assigned");
                return;
            }

            if (!listenerObject.TryGetComponent(out AudioListener _))
            {
                AudioListener sceneListener = FindAnyObjectByType<AudioListener>();
                string actualListener = sceneListener != null
                    ? $"; scene AudioListener is on '{sceneListener.gameObject.name}'"
                    : "; no scene AudioListener was found";
                LogListenerSetupError(
                    $"listenerObject '{listenerObject.name}' does not have an AudioListener"
                    + actualListener);
                return;
            }

            listenerTransform = listenerObject.transform;
            listenerSetupErrorLogged = false;
        }

        private void LogListenerSetupError(string reason)
        {
            if (listenerSetupErrorLogged)
            {
                return;
            }

            listenerSetupErrorLogged = true;
            Debug.LogError($"{nameof(AudioRoot)} on '{name}' is not configured: {reason}.");
        }

        private AudioSource AvailableSource()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i] != null && !IsSourceActive(sources[i]))
                {
                    return sources[i];
                }
            }

            if (sources.Count >= math.max(0, maxActiveSources))
            {
                return null;
            }

            return CreateSource();
        }

        private AudioSource CreateSource()
        {
            var sourceObject = new GameObject($"OneShotAudio_{sources.Count + 1}");
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = rolloffMode;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            sources.Add(source);
            return source;
        }

        private bool IsSourceActive(AudioSource source)
        {
            for (int i = 0; i < activeSounds.Count; i++)
            {
                if (activeSounds[i].Source == source)
                {
                    return true;
                }
            }

            return false;
        }

        private void PruneInactive(float now)
        {
            for (int i = activeSounds.Count - 1; i >= 0; i--)
            {
                ActiveSound sound = activeSounds[i];
                if (sound.Source == null || !sound.Source.isPlaying || now >= sound.EndTime)
                {
                    if (sound.Source != null)
                    {
                        sound.Source.clip = null;
                    }

                    activeSounds.RemoveAt(i);
                }
            }
        }

        private float ResolveAudibleRadius(float eventRadius) =>
            eventRadius > 0f ? eventRadius : math.max(0f, defaultAudibleRadius);

        private void ResizeCopyIndexScratch(int count)
        {
            if (copyIndexByPendingIndex.Capacity < count)
            {
                copyIndexByPendingIndex.Capacity = count;
            }

            while (copyIndexByPendingIndex.Count < count)
            {
                copyIndexByPendingIndex.Add(0);
            }

            if (copyIndexByPendingIndex.Count > count)
            {
                copyIndexByPendingIndex.RemoveRange(
                    count,
                    copyIndexByPendingIndex.Count - count);
            }
        }

        private void SortGroupingIndices()
        {
            for (int i = 1; i < groupingIndices.Count; i++)
            {
                int candidate = groupingIndices[i];
                int insertionIndex = i;
                while (insertionIndex > 0
                    && CompareForGrouping(candidate, groupingIndices[insertionIndex - 1]) < 0)
                {
                    groupingIndices[insertionIndex] = groupingIndices[insertionIndex - 1];
                    insertionIndex--;
                }

                groupingIndices[insertionIndex] = candidate;
            }
        }

        private void SortSelectedIndices()
        {
            for (int i = 1; i < selectedIndices.Count; i++)
            {
                int candidate = selectedIndices[i];
                int insertionIndex = i;
                while (insertionIndex > 0
                    && CompareForSelection(candidate, selectedIndices[insertionIndex - 1]) < 0)
                {
                    selectedIndices[insertionIndex] = selectedIndices[insertionIndex - 1];
                    insertionIndex--;
                }

                selectedIndices[insertionIndex] = candidate;
            }
        }

        private int CompareForGrouping(int leftIndex, int rightIndex)
        {
            SoundEvent left = pending[leftIndex];
            SoundEvent right = pending[rightIndex];
            int comparison = left.ClipId.CompareTo(right.ClipId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.Priority.CompareTo(left.Priority);
            return comparison != 0
                ? comparison
                : CompareOccurrenceFacts(in left, leftIndex, in right, rightIndex);
        }

        private int CompareForSelection(int leftIndex, int rightIndex)
        {
            SoundEvent left = pending[leftIndex];
            SoundEvent right = pending[rightIndex];
            int comparison = right.Priority.CompareTo(left.Priority);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = copyIndexByPendingIndex[leftIndex]
                .CompareTo(copyIndexByPendingIndex[rightIndex]);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.ClipId.CompareTo(right.ClipId);
            return comparison != 0
                ? comparison
                : CompareOccurrenceFacts(in left, leftIndex, in right, rightIndex);
        }

        private static int CompareOccurrenceFacts(
            in SoundEvent left,
            int leftIndex,
            in SoundEvent right,
            int rightIndex)
        {
            int comparison = ((byte)left.Category).CompareTo((byte)right.Category);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Position.x.CompareTo(right.Position.x);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Position.y.CompareTo(right.Position.y);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Velocity.x.CompareTo(right.Velocity.x);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Velocity.y.CompareTo(right.Velocity.y);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.AudibleRadius.CompareTo(right.AudibleRadius);
            return comparison != 0 ? comparison : leftIndex.CompareTo(rightIndex);
        }

        private void CountCapacityCull(int copyIndex)
        {
            if (copyIndex == 0)
            {
                culledNovel++;
            }
            else
            {
                culledRedundant++;
            }
        }

        private readonly struct ActiveSound
        {
            public ActiveSound(AudioSource source, AudioClip clip, float endTime)
            {
                Source = source;
                Clip = clip;
                EndTime = endTime;
            }

            public AudioSource Source { get; }
            public AudioClip Clip { get; }
            public float EndTime { get; }
        }
    }
}
