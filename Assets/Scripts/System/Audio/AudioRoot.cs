using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

[assembly: InternalsVisibleTo("PlayGround.Tests.EditMode")]

namespace PlayGround.System.Combat.Audio
{
    public sealed class AudioRoot : MonoBehaviour
    {
        private const int UnityRealVoiceHeadroom = 4;
        private const int NeutralUnityPriority = 128;

        public static AudioRoot Instance { get; private set; }

        public int ActiveCount
        {
            get
            {
                PruneInactive(Time.time);
                return activeVoiceCount;
            }
        }
        public long Accepted => accepted;
        public long CulledDistance => culledDistance;
        public long CulledRedundant => culledRedundant;
        public long CulledNovel => culledNovel;
        public long CulledSpacing => culledSpacing;
        public long Rejected => rejected;

        internal IReadOnlyList<SelectedSound> SelectedSounds => selectedSounds;

        [SerializeField, Min(0)]
        [Tooltip("Concurrent duplicate-voice budget after one voice per represented clip. Runtime also caps it below Unity's real-voice limit. New unique clips may exceed this value; duplicate copies may not.")]
        private int maxActiveSources = 24;

        [SerializeField, Min(0)]
        [Tooltip("Number of unassigned AudioSources created during setup, clamped to Max Active Sources. First use assigns each to a clip-id pool; remaining voices are created lazily.")]
        private int prewarmSources = 8;

        [SerializeField, Min(0f)]
        [Tooltip("Minimum time between starts of the same clip across frames. Same-frame copies use the per-frame copy cap instead.")]
        private float sameSoundStartSpacingSeconds = 0.04f;

        [SerializeField, Min(0f)]
        [Tooltip("Audible radius used when a SoundEvent supplies a radius of zero or less. Also becomes that voice's maximum rolloff distance.")]
        private float defaultAudibleRadius = 20f;

        [SerializeField, Min(0)]
        [Tooltip("Maximum copies of one clip eligible to play from a single frame's batch. A present clip always retains its first copy, so values below one behave as one.")]
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
        private readonly List<PendingBucket> pendingByClipId = new() { null };
        private readonly List<int> pendingClipIds = new();
        private readonly List<int> eligibleClipIds = new();
        private readonly List<SelectedSound> selectedSounds = new();
        private readonly List<VoicePool> voicePoolsByClipId = new() { null };
        private readonly List<VoiceSlot> allVoices = new();
        private readonly Stack<VoiceSlot> unassignedVoices = new();
        private readonly MinPriorityQueue<VoiceSlot> activeVoicesByExpiry = new();
        private readonly Dictionary<int, float> lastStartTimeByClipId = new();
        private readonly Dictionary<int, float> batchLastStartTimeByClipId = new();
        private Transform listenerTransform;
        private Unity.Mathematics.Random random = new(0x6E624EB7u);
        private bool listenerSetupErrorLogged;
        private int pendingCount;
        private int fairnessCursor;
        private int activeVoiceCount;
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
            for (int i = allVoices.Count; i < count; i++)
            {
                unassignedVoices.Push(CreateVoice(clipId: 0));
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
            selectedSounds.Clear();
            batchLastStartTimeByClipId.Clear();

            if (pendingCount == 0)
            {
                ClearPending();
                return;
            }

            if (listenerTransform == null)
            {
                culledDistance += pendingCount;
                ClearPending();
                return;
            }

            RankPending(
                now,
                new float2(listenerTransform.position.x, listenerTransform.position.y),
                ResolveDuplicateVoiceBudget());
            for (int i = 0; i < selectedSounds.Count; i++)
            {
                SelectedSound selected = selectedSounds[i];
                SoundEvent soundEvent = selected.Event;
                if (!TryGetClip(soundEvent.ClipId, out AudioClip clip))
                {
                    rejected++;
                    continue;
                }

                VoiceSlot voice = AvailableVoice(soundEvent.ClipId, selected.CopyIndex == 0);
                if (voice == null)
                {
                    CountCapacityCull(selected.CopyIndex);
                    continue;
                }

                AudioSource source = voice.Source;
                int copyIndex = selected.CopyIndex;
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
                // AudioRoot already selected this voice. Keep every source equal so Unity's
                // global mixer cannot reintroduce clip or faction preference.
                source.priority = NeutralUnityPriority;
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
                voice.Active = true;
                voice.ExpireTime = endTime;
                activeVoicesByExpiry.Enqueue(voice, endTime);
                activeVoiceCount++;
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

            ClearPending();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Clear();
            allVoices.Clear();
            unassignedVoices.Clear();
            voicePoolsByClipId.Clear();
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
            pendingByClipId.Add(new PendingBucket());
            voicePoolsByClipId.Add(new VoicePool());
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
            EnqueueBucketed(soundEvent.ClipId, in soundEvent);
        }

        internal void EnqueueBucketed(int clipId, in SoundEvent soundEvent)
        {
            if (clipId <= 0
                || clipId >= pendingByClipId.Count
                || soundEvent.ClipId != clipId)
            {
                rejected++;
                return;
            }

            PendingBucket bucket = pendingByClipId[clipId];
            if (listenerTransform != null)
            {
                float2 listenerPosition = new(
                    listenerTransform.position.x,
                    listenerTransform.position.y);
                float audibleRadius = ResolveAudibleRadius(soundEvent.AudibleRadius);
                if (math.distancesq(soundEvent.Position, listenerPosition)
                    > audibleRadius * audibleRadius)
                {
                    culledDistance++;
                    return;
                }
            }

            // Dispatch arrives already grouped by clip, so enforce the per-clip
            // occurrence ceiling while copying the bucket. LateUpdate then scales with
            // represented clips instead of the raw projectile-spam event count.
            if (bucket.Events.Count >= math.max(1, maxCopiesPerClipPerFrame))
            {
                culledRedundant++;
                return;
            }

            if (bucket.Events.Count == 0)
            {
                pendingClipIds.Add(clipId);
            }

            bucket.Events.Add(soundEvent);
            pendingCount++;
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
                activeVoiceCount,
                allVoices.Count,
                ResolveDuplicateVoiceBudget(),
                accepted,
                culledDistance,
                culledRedundant,
                culledNovel,
                culledSpacing,
                rejected);
        }

