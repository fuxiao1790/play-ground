using PlayGround.Audio;
using PlayGround.Common;
using PlayGround.System.Aoe;
using UnityEngine;

namespace PlayGround.Attack
{
    public class AoeAttack : MonoBehaviour
    {
        [SerializeField] private AoeRoot aoeRoot;
        [SerializeField] protected AoeConfig config;
        [SerializeField] private float recoverySeconds = 0.25f;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioManager audioManager;

        private float cooldownRemaining;
        private int registeredTypeId = -1;

        public bool IsReady => cooldownRemaining <= 0f;

        protected virtual float SpawnLifetimeSeconds => 0f;
        protected virtual float SpawnTickIntervalSeconds => 0f;

        private void Awake()
        {
            if (aoeRoot == null)
            {
                throw new MissingReferenceException($"{nameof(AoeAttack)} on {name} needs an AOE root.");
            }

            ValidateConfig();
            audioManager ??= AudioManager.Instance != null ? AudioManager.Instance : FindAnyObjectByType<AudioManager>();
        }

        private void OnEnable()
        {
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
                    center,
                    EffectiveTargetMask(),
                    damageSnapshot,
                    SpawnLifetimeSeconds,
                    SpawnTickIntervalSeconds));
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

            return transform.position;
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
