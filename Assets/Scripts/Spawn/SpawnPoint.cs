using System.Collections.Generic;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Spawn
{
    public sealed class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private MobSpawnPool pool;
        [SerializeField] private SpawnConfig config;
        [SerializeField, Min(0.05f)] private float spawnInterval = 2f;
        [SerializeField] private bool autostart = true;
        [SerializeField, Min(0f)] private float spawnRadius = 16f;
        [SerializeField] private Vector2 spawnOrigin;
        [SerializeField, Min(0f)] private float mobClearanceRadius = 0.5f;
        [SerializeField, Min(0)] private int maxLocalMobs;
        [SerializeField] private bool useConfigTiming = true;

        private readonly List<MobRoot> localMobs = new();
        private MobSpawnerRoot root;
        private float timer;
        private bool running;

        public int ActiveLocalMobCount
        {
            get
            {
                CleanupLocal();
                return localMobs.Count;
            }
        }

        private float EffectiveSpawnInterval => useConfigTiming && config != null ? config.SpawnInterval : spawnInterval;
        private float EffectiveSpawnRadius => useConfigTiming && config != null ? config.SpawnRadius : spawnRadius;
        private float EffectiveMobClearanceRadius => useConfigTiming && config != null ? config.MobClearanceRadius : mobClearanceRadius;
        private int EffectiveMaxLocalMobs => useConfigTiming && config != null ? config.MaxLocalMobs : maxLocalMobs;

        private void Awake()
        {
            if (root == null)
            {
                root = GetComponentInParent<MobSpawnerRoot>();
            }

            if (root == null)
            {
                throw new MissingReferenceException($"{nameof(SpawnPoint)} on {name} needs a parent {nameof(MobSpawnerRoot)}.");
            }

            root.BindSpawnPointForRuntime(this);
        }

        private void OnEnable()
        {
            running = useConfigTiming && config != null ? config.Autostart : autostart;
            timer = EffectiveSpawnInterval;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Configure(MobSpawnPool spawnPool, float interval, float radius, int localCap = 0)
        {
            pool = spawnPool;
            spawnInterval = Mathf.Max(0.05f, interval);
            spawnRadius = Mathf.Max(0f, radius);
            maxLocalMobs = Mathf.Max(0, localCap);
            useConfigTiming = false;
        }

        public void Bind(MobSpawnerRoot spawnerRoot)
        {
            root = spawnerRoot;
        }

        public void Tick(float deltaTime)
        {
            if (!running)
            {
                return;
            }

            timer -= deltaTime;
            if (timer > 0f)
            {
                return;
            }

            timer += EffectiveSpawnInterval;
            root.RequestSpawn(this);
        }

        public void RegisterMob(MobRoot mob)
        {
            localMobs.Add(mob);
            mob.SoftDied += OnLocalMobSoftDied;
        }

        public bool CanSpawnLocal()
        {
            int cap = EffectiveMaxLocalMobs;
            return cap <= 0 || ActiveLocalMobCount < cap;
        }

        public MobSpawnPool PoolOrFallback(MobSpawnPool fallback)
        {
            if (pool != null && pool.HasEntries)
            {
                return pool;
            }

            if (config != null && config.Pool != null && config.Pool.HasEntries)
            {
                return config.Pool;
            }

            return fallback;
        }

        public bool TryFindSpawnPosition(out Vector2 position)
        {
            CleanupLocal();
            Vector2 origin = spawnOrigin == Vector2.zero ? (Vector2)transform.position : spawnOrigin;
            float searchRadius = Mathf.Max(EffectiveSpawnRadius, EffectiveMobClearanceRadius);
            float minSeparation = EffectiveMobClearanceRadius * 2f;

            for (int i = 0; i < 10; i++)
            {
                Vector2 candidate = origin + Random.insideUnitCircle * searchRadius;
                if (IsFree(candidate, minSeparation))
                {
                    position = candidate;
                    return true;
                }
            }

            position = origin;
            return false;
        }

        public void StartSpawning()
        {
            running = true;
        }

        public void StopSpawning()
        {
            running = false;
        }

        private bool IsFree(Vector2 candidate, float minSeparation)
        {
            for (int i = 0; i < localMobs.Count; i++)
            {
                MobRoot mob = localMobs[i];
                if (mob != null && mob.IsProjectileTargetActive)
                {
                    float distance = Vector2.Distance(mob.transform.position, candidate);
                    if (distance < minSeparation)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void OnLocalMobSoftDied(MobRoot mob)
        {
            mob.SoftDied -= OnLocalMobSoftDied;
            localMobs.Remove(mob);
        }

        private void CleanupLocal()
        {
            for (int i = localMobs.Count - 1; i >= 0; i--)
            {
                MobRoot mob = localMobs[i];
                if (mob != null && mob.IsProjectileTargetActive)
                {
                    continue;
                }

                if (mob != null)
                {
                    mob.SoftDied -= OnLocalMobSoftDied;
                }

                localMobs.RemoveAt(i);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 origin = spawnOrigin == Vector2.zero ? (Vector2)transform.position : spawnOrigin;
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(origin, EffectiveSpawnRadius);
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(origin, 0.08f);
        }
    }
}
