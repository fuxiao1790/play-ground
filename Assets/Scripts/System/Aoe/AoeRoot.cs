using System.Collections.Generic;
using PlayGround.Attack;
using PlayGround.Common;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class AoeRoot : MonoBehaviour
    {
        private static readonly ProfilerMarker StepProfilerMarker = new("AoeRoot.Step");
        private static readonly ProfilerMarker DrainEventsProfilerMarker = new("AoeRoot.DrainEvents");

        [SerializeField] private AoeTypeDefinition[] aoeTypes = global::System.Array.Empty<AoeTypeDefinition>();
        [SerializeField] private int targetMask = 1;
        [SerializeField, Min(0)] private int maximumAoeCount = 10000;
        [SerializeField, Min(0)] private int maximumTargetCount = 100;
        [SerializeField] private bool spawnVisuals = true;

        private readonly AoeTargetRegistry targetRegistry = new();
        private readonly AoeWorld world = new();
        private readonly List<AoeHitEvent> pendingHits = new();
        private readonly List<AoeDespawnedEvent> pendingDespawns = new();
        private readonly Dictionary<int, PooledVisual> activeVisualsByAoeId = new();
        private readonly Dictionary<int, Stack<GameObject>> visualPoolsByType = new();

        private AoeTypeRegistry typeRegistry;
        private AoeTargetSync targetSync;
        private int spawnedAoes;
        private int despawnedAoes;
        private int hitEvents;

        public event global::System.Action<AoeHitContext> AoeHit;

        public AoeTargetRegistry TargetRegistry => targetRegistry;
        public AoeRuntimeCounters Counters => new(
            world.ActiveCount,
            spawnedAoes,
            despawnedAoes,
            hitEvents,
            activeVisualsByAoeId.Count);

        private void Awake()
        {
            world.MaximumAoeCount = maximumAoeCount;
            world.MaximumTargetCount = maximumTargetCount;
            typeRegistry = new AoeTypeRegistry(world);
            targetSync = new AoeTargetSync(targetRegistry);

            for (int i = 0; i < aoeTypes.Length; i++)
            {
                typeRegistry.Register(aoeTypes[i]);
                PreloadVisuals(aoeTypes[i]);
            }
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int, PooledVisual> pair in activeVisualsByAoeId)
            {
                if (pair.Value.Instance != null)
                {
                    Destroy(pair.Value.Instance);
                }
            }

            foreach (Stack<GameObject> pool in visualPoolsByType.Values)
            {
                while (pool.Count > 0)
                {
                    GameObject instance = pool.Pop();
                    if (instance != null)
                    {
                        Destroy(instance);
                    }
                }
            }
        }

        public void Configure(AoeTypeDefinition[] definitions, int mask)
        {
            aoeTypes = definitions ?? global::System.Array.Empty<AoeTypeDefinition>();
            targetMask = mask;
        }

        public int Spawn(ProjectileAoeSpawnRequest request)
        {
            return Spawn(new AoeSpawnCommand(
                request.EffectTypeId,
                request.Position,
                targetMask,
                request.Damage,
                request.LifetimeSeconds,
                request.TickIntervalSeconds));
        }

        public int Spawn(AoeSpawnCommand command)
        {
            int aoeId = world.SubmitSpawn(command);
            spawnedAoes++;
            SpawnVisual(aoeId, command);
            return aoeId;
        }

        public void Step(float deltaTime)
        {
            IReadOnlyList<AoeTargetSnapshot> snapshots = targetSync.Snapshot();
            world.SubmitTargets(snapshots);
            using (StepProfilerMarker.Auto())
            {
                world.Step(deltaTime);
            }

            using (DrainEventsProfilerMarker.Auto())
            {
                DrainEvents();
            }
        }

        private void DrainEvents()
        {
            world.DrainEvents(pendingHits, pendingDespawns);
            hitEvents += pendingHits.Count;
            despawnedAoes += pendingDespawns.Count;

            for (int i = 0; i < pendingHits.Count; i++)
            {
                AoeHitEvent hit = pendingHits[i];
                targetSync.TargetsById.TryGetValue(hit.TargetId, out IAoeTarget target);
                var context = new AoeHitContext(hit.AoeId, hit.TypeId, hit.TargetId, hit.Position, hit.Damage, target);
                AoeHit?.Invoke(context);
                target?.ReceiveAoeHit(hit.Damage);
            }

            for (int i = 0; i < pendingDespawns.Count; i++)
            {
                ReleaseVisual(pendingDespawns[i].AoeId);
            }

            pendingHits.Clear();
            pendingDespawns.Clear();
        }

        private void SpawnVisual(int aoeId, AoeSpawnCommand command)
        {
            if (!spawnVisuals
                || !typeRegistry.TryGetDefinition(command.TypeId, out AoeTypeDefinition definition)
                || definition.VisualPrefab == null)
            {
                return;
            }

            GameObject instance = GetVisualInstance(definition);
            instance.transform.SetPositionAndRotation(command.Position, Quaternion.identity);
            instance.SetActive(true);
            activeVisualsByAoeId[aoeId] = new PooledVisual(command.TypeId, instance);
        }

        private GameObject GetVisualInstance(AoeTypeDefinition definition)
        {
            Stack<GameObject> pool = PoolFor(definition.TypeId);
            if (pool.Count > 0)
            {
                return pool.Pop();
            }

            GameObject instance = Instantiate(definition.VisualPrefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void ReleaseVisual(int aoeId)
        {
            if (!activeVisualsByAoeId.TryGetValue(aoeId, out PooledVisual visual))
            {
                return;
            }

            activeVisualsByAoeId.Remove(aoeId);
            if (visual.Instance == null)
            {
                return;
            }

            visual.Instance.SetActive(false);
            visual.Instance.transform.SetParent(transform, false);
            PoolFor(visual.TypeId).Push(visual.Instance);
        }

        private void PreloadVisuals(AoeTypeDefinition definition)
        {
            if (!spawnVisuals || definition == null || definition.VisualPrefab == null)
            {
                return;
            }

            Stack<GameObject> pool = PoolFor(definition.TypeId);
            for (int i = 0; i < definition.PreloadCount; i++)
            {
                GameObject instance = Instantiate(definition.VisualPrefab, transform);
                instance.SetActive(false);
                pool.Push(instance);
            }
        }

        private Stack<GameObject> PoolFor(int typeId)
        {
            if (!visualPoolsByType.TryGetValue(typeId, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                visualPoolsByType.Add(typeId, pool);
            }

            return pool;
        }

        private readonly struct PooledVisual
        {
            public PooledVisual(int typeId, GameObject instance)
            {
                TypeId = typeId;
                Instance = instance;
            }

            public int TypeId { get; }
            public GameObject Instance { get; }
        }
    }
}