        public void Clear()
        {
            activeVoicesByExpiry.Clear();
            activeVoiceCount = 0;
            unassignedVoices.Clear();
            for (int clipId = 1; clipId < voicePoolsByClipId.Count; clipId++)
            {
                voicePoolsByClipId[clipId].FreeVoices.Clear();
            }

            for (int i = 0; i < allVoices.Count; i++)
            {
                VoiceSlot voice = allVoices[i];
                AudioSource source = voice.Source;
                voice.Active = false;
                voice.ExpireTime = 0f;
                if (source == null)
                {
                    continue;
                }

                source.Stop();
                source.clip = null;
                if (voice.ClipId > 0 && voice.ClipId < voicePoolsByClipId.Count)
                {
                    voicePoolsByClipId[voice.ClipId].FreeVoices.Push(voice);
                }
                else
                {
                    unassignedVoices.Push(voice);
                }
            }

            ClearPending();
            selectedSounds.Clear();
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
            eligibleClipIds.Clear();
            selectedSounds.Clear();

            if (pendingCount == 0)
            {
                return 0;
            }

            int allowedCopies = math.max(1, maxCopiesPerClipPerFrame);
            float spacing = math.max(0f, sameSoundStartSpacingSeconds);
            int totalRepeatDemand = 0;
            for (int bucketIndex = 0; bucketIndex < pendingClipIds.Count; bucketIndex++)
            {
                int clipId = pendingClipIds[bucketIndex];
                PendingBucket bucket = pendingByClipId[clipId];
                bucket.ResetSelection();
                for (int eventIndex = 0; eventIndex < bucket.Events.Count; eventIndex++)
                {
                    SoundEvent soundEvent = bucket.Events[eventIndex];
                    float audibleRadius = ResolveAudibleRadius(soundEvent.AudibleRadius);
                    if (math.distancesq(soundEvent.Position, listenerPosition)
                        > audibleRadius * audibleRadius)
                    {
                        culledDistance++;
                        continue;
                    }

                    if (bucket.EligibleEventIndices.Count >= allowedCopies)
                    {
                        culledRedundant++;
                        continue;
                    }

                    bucket.EligibleEventIndices.Add(eventIndex);
                }

                if (bucket.EligibleEventIndices.Count == 0)
                {
                    continue;
                }

                if (spacing > 0f
                    && lastStartTimeByClipId.TryGetValue(clipId, out float lastStart)
                    && now - lastStart < spacing)
                {
                    culledSpacing += bucket.EligibleEventIndices.Count;
                    bucket.EligibleEventIndices.Clear();
                    continue;
                }

                bucket.RepeatDemand = bucket.EligibleEventIndices.Count - 1;
                totalRepeatDemand += bucket.RepeatDemand;
                eligibleClipIds.Add(clipId);
            }

            if (eligibleClipIds.Count == 0)
            {
                AdvanceFairnessCursor();
                return 0;
            }

            // Every represented clip keeps one occurrence. The configured/Unity voice
            // budget only controls redundant copies; it never erases a unique sound.
            for (int offset = 0; offset < eligibleClipIds.Count; offset++)
            {
                int clipId = EligibleClipAt(offset);
                PendingBucket bucket = pendingByClipId[clipId];
                AddSelection(bucket, copyIndex: 0);
            }

            int repeatBudget = math.max(
                0,
                freeVoices - activeVoiceCount - eligibleClipIds.Count);
            int repeatsToSelect = math.min(totalRepeatDemand, repeatBudget);
            AllocateRepeatQuotas(repeatsToSelect, totalRepeatDemand);
            for (int offset = 0; offset < eligibleClipIds.Count; offset++)
            {
                PendingBucket bucket = pendingByClipId[EligibleClipAt(offset)];
                for (int copyIndex = 1; copyIndex <= bucket.RepeatQuota; copyIndex++)
                {
                    AddSelection(bucket, copyIndex);
                }

                culledRedundant += bucket.RepeatDemand - bucket.RepeatQuota;
            }

            AdvanceFairnessCursor();
            return selectedSounds.Count;
        }

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

