using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Audio
{
    public sealed class AudioManager : MonoBehaviour
    {
        public const int DefaultMaxSimultaneousPerSound = 8;
        public const float DefaultSameSoundStartSpacingSeconds = 0.04f;

        public static AudioManager Instance { get; private set; }

        [SerializeField, Min(0)] private int maxActiveSources = 24;
        [SerializeField, Min(0)] private int prewarmSources = 8;
        [SerializeField, Min(0f)] private float sameSoundStartSpacingSeconds = DefaultSameSoundStartSpacingSeconds;
        [SerializeField, Range(0f, 1f)] private float spatialBlend;
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;
        [SerializeField, Min(0f)] private float minDistance = 1f;
        [SerializeField, Min(0.01f)] private float maxDistance = 20f;

        private readonly List<AudioSource> sources = new();
        private readonly List<ActiveSound> activeSounds = new();
        private readonly Dictionary<AudioClip, float> lastStartTimeByClip = new();
        private long accepted;
        private long culledSpacing;
        private long culledCapacity;
        private long rejected;

        public int ActiveCount
        {
            get
            {
                PruneInactive();
                return activeSounds.Count;
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            int count = Mathf.Min(Mathf.Max(0, prewarmSources), Mathf.Max(0, maxActiveSources));
            for (int i = sources.Count; i < count; i++)
            {
                CreateSource();
            }
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool PlaySound(AudioClip clip, Vector2 worldPosition, int priority = 0, int maxSimultaneous = DefaultMaxSimultaneousPerSound)
        {
            if (clip == null)
            {
                rejected++;
                return false;
            }

            PruneInactive();

            int allowedSimultaneous = Mathf.Max(0, maxSimultaneous);
            if (allowedSimultaneous == 0 || CurrentPlayingCountNoPrune(clip) >= allowedSimultaneous)
            {
                culledCapacity++;
                return false;
            }

            float now = Time.time;
            if (ShouldCullForSpacing(clip, now))
            {
                culledSpacing++;
                return false;
            }

            AudioSource source = AvailableSource();
            if (source == null)
            {
                culledCapacity++;
                return false;
            }

            source.transform.position = worldPosition;
            source.clip = clip;
            source.priority = Mathf.Clamp(128 - priority, 0, 256);
            source.Play();

            activeSounds.Add(new ActiveSound(source, clip, now + clip.length));
            lastStartTimeByClip[clip] = now;
            accepted++;
            return true;
        }

        public int CurrentPlayingCount(AudioClip clip)
        {
            if (clip == null)
            {
                return 0;
            }

            PruneInactive();
            return CurrentPlayingCountNoPrune(clip);
        }

        public string StatsText()
        {
            PruneInactive();
            return string.Format(
                global::System.Globalization.CultureInfo.InvariantCulture,
                "AudioManager: active:{0} pool:{1}/{2} accepted:{3} culled_spacing:{4} culled_capacity:{5} rejected:{6}",
                activeSounds.Count,
                sources.Count,
                Mathf.Max(0, maxActiveSources),
                accepted,
                culledSpacing,
                culledCapacity,
                rejected);
        }

        public void Clear()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Stop();
                sources[i].clip = null;
            }

            activeSounds.Clear();
            lastStartTimeByClip.Clear();
            accepted = 0;
            culledSpacing = 0;
            culledCapacity = 0;
            rejected = 0;
        }

        private AudioSource AvailableSource()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (!sources[i].isPlaying)
                {
                    return sources[i];
                }
            }

            if (sources.Count >= Mathf.Max(0, maxActiveSources))
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

        private bool ShouldCullForSpacing(AudioClip clip, float now)
        {
            return sameSoundStartSpacingSeconds > 0f
                && lastStartTimeByClip.TryGetValue(clip, out float lastStart)
                && now - lastStart < sameSoundStartSpacingSeconds;
        }

        private int CurrentPlayingCountNoPrune(AudioClip clip)
        {
            int count = 0;
            for (int i = 0; i < activeSounds.Count; i++)
            {
                if (activeSounds[i].Clip == clip)
                {
                    count++;
                }
            }

            return count;
        }

        private void PruneInactive()
        {
            for (int i = activeSounds.Count - 1; i >= 0; i--)
            {
                ActiveSound sound = activeSounds[i];
                if (!sound.Source.isPlaying || Time.time >= sound.EndTime)
                {
                    sound.Source.clip = null;
                    activeSounds.RemoveAt(i);
                }
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
