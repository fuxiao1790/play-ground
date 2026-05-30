using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    // Assign parentAttack and childAttack by dragging ProjectileAttack prefabs into the inspector.
    // All projectile settings (speed, lifetime, shape, tracking, pierce, damage) are authored on
    // those prefabs. This component only holds the spawn schedule (count, interval, spread).
    public sealed class ChildSpawningProjectileAttack : MonoBehaviour
    {
        private static int nextChildSpawnerId;

        [SerializeField] private ProjectileAttack parentAttack;
        [SerializeField] private ProjectileAttack childAttack;

        [SerializeField] private int childProjectileCount;
        [SerializeField] private float childSpawnIntervalSeconds;
        [SerializeField] private float childSpawnIntervalJitterSeconds;
        [SerializeField, Range(0f, 180f)] private float childSideSpreadDegrees = 30f;

        private int childSpawnerId;
        private ProjectileRoot subscribedRoot;

        public bool IsReady => parentAttack != null && parentAttack.IsReady;

        public bool TryFire(Vector2 aimDirection) => parentAttack != null && parentAttack.TryFire(aimDirection);

        public void ConfigureAoeRoot(AoeRoot root)
        {
            if (parentAttack != null)
                parentAttack.ConfigureAoeRoot(root);
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void OnEnable()
        {
            // All Awakes have completed, so parentAttack.Root is guaranteed set.
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
            if (childAttack == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: childAttack is not assigned.");
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
                parentAttack.SetChildConfig(ProjectileChildSpawnConfig.Disabled);
                return;
            }

            if (childAttack.Prefab != null)
                parentAttack.Root.RegisterTemplate(childAttack.Prefab);

            parentAttack.SetChildConfig(BuildChildConfig());
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
            BasicAttackPrefab prefab = childAttack.Prefab;
            int typeId = parentAttack.Root.RegisterTemplate(prefab);

            return new ProjectileChildSpawnConfig(
                childSpawnerId,
                typeId,
                Mathf.Max(0.01f, childSpawnIntervalSeconds),
                childSpawnIntervalJitterSeconds,
                childAttack.Speed,
                childAttack.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.ShapeType,
                prefab.RotationRadians,
                new DamageSnapshot(childAttack.Damage),
                childAttack.ActiveTargetMask,
                childAttack.DirectDamageEnabled,
                childAttack.PierceCount,
                childAttack.RepeatHitCooldown,
                prefab.VisualScale,
                prefab.VisualRotationDegrees,
                childAttack.GetTrackingConfig(),
                new ProjectileChildSpawnBehavior(childProjectileCount, ProjectileChildSpawnPatternType.SideSpray, childSideSpreadDegrees));
        }
    }
}
