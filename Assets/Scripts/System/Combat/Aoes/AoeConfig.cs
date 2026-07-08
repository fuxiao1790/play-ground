using PlayGround.Skills;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Aoes
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/AOE Config", fileName = "AoeConfig")]
    public class AoeConfig : ScriptableObject
    {
        [SerializeField] private BasicAoePrefab basicPrefab;
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSize = 1f;
        [SerializeField, Min(0)] private int preloadCount;
        [SerializeField] private float damage = 1f;
        // Legacy config-only value; not AOE echo multiplicity.
        [SerializeField, Min(1)] private int count = 1;
        [SerializeField] private bool spawnAtAimPosition;

        public BasicAoePrefab Prefab => basicPrefab;
        public GameObject VisualPrefab => basicPrefab != null ? basicPrefab.gameObject : null;
        public Collider2D CollisionShape => basicPrefab != null ? basicPrefab.Hurtbox : null;
        public float AreaSize => areaSize;
        public int PreloadCount => preloadCount;
        public float Damage => damage;
        public virtual float LifetimeSeconds => 0f;
        public virtual float TickIntervalSeconds => 0f;
        public int Count => count;
        public bool SpawnAtAimPosition => spawnAtAimPosition;

        public void Configure(BasicAoePrefab prefab)
        {
            basicPrefab = prefab;
        }

        public AoeSpawnGeometry CreateSpawnGeometry()
        {
            return AoeSpawnGeometry.FromTemplate(
                VisualPrefab,
                CollisionShape,
                areaSize,
                basicPrefab != null ? basicPrefab.VisualRotationDegrees : 0f);
        }

        public virtual AoeTypeDefinition CreateTypeDefinition()
        {
            var definition = new AoeTypeDefinition();
            definition.Configure(
                VisualPrefab,
                CollisionShape,
                basicPrefab != null ? basicPrefab.VisualRotationDegrees : 0f,
                preloadCount,
                basicPrefab != null ? basicPrefab.SpawnEffect : null,
                basicPrefab != null ? basicPrefab.HitEffect : null,
                basicPrefab != null ? basicPrefab.ExpireEffect : null,
                PulseEffect,
                basicPrefab != null ? basicPrefab.ArmingEffect : null);
            return definition;
        }

        protected virtual VisualEffectAsset PulseEffect => null;

        public virtual bool IsValidConfig(out string reason)
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
