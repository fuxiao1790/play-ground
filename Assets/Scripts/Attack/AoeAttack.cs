using PlayGround.Audio;
using PlayGround.Common;
using PlayGround.System.Aoe;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class AoeAttack : MonoBehaviour
    {
        [SerializeField] private AoeRoot aoeRoot;
        [SerializeField] private AoeConfig config;
        [SerializeField] private float recoverySeconds = 0.25f;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioManager audioManager;

        private float cooldownRemaining;
        private int deterministicSeed;
        private int registeredTypeId = -1;

        public bool IsReady => cooldownRemaining <= 0f;

        private void Awake()
        {
            if (aoeRoot == null)
            {
                throw new MissingReferenceException($"{nameof(AoeAttack)} on {name} needs an AOE root.");
            }

            ValidateConfig();
            audioManager ??= AudioManager.Instance != null ? AudioManager.Instance : FindAnyObjectByType<AudioManager>();
            deterministicSeed = gameObject.GetHashCode();
            RegisterAoeType();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Configure(AoeRoot root)
        {
            aoeRoot = root;
            RegisterAoeType();
        }

        public void Tick(float deltaTime)
        {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        }

        public bool TryFire(Vector2 aimWorldPosition)
        {
            if (!IsReady)
            {
                return false;
            }

            Vector2 center = SpawnCenter(aimWorldPosition);
            int count = Mathf.Max(1, config.Count);
            DamageSnapshot damageSnapshot = new(Mathf.Max(0f, config.Damage));
            for (int i = 0; i < count; i++)
            {
                aoeRoot.Spawn(new AoeSpawnCommand(
                    registeredTypeId,
                    SpawnPosition(center, i, count),
                    EffectiveTargetMask(),
                    damageSnapshot,
                    config.LifetimeSeconds,
                    config.TickIntervalSeconds));
            }

            PlayPerformSound(center);
            cooldownRemaining = Mathf.Max(0.01f, recoverySeconds);
            return true;
        }

        private Vector2 SpawnCenter(Vector2 aimWorldPosition)
        {
            if (config.SpawnAtAimPosition)
            {
                return aimWorldPosition;
            }

            return config.SpawnAtOwnerPosition ? transform.root.position : (Vector2)transform.position;
        }

        private Vector2 SpawnPosition(Vector2 center, int index, int count)
        {
            float radius = Mathf.Max(0f, config.BurstRadius);
            if (count <= 1 || radius <= 0f)
            {
                return center;
            }

            if (config.RandomizePositions)
            {
                float angle = Mathf.Repeat(Hash01(index, 0) * Mathf.PI * 2f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt(Hash01(index, 1)) * radius;
                return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            }

            float ringAngle = Mathf.PI * 2f * index / count;
            float ringRadius = Mathf.Sqrt((index + 0.5f) / count) * radius;
            return center + new Vector2(Mathf.Cos(ringAngle), Mathf.Sin(ringAngle)) * ringRadius;
        }

        private void ValidateConfig()
        {
            if (config == null)
                throw new MissingReferenceException($"{nameof(AoeAttack)} on {name} needs an {nameof(AoeConfig)}.");

            if (!config.IsValidConfig(out string reason))
                throw new MissingReferenceException($"{nameof(AoeAttack)} on {name} has invalid {nameof(AoeConfig)} '{config.name}': {reason}.");
        }

        private void RegisterAoeType()
        {
            if (aoeRoot == null || config == null) return;
            registeredTypeId = aoeRoot.RegisterConfig(config);
        }

        private int EffectiveTargetMask()
        {
            return config.TargetMask != 1 || aoeRoot == null ? config.TargetMask : aoeRoot.TargetMask;
        }

        private float Hash01(int index, int salt)
        {
            uint hash = (uint)(deterministicSeed + (index * 397) + (salt * 104729));
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }

        private void PlayPerformSound(Vector2 worldPosition)
        {
            if (performSound == null || audioManager == null)
            {
                return;
            }

            audioManager.PlaySound(performSound, worldPosition);
        }
    }
}
