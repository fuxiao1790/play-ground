using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    // Required prefab structure:
    //
    //   SampleChildSpawningAttack   ← this component  [DefaultExecutionOrder(100)]
    //     ParentProjectile          ← ProjectileAttack (fires and owns child spawn config)
    //     ChildProjectile           ← ProjectileAttack (config source; deactivated at runtime)
    //
    // Both child nodes must be direct children of this GameObject.
    // All projectile settings live on the two ProjectileAttack children.
    // This component only holds the spawn schedule (count, interval, spread).
    //
    // [DefaultExecutionOrder(100)] ensures both child ProjectileAttack.Awake() calls
    // (order 0) complete before this Awake runs. ChildProjectile is then deactivated
    // so GetComponentsInChildren does not expose it to PlayerAttackLoadout.
    [DefaultExecutionOrder(100)]
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

        public bool IsReady => parentAttack.IsReady;

        public bool TryFire(Vector2 aimDirection) => parentAttack.TryFire(aimDirection);

        public void ConfigureAoeRoot(AoeRoot root) => parentAttack.ConfigureAoeRoot(root);

        private void Awake()
        {
            AutoWireChildren();
            ValidateReferences();
            // child ProjectileAttack.Awake() has already run (order 0 < 100).
            // Deactivate ChildProjectile before the OnEnable phase so it is never
            // discovered by GetComponentsInChildren or subscribed to root events.
            childAttack.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // All Awakes completed; parentAttack.Root is guaranteed set.
            AssignChildSpawnerId();
            InjectChildConfig();
            SubscribeRoot();
        }

        private void OnDisable()
        {
            UnsubscribeRoot();
        }

        // --- private ---

        private void AutoWireChildren()
        {
            if (parentAttack == null)
            {
                Transform t = transform.Find("ParentProjectile");
                if (t != null) parentAttack = t.GetComponent<ProjectileAttack>();
            }

            if (childAttack == null)
            {
                Transform t = transform.Find("ChildProjectile");
                if (t != null) childAttack = t.GetComponent<ProjectileAttack>();
            }
        }

        private void ValidateReferences()
        {
            if (parentAttack == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: missing ParentProjectile child with ProjectileAttack.");
            if (childAttack == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: missing ChildProjectile child with ProjectileAttack.");
            if (parentAttack.transform.parent != transform)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: ParentProjectile must be a direct child of this GameObject.");
            if (childAttack.transform.parent != transform)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: ChildProjectile must be a direct child of this GameObject.");
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
