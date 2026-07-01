using System;
using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.Spawn
{
    public sealed class MobSpawnerRoot : MonoBehaviour
    {
        [SerializeField] private MobSpawnPool fallbackPool;
        [SerializeField, Min(0)] private int maxMobs = 20;
        [SerializeField] private SpawnCoordinator coordinator;
        [SerializeField] private SpawnPoint[] spawnPoints = Array.Empty<SpawnPoint>();
        [SerializeField] private Transform spawnParent;
        [SerializeField] private Transform target;
        [SerializeField] private CombatRoot combatRoot;
        [SerializeField] private Sprite runtimeMobSprite;
        [SerializeField] private int randomSeed;

        private readonly List<MobRoot> spawnedMobs = new();
        private readonly HashSet<MobRoot> softDeadMobs = new();
        private readonly Dictionary<MobRoot, SpawnPoint> spawnPointByMob = new();
        private global::System.Random random;
        private bool active = true;
        private int activeMobCount;

        public int MaxMobs => maxMobs;
        public bool IsActive => active;

        private void Awake()
        {
            random = randomSeed == 0 ? new global::System.Random() : new global::System.Random(randomSeed);
            combatRoot ??= FindTaggedCombatRoot(GameplayTags.PlayerProjectileRoot);
            if (fallbackPool == null)
            {
                fallbackPool = CreateRuntimePool();
            }

            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                spawnPoints = GetComponentsInChildren<SpawnPoint>(includeInactive: true);
            }

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null)
                {
                    throw new MissingReferenceException($"{nameof(MobSpawnerRoot)} on {name} spawn point slot {i} is empty.");
                }

                spawnPoints[i].Bind(this);
            }
        }

        public void Configure(
            MobSpawnPool pool,
            int mobCap,
            Transform targetTransform = null,
            CombatRoot combat = null,
            SpawnPoint[] points = null)
        {
            fallbackPool = pool;
            maxMobs = Mathf.Max(0, mobCap);
            target = targetTransform;
            combatRoot = combat;
            spawnPoints = points ?? spawnPoints;
        }

        public void ConfigureRuntimeVisual(Sprite mobSprite)
        {
            runtimeMobSprite = mobSprite;
        }

        public void BindCombatRoot(CombatRoot root)
        {
            combatRoot = root;
            for (int i = 0; i < spawnedMobs.Count; i++)
            {
                MobRoot mob = spawnedMobs[i];
                if (mob == null)
                {
                    continue;
                }

                if (combatRoot != null)
                {
                    mob.Register(combatRoot.TargetRegistry);
                    mob.BindCombatRoot(combatRoot);
                }
            }
        }

        public void BindSpawnPointForRuntime(SpawnPoint spawnPoint)
        {
            spawnPoint.Bind(this);
        }

        public bool CanSpawn(SpawnPoint spawnPoint)
        {
            return active
                && activeMobCount < maxMobs
                && (coordinator == null || coordinator.CanSpawn(spawnPoint))
                && (spawnPoint == null || spawnPoint.CanSpawnLocal());
        }

        public int ActiveMobCount()
        {
            return activeMobCount;
        }

        public MobRoot RequestSpawn(SpawnPoint spawnPoint)
        {
            if (spawnPoint == null)
            {
                throw new ArgumentNullException(nameof(spawnPoint));
            }

            if (!CanSpawn(spawnPoint))
            {
                return null;
            }

            MobSpawnPool pool = spawnPoint.PoolOrFallback(fallbackPool);
            MobRoot prefab = pool != null ? pool.ChoosePrefab(random) : null;
            if (prefab == null)
            {
                Debug.LogError($"No mob prefab available for spawn point '{spawnPoint.name}'.", spawnPoint);
                return null;
            }

            if (!spawnPoint.TryFindSpawnPosition(out Vector2 position))
            {
                return null;
            }

            Transform parent = spawnParent != null ? spawnParent : transform.parent;
            MobRoot mob = Instantiate(prefab, position, Quaternion.identity, parent);
            mob.gameObject.SetActive(true);
            RegisterMob(spawnPoint, mob);
            spawnPoint.RegisterMob(mob);
            return mob;
        }

        public void DespawnAll()
        {
            for (int i = spawnedMobs.Count - 1; i >= 0; i--)
            {
                MobRoot mob = spawnedMobs[i];
                if (mob != null)
                {
                    if (spawnPointByMob.TryGetValue(mob, out SpawnPoint spawnPoint) && spawnPoint != null)
                    {
                        spawnPoint.ReleaseMob(mob);
                    }

                    Destroy(mob.gameObject);
                }
            }

            spawnedMobs.Clear();
            softDeadMobs.Clear();
            spawnPointByMob.Clear();
            activeMobCount = 0;
        }

        public void Stop()
        {
            active = false;
        }

        public void StartSpawning()
        {
            active = true;
        }

        private void RegisterMob(SpawnPoint spawnPoint, MobRoot mob)
        {
            spawnedMobs.Add(mob);
            spawnPointByMob[mob] = spawnPoint;
            activeMobCount++;
            mob.SoftDied += OnMobSoftDied;
            SpawnedMobLifetime lifetime = mob.gameObject.AddComponent<SpawnedMobLifetime>();
            lifetime.Initialize(mob, OnMobDestroyed);
            if (target != null)
            {
                mob.SetTarget(target);
            }

            if (combatRoot != null)
            {
                mob.Register(combatRoot.TargetRegistry);
                mob.BindCombatRoot(combatRoot);
            }

            coordinator?.OnSpawned(spawnPoint, mob);
        }

        private void OnMobSoftDied(MobRoot mob)
        {
            if (!spawnedMobs.Contains(mob) || !softDeadMobs.Add(mob))
            {
                return;
            }

            activeMobCount = Mathf.Max(0, activeMobCount - 1);
            spawnPointByMob.Remove(mob);
            coordinator?.OnDespawned(mob);
        }

        private void OnMobDestroyed(MobRoot mob)
        {
            if (mob != null)
            {
                mob.SoftDied -= OnMobSoftDied;
            }

            bool wasTracked = spawnedMobs.Remove(mob);
            bool wasSoftDead = softDeadMobs.Remove(mob);
            if (spawnPointByMob.Remove(mob, out SpawnPoint spawnPoint) && spawnPoint != null)
            {
                spawnPoint.ReleaseMob(mob);
            }

            if (wasTracked && !wasSoftDead)
            {
                activeMobCount = Mathf.Max(0, activeMobCount - 1);
                coordinator?.OnDespawned(mob);
            }
        }

        private MobSpawnPool CreateRuntimePool()
        {
            MobSpawnPool pool = ScriptableObject.CreateInstance<MobSpawnPool>();
            pool.Configure(new[] { CreateRuntimeMobPrefab() });
            return pool;
        }

        private MobRoot CreateRuntimeMobPrefab()
        {
            GameObject mobObject = new("RuntimeMobPrefab");
            mobObject.SetActive(false);
            mobObject.layer = OptionalLayer("MobBody");

            Rigidbody2D body = mobObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;

            CircleCollider2D bodyCollider = mobObject.AddComponent<CircleCollider2D>();
            bodyCollider.radius = 0.5f;

            GameObject hurtboxObject = new("Hurtbox");
            hurtboxObject.layer = OptionalLayer("MobHurtbox");
            hurtboxObject.transform.SetParent(mobObject.transform, false);
            CircleCollider2D hurtbox = hurtboxObject.AddComponent<CircleCollider2D>();
            hurtbox.radius = 0.5f;
            hurtbox.isTrigger = true;

            GameObject visual = new("Visual");
            visual.transform.SetParent(mobObject.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = runtimeMobSprite != null
                ? runtimeMobSprite
                : Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            renderer.color = runtimeMobSprite != null
                ? Color.white
                : new Color(0.9f, 0.25f, 0.2f, 1f);
            renderer.sortingOrder = 9;
            if (runtimeMobSprite == null)
            {
                visual.transform.localScale = Vector3.one * 64f;
            }

            MobRoot root = mobObject.AddComponent<MobRoot>();
            root.Configure(body, bodyCollider, hurtbox, renderer, target);
            root.ConfigureAuthoring(35f, 2.5f, 0.5f);
            return root;
        }

        private static int OptionalLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : 0;
        }

        private static CombatRoot FindTaggedCombatRoot(string tag)
        {
            try
            {
                GameObject rootObject = GameObject.FindWithTag(tag);
                return rootObject != null ? rootObject.GetComponent<CombatRoot>() : null;
            }
            catch (UnityException)
            {
                return null;
            }
        }
    }

    internal sealed class SpawnedMobLifetime : MonoBehaviour
    {
        private Action<MobRoot> destroyed;
        private MobRoot mob;

        public void Initialize(MobRoot trackedMob, Action<MobRoot> onDestroyed)
        {
            mob = trackedMob;
            destroyed = onDestroyed;
        }

        private void OnDestroy()
        {
            destroyed?.Invoke(mob);
        }
    }
}
