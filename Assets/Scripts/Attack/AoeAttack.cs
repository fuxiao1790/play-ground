using PlayGround.Common;
using PlayGround.System.Aoe;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class AoeAttack : MonoBehaviour
    {
        [SerializeField] private AoeRoot aoeRoot;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private int aoeTypeId;
        [SerializeField] private float recoverySeconds = 0.25f;
        [SerializeField] private float damage = 1f;
        [SerializeField] private float lifetimeSeconds;
        [SerializeField] private float tickIntervalSeconds;
        [SerializeField, Min(1)] private int aoeCount = 1;
        [SerializeField] private bool spawnAtAimPosition;
        [SerializeField] private bool spawnAtOwnerPosition = true;
        [SerializeField] private float burstRadius;
        [SerializeField] private bool randomizePositions;

        private float cooldownRemaining;
        private int deterministicSeed;

        public bool IsReady => cooldownRemaining <= 0f;

        private void Awake()
        {
            if (aoeRoot == null)
            {
                throw new MissingReferenceException($"{nameof(AoeAttack)} on {name} needs an AOE root.");
            }

            deterministicSeed = gameObject.GetHashCode();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Configure(AoeRoot root)
        {
            aoeRoot = root;
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
            int count = Mathf.Max(1, aoeCount);
            DamageSnapshot damageSnapshot = new(Mathf.Max(0f, damage));
            for (int i = 0; i < count; i++)
            {
                aoeRoot.Spawn(new AoeSpawnCommand(
                    aoeTypeId,
                    SpawnPosition(center, i, count),
                    1,
                    damageSnapshot,
                    lifetimeSeconds,
                    tickIntervalSeconds));
            }

            PlayPerformSound(center);
            cooldownRemaining = Mathf.Max(0.01f, recoverySeconds);
            return true;
        }

        private Vector2 SpawnCenter(Vector2 aimWorldPosition)
        {
            if (spawnAtAimPosition)
            {
                return aimWorldPosition;
            }

            return spawnAtOwnerPosition ? transform.root.position : (Vector2)transform.position;
        }

        private Vector2 SpawnPosition(Vector2 center, int index, int count)
        {
            float radius = Mathf.Max(0f, burstRadius);
            if (count <= 1 || radius <= 0f)
            {
                return center;
            }

            if (randomizePositions)
            {
                float angle = Mathf.Repeat(Hash01(index, 0) * Mathf.PI * 2f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt(Hash01(index, 1)) * radius;
                return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            }

            float ringAngle = Mathf.PI * 2f * index / count;
            float ringRadius = Mathf.Sqrt((index + 0.5f) / count) * radius;
            return center + new Vector2(Mathf.Cos(ringAngle), Mathf.Sin(ringAngle)) * ringRadius;
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
            if (performSound == null || audioSource == null)
            {
                return;
            }

            audioSource.transform.position = worldPosition;
            audioSource.PlayOneShot(performSound);
        }
    }
}
