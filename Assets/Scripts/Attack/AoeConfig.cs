using PlayGround.System.Aoe;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/AOE Config", fileName = "AoeConfig")]
    public class AoeConfig : ScriptableObject
    {
        [SerializeField] private BasicAoePrefab basicPrefab;
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1f;
        [SerializeField, Min(0)] private int preloadCount;
        [SerializeField] private float damage = 1f;
        [SerializeField, Min(1)] private int count = 1;
        [SerializeField] private bool spawnAtAimPosition;
        [SerializeField] private int targetMask = 1;

        public BasicAoePrefab Prefab => basicPrefab;
        public GameObject VisualPrefab => basicPrefab != null ? basicPrefab.gameObject : null;
        public Collider2D CollisionShape => basicPrefab != null ? basicPrefab.Hurtbox : null;
        public float SizeMultiplier => sizeMultiplier;
        public int PreloadCount => preloadCount;
        public float Damage => damage;
        public virtual float LifetimeSeconds => 0f;
        public virtual float TickIntervalSeconds => 0f;
        public int Count => count;
        public bool SpawnAtAimPosition => spawnAtAimPosition;
        public int TargetMask => targetMask;

        public void Configure(BasicAoePrefab prefab)
        {
            basicPrefab = prefab;
        }

        public AoeTypeDefinition CreateTypeDefinition()
        {
            var definition = new AoeTypeDefinition();
            definition.Configure(
                VisualPrefab,
                CollisionShape,
                sizeMultiplier,
                basicPrefab != null ? basicPrefab.VisualRotationDegrees : 0f,
                preloadCount,
                basicPrefab != null ? basicPrefab.SpawnEffect : null,
                basicPrefab != null ? basicPrefab.HitEffect : null,
                basicPrefab != null ? basicPrefab.ExpireEffect : null,
                PulseEffect);
            return definition;
        }

        protected virtual VisualEffectAsset PulseEffect => null;

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
