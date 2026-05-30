using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class ChildSpawningProjectileAttack : MonoBehaviour
    {
        private static int nextChildSpawnerId;

        [SerializeField] private ProjectileAttack parentAttack;
        [SerializeField] private ProjectileConfig childConfig;

        [SerializeField] private int childProjectileCount;
        [SerializeField] private float childSpawnIntervalSeconds;
        [SerializeField] private float childSpawnIntervalJitterSeconds;
        [SerializeField, Range(0f, 180f)] private float childSideSpreadDegrees = 30f;

        private int childSpawnerId;
        private ProjectileRoot subscribedRoot;
        private ProjectileChildSpawnConfig activeChildConfig = ProjectileChildSpawnConfig.Disabled;

        public bool IsReady => parentAttack.IsReady;

        public bool TryFire(Vector2 aimDirection) => parentAttack.TryFire(aimDirection, activeChildConfig);

        public void ConfigureAoeRoot(AoeRoot root) => parentAttack.ConfigureAoeRoot(root);

        private void Awake()
        {
            ValidateReferences();
        }

        private void OnEnable()
        {
            AssignChildSpawnerId();
            InjectChildConfig();
            SubscribeRoot();
        }

        private void OnDisable()
        {
            UnsubscribeRoot();
        }

        // --- private ---

        private void ValidateReferences()
        {
            if (parentAttack == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: parentAttack is not assigned.");
            if (childConfig == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: childConfig is not assigned.");
        }

        private void AssignChildSpawnerId()
        {
            if (childSpawnerId <= 0 && childProjectileCount > 0 && childSpawnIntervalSeconds > 0f)
                childSpawnerId = ++nextChildSpawnerId;
        }

        private void InjectChildConfig()
        {
            if (childProjectileCount <= 0 || childSpawnIntervalSeconds <= 0f)
            {
                activeChildConfig = ProjectileChildSpawnConfig.Disabled;
                return;
            }

            if (childConfig.Prefab != null)
                parentAttack.Root.RegisterTemplate(childConfig.Prefab);

            activeChildConfig = BuildChildConfig();
        }

        private void SubscribeRoot()
        {
            ProjectileRoot root = parentAttack.Root;
            if (root == null || subscribedRoot == root) return;

            root.ChildSpawnRequested += OnChildSpawnRequested;
            subscribedRoot = root;
        }

        private void UnsubscribeRoot()
        {
            if (subscribedRoot == null) return;

            subscribedRoot.ChildSpawnRequested -= OnChildSpawnRequested;
            subscribedRoot = null;
        }

        private void OnChildSpawnRequested(ProjectileChildSpawnRequest request)
        {
            if (request.ChildSpawnerId != childSpawnerId) return;
            if (!parentAttack.OwnsProjectile(request.ProjectileId)) return;

            parentAttack.TrackProjectileId(request.ChildProjectileId);
        }

        private ProjectileChildSpawnConfig BuildChildConfig()
        {
            BasicAttackPrefab prefab = childConfig.Prefab;
            int typeId = parentAttack.Root.RegisterTemplate(prefab);

            return new ProjectileChildSpawnConfig(
                childSpawnerId,
                typeId,
                Mathf.Max(0.01f, childSpawnIntervalSeconds),
                childSpawnIntervalJitterSeconds,
                childConfig.Speed,
                childConfig.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.ShapeType,
                prefab.RotationRadians,
                new DamageSnapshot(childConfig.Damage),
                childConfig.TargetMask,
                childConfig.DirectDamageEnabled,
                childConfig.PierceCount,
                childConfig.RepeatHitCooldown,
                prefab.VisualScale,
                prefab.VisualRotationDegrees,
                childConfig.GetTrackingConfig(),
                new ProjectileChildSpawnBehavior(childProjectileCount, ProjectileChildSpawnPatternType.SideSpray, childSideSpreadDegrees));
        }
    }
}
