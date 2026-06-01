using PlayGround.System.Aoe;
using UnityEngine;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/AOE Config", fileName = "AoeConfig")]
    public sealed class AoeConfig : ScriptableObject
    {
        [SerializeField] private int typeId;
        [SerializeField] private BasicAoePrefab basicPrefab;
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1f;
        [SerializeField, Min(0)] private int preloadCount;
        [SerializeField] private float damage = 1f;
        [SerializeField] private float lifetimeSeconds;
        [SerializeField] private float tickIntervalSeconds;
        [SerializeField, Min(1)] private int count = 1;
        [SerializeField] private bool spawnAtAimPosition;
        [SerializeField] private bool spawnAtOwnerPosition = true;
        [SerializeField] private float burstRadius;
        [SerializeField] private bool randomizePositions;
        [SerializeField] private int targetMask = 1;

        public int TypeId => typeId;
        public BasicAoePrefab Prefab => basicPrefab;
        public GameObject VisualPrefab => basicPrefab != null ? basicPrefab.gameObject : null;
        public Collider2D CollisionShape => basicPrefab != null ? basicPrefab.Hurtbox : null;
        public float SizeMultiplier => sizeMultiplier;
        public int PreloadCount => preloadCount;
        public float Damage => damage;
        public float LifetimeSeconds => lifetimeSeconds;
        public float TickIntervalSeconds => tickIntervalSeconds;
        public int Count => count;
        public bool SpawnAtAimPosition => spawnAtAimPosition;
        public bool SpawnAtOwnerPosition => spawnAtOwnerPosition;
        public float BurstRadius => burstRadius;
        public bool RandomizePositions => randomizePositions;
        public int TargetMask => targetMask;

        public AoeTypeDefinition CreateTypeDefinition()
        {
            var definition = new AoeTypeDefinition();
            definition.Configure(
                typeId,
                VisualPrefab,
                CollisionShape,
                sizeMultiplier,
                basicPrefab != null ? basicPrefab.VisualRotationDegrees : 0f,
                preloadCount);
            return definition;
        }

        public bool IsValidConfig(out string reason)
        {
            if (basicPrefab == null)
            {
                reason = "basicPrefab is not assigned";
                return false;
            }

            return basicPrefab.IsValidTemplate(out reason);
        }
    }
}
