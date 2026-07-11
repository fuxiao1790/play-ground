using System.Collections.Generic;
using PlayGround.Mob;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Vfx;
using UnityEngine;

namespace PlayGround.Spawn
{
    public sealed class SpawnController : MonoBehaviour, ISpawnSink
    {
        [SerializeField] private MobSpawnTable table;
        [SerializeField] private SpawnBehaviour behaviour;
        [SerializeField] private SpawnPlacement placement;
        [SerializeField] private SpawnPoint[] spawnPoints;
        [SerializeField, Min(0)] private int cap = 300;
        [SerializeField] private CombatRoot combatRoot;
        [SerializeField] private CombatVfxRoot vfxRoot;
        [SerializeField] private Transform target;
        [SerializeField, Min(0)] private int prewarm;
        [SerializeField] private int randomSeed;

        private readonly HashSet<MobRoot> pendingReclaim = new();
        private MobPool pool;
        private SpawnBehaviourRuntime behaviourRuntime;
        private global::System.Random rng;

        public int ActiveCount { get; private set; }
        public int Cap => cap;
        public bool CanSpawn => isActiveAndEnabled && combatRoot != null && ActiveCount < cap;

        private void Awake()
        {
            ValidateReferences();
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                spawnPoints = GetComponentsInChildren<SpawnPoint>(true);
            }

            GameObject poolRootObject = new($"{nameof(SpawnController)} Pool");
            poolRootObject.SetActive(false);
            poolRootObject.transform.SetParent(transform, false);

            pool = new MobPool(poolRootObject.transform);
            rng = randomSeed == 0 ? new global::System.Random() : new global::System.Random(randomSeed);
            behaviourRuntime = behaviour.CreateRuntime();
            pool.Prewarm(table, Mathf.Max(prewarm, cap));
        }

        private void Update()
        {
            if (combatRoot == null)
            {
                return;
            }

            behaviourRuntime.Tick(this, Time.deltaTime);
            ReclaimDead();
        }

        private void OnValidate()
        {
            cap = Mathf.Max(0, cap);
            prewarm = Mathf.Max(0, prewarm);
        }

        public void Bind(CombatRoot root, Transform targetTransform)
        {
            if (combatRoot == null)
            {
                combatRoot = root;
            }

            if (target == null)
            {
                target = targetTransform;
            }
        }

        public void Spawn()
        {
            if (!CanSpawn)
            {
                return;
            }

            MobRoot prefab = table.ChoosePrefab(rng);
            if (prefab == null)
            {
                return;
            }

            var context = new SpawnContext(spawnPoints, target, rng);
            if (!placement.TryResolve(context, out Vector2 position))
            {
                return;
            }

            MobRoot mob = pool.Rent(prefab, position);
            if (mob == null)
            {
                return;
            }

            WireMob(mob);
            ActiveCount++;
        }

        private void WireMob(MobRoot mob)
        {
            mob.Register(combatRoot.TargetRegistry);
            mob.BindCombatRoot(combatRoot);
            if (vfxRoot != null)
            {
                mob.BindVfxRoot(vfxRoot);
            }

            if (target != null)
            {
                mob.SetTarget(target);
            }

            mob.SoftDied -= OnMobSoftDied;
            mob.SoftDied += OnMobSoftDied;
        }

        private void OnMobSoftDied(MobRoot mob)
        {
            if (mob == null)
            {
                return;
            }

            mob.SoftDied -= OnMobSoftDied;
            pendingReclaim.Add(mob);
        }

        private void ReclaimDead()
        {
            if (pendingReclaim.Count == 0)
            {
                return;
            }

            foreach (MobRoot mob in pendingReclaim)
            {
                pool.Return(mob);
                ActiveCount = Mathf.Max(0, ActiveCount - 1);
            }

            pendingReclaim.Clear();
        }

        private void ValidateReferences()
        {
            if (table == null)
            {
                throw new MissingReferenceException($"{nameof(SpawnController)} on {name} needs a {nameof(MobSpawnTable)}.");
            }

            if (behaviour == null)
            {
                throw new MissingReferenceException($"{nameof(SpawnController)} on {name} needs a {nameof(SpawnBehaviour)}.");
            }

            if (placement == null)
            {
                throw new MissingReferenceException($"{nameof(SpawnController)} on {name} needs a {nameof(SpawnPlacement)}.");
            }
        }
    }
}