        private VoiceSlot AvailableVoice(int clipId, bool isFirstCopy)
        {
            int duplicateVoiceBudget = ResolveDuplicateVoiceBudget();
            if (!isFirstCopy && activeVoiceCount >= duplicateVoiceBudget)
            {
                return null;
            }

            VoicePool pool = voicePoolsByClipId[clipId];
            while (pool.FreeVoices.Count > 0)
            {
                VoiceSlot voice = pool.FreeVoices.Pop();
                if (voice.Source != null)
                {
                    return voice;
                }
            }

            while (unassignedVoices.Count > 0)
            {
                VoiceSlot voice = unassignedVoices.Pop();
                if (voice.Source == null)
                {
                    continue;
                }

                voice.ClipId = clipId;
                return voice;
            }

            return CreateVoice(clipId);
        }

        private int ResolveDuplicateVoiceBudget()
        {
            AudioConfiguration configuration = AudioSettings.GetConfiguration();
            return ClampDuplicateVoiceBudget(maxActiveSources, configuration.numRealVoices);
        }

        internal static int ClampDuplicateVoiceBudget(int configuredBudget, int unityRealVoices)
        {
            int nonNegativeConfiguredBudget = math.max(0, configuredBudget);
            if (nonNegativeConfiguredBudget == 0)
            {
                return 0;
            }

            int unityBudget = math.max(1, unityRealVoices - UnityRealVoiceHeadroom);
            return math.min(nonNegativeConfiguredBudget, unityBudget);
        }

        private VoiceSlot CreateVoice(int clipId)
        {
            var sourceObject = new GameObject($"OneShotAudio_{allVoices.Count + 1}");
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = rolloffMode;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            var voice = new VoiceSlot(source, clipId);
            allVoices.Add(voice);
            return voice;
        }

        private void PruneInactive(float now)
        {
            while (activeVoicesByExpiry.TryPeek(out VoiceSlot voice, out float expireTime)
                && now >= expireTime)
            {
                activeVoicesByExpiry.TryDequeue(out voice, out _);
                if (!voice.Active || voice.ExpireTime != expireTime)
                {
                    continue;
                }

                voice.Active = false;
                voice.ExpireTime = 0f;
                activeVoiceCount--;
                if (voice.Source == null)
                {
                    continue;
                }

                voice.Source.clip = null;
                voicePoolsByClipId[voice.ClipId].FreeVoices.Push(voice);
            }
        }

        private float ResolveAudibleRadius(float eventRadius) =>
            eventRadius > 0f ? eventRadius : math.max(0f, defaultAudibleRadius);

        private void AllocateRepeatQuotas(int repeatsToSelect, int totalRepeatDemand)
        {
            if (repeatsToSelect <= 0 || totalRepeatDemand <= 0)
            {
                return;
            }

            int allocated = 0;
            for (int i = 0; i < eligibleClipIds.Count; i++)
            {
                PendingBucket bucket = pendingByClipId[eligibleClipIds[i]];
                long scaledDemand = (long)bucket.RepeatDemand * repeatsToSelect;
                bucket.RepeatQuota = (int)(scaledDemand / totalRepeatDemand);
                bucket.QuotaRemainder = (int)(scaledDemand % totalRepeatDemand);
                allocated += bucket.RepeatQuota;
            }

            // The floor allocations leave fewer than one slot per represented clip.
            // Pick largest fractional remainders with bounded linear scans; voice budgets
            // are small, so this stays cheap and never needs ordering the event batch.
            while (allocated < repeatsToSelect)
            {
                PendingBucket best = null;
                int bestRemainder = -1;
                for (int offset = 0; offset < eligibleClipIds.Count; offset++)
                {
                    PendingBucket candidate = pendingByClipId[EligibleClipAt(offset)];
                    if (candidate.RepeatQuota >= candidate.RepeatDemand
                        || candidate.QuotaRemainder <= bestRemainder)
                    {
                        continue;
                    }

                    best = candidate;
                    bestRemainder = candidate.QuotaRemainder;
                }

                if (best == null)
                {
                    break;
                }

                best.RepeatQuota++;
                // A largest-remainder allocation grants at most one fractional slot.
                best.QuotaRemainder = -1;
                allocated++;
            }
        }

        private void AddSelection(PendingBucket bucket, int copyIndex)
        {
            int eventIndex = bucket.EligibleEventIndices[copyIndex];
            selectedSounds.Add(new SelectedSound(bucket.Events[eventIndex], copyIndex));
        }

        private int EligibleClipAt(int offset)
        {
            int count = eligibleClipIds.Count;
            return eligibleClipIds[(fairnessCursor + offset) % count];
        }

        private void AdvanceFairnessCursor()
        {
            if (pendingClipIds.Count > 0)
            {
                fairnessCursor = (fairnessCursor + 1) % pendingClipIds.Count;
            }
        }

        private void ClearPending()
        {
            for (int i = 0; i < pendingClipIds.Count; i++)
            {
                pendingByClipId[pendingClipIds[i]].Clear();
            }

            pendingClipIds.Clear();
            eligibleClipIds.Clear();
            pendingCount = 0;
        }

        internal readonly struct SelectedSound
        {
            public SelectedSound(SoundEvent soundEvent, int copyIndex)
            {
                Event = soundEvent;
                CopyIndex = copyIndex;
            }

            public SoundEvent Event { get; }
            public int CopyIndex { get; }
        }

        private sealed class PendingBucket
        {
            public readonly List<SoundEvent> Events = new();
            public readonly List<int> EligibleEventIndices = new();
            public int RepeatDemand;
            public int RepeatQuota;
            public int QuotaRemainder;

            public void ResetSelection()
            {
                EligibleEventIndices.Clear();
                RepeatDemand = 0;
                RepeatQuota = 0;
                QuotaRemainder = 0;
            }

            public void Clear()
            {
                Events.Clear();
                ResetSelection();
            }
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

        private sealed class VoicePool
        {
            public readonly Stack<VoiceSlot> FreeVoices = new();
        }

        private sealed class VoiceSlot
        {
            public VoiceSlot(AudioSource source, int clipId)
            {
                Source = source;
                ClipId = clipId;
            }

            public AudioSource Source { get; }
            public int ClipId { get; set; }
            public bool Active { get; set; }
            public float ExpireTime { get; set; }
        }
    }
}
