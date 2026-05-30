using System.Collections.Generic;
using PlayGround.Audio;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class ProjectileAttack : MonoBehaviour
    {
        [SerializeField] private ProjectileRoot projectileRoot;
        [SerializeField] private float recoverySeconds = 0.15f;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioManager audioManager;
        [SerializeField] private AoeRoot aoeRoot;
        [SerializeField] private BasicAttackPrefab basicPrefab;
        [SerializeField, HideInInspector] private int projectileTypeId;
        [SerializeField] private float projectileSpeed = 16f;
        [SerializeField] private float projectileLifetime = 1.5f;
        [SerializeField] private float projectileDamage = 10f;
        [SerializeField] private int projectileCount = 1;
        [SerializeField] private float spreadDegrees;
        [SerializeField] private float jitterDegrees;
        [SerializeField] private int targetMask = 1;
        [SerializeField] private int pierceCount;
        [SerializeField] private float repeatHitCooldownSeconds;
        [SerializeField] private bool trackingEnabled;
        [SerializeField] private float trackingRange;
        [SerializeField] private float trackingTurnSpeedDegrees;
        [SerializeField] private float trackingQueryIntervalSeconds;
        [SerializeField] private float trackingInitialQueryDelaySeconds;
        [SerializeField] private bool projectileDirectDamageEnabled = true;
        [SerializeField] private int impactAoeTypeId = -1;
        [SerializeField] private float impactAoeDamage = 1f;
        [SerializeField] private float impactAoeLifetimeSeconds;
        [SerializeField] private float impactAoeTickIntervalSeconds;

        private readonly List<ProjectileSpawnCommand> commands = new();
        private readonly HashSet<int> ownedProjectileIds = new();
        private ProjectileHitEffect[] hitEffects = global::System.Array.Empty<ProjectileHitEffect>();
        private ProjectileRoot subscribedRoot;
        private AoeRoot subscribedAoeRoot;
        private float cooldownRemaining;
        private ProjectileChildSpawnConfig activeChildConfig = ProjectileChildSpawnConfig.Disabled;

        public event global::System.Action<ProjectileAoeSpawnRequest> AoeSpawnRequested;

        // --- surface for ChildSpawningProjectileAttack ---
        public ProjectileRoot Root => projectileRoot;
        public BasicAttackPrefab Prefab => basicPrefab;
        public float Damage => projectileDamage;
        public float Speed => projectileSpeed;
        public float Lifetime => projectileLifetime;
        public int PierceCount => pierceCount;
        public int ActiveTargetMask => EffectiveTargetMask();
        public bool DirectDamageEnabled => projectileDirectDamageEnabled;
        public float RepeatHitCooldown => repeatHitCooldownSeconds;
        public bool IsReady => cooldownRemaining <= 0f;

        public ProjectileTrackingConfig GetTrackingConfig() => TrackingConfig();
        public bool OwnsProjectile(int id) => ownedProjectileIds.Contains(id);
        public void TrackProjectileId(int id) => ownedProjectileIds.Add(id);
        public void SetChildConfig(ProjectileChildSpawnConfig config) => activeChildConfig = config;

        // --- lifecycle ---

        private void Awake()
        {
            projectileRoot ??= FindProjectileRootForOwner();
            if (projectileRoot == null)
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a projectile root.");
            }

            ValidateReferences();
            audioManager ??= AudioManager.Instance != null ? AudioManager.Instance : FindAnyObjectByType<AudioManager>();
            hitEffects = GetComponentsInChildren<ProjectileHitEffect>(true);
            for (int i = 0; i < hitEffects.Length; i++)
            {
                hitEffects[i].Configure(this);
            }

            RegisterBasicPrefabs();
        }

        private void OnEnable()
        {
            SubscribeProjectileRoot();
            SubscribeAoeRoot();
        }

        private void OnDisable()
        {
            UnsubscribeProjectileRoot();
            UnsubscribeAoeRoot();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnValidate()
        {
            if (basicPrefab != null && !IsValidBasicPrefab(basicPrefab, out string reason))
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} has invalid {nameof(basicPrefab)} '{basicPrefab.name}': {reason}.");
            }
        }

        // --- public API ---

        public void Configure(ProjectileRoot root)
        {
            if (projectileRoot == root) return;

            UnsubscribeProjectileRoot();
            projectileRoot = root;
            RegisterBasicPrefabs();
            if (isActiveAndEnabled)
            {
                SubscribeProjectileRoot();
            }
        }

        public void ConfigureAoeRoot(AoeRoot root)
        {
            if (aoeRoot == root) return;

            UnsubscribeAoeRoot();
            aoeRoot = root;
            if (isActiveAndEnabled)
            {
                SubscribeAoeRoot();
            }
        }

        public void Tick(float deltaTime)
        {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        }

        public bool TryFire(Vector2 aimDirection)
        {
            if (!IsReady) return false;

            DamageSnapshot damage = new(projectileDamage);
            ProjectileVolleyBuilder.Build(
                commands,
                transform.position,
                aimDirection,
                projectileCount,
                spreadDegrees,
                jitterDegrees,
                projectileSpeed,
                projectileLifetime,
                basicPrefab.Radius,
                damage,
                basicPrefab.ShapeType);

            for (int i = 0; i < commands.Count; i++)
            {
                ProjectileSpawnCommand command = WithAuthoredOptions(commands[i], damage);
                ownedProjectileIds.Add(projectileRoot.Spawn(command));
            }

            PlayPerformSound(transform.position);
            cooldownRemaining = recoverySeconds;
            return true;
        }

        public void ConfigureAuthoring(
            int projectileCount,
            int pierceCount,
            bool directDamageEnabled,
            bool trackingEnabled)
        {
            this.projectileCount = projectileCount;
            this.pierceCount = pierceCount;
            projectileDirectDamageEnabled = directDamageEnabled;
            this.trackingEnabled = trackingEnabled;
        }

        // --- private ---

        private ProjectileSpawnCommand WithAuthoredOptions(ProjectileSpawnCommand baseCommand, DamageSnapshot damage)
        {
            return new ProjectileSpawnCommand(
                baseCommand.Position,
                baseCommand.Direction,
                projectileSpeed,
                projectileLifetime,
                basicPrefab.Radius,
                basicPrefab.HalfExtents,
                basicPrefab.RotationRadians,
                damage,
                basicPrefab.ShapeType,
                projectileRoot.RegisterTemplate(basicPrefab),
                EffectiveTargetMask(),
                pierceCount,
                repeatHitCooldownSeconds,
                TrackingConfig(),
                activeChildConfig,
                projectileDirectDamageEnabled);
        }

        private ProjectileTrackingConfig TrackingConfig()
        {
            return new ProjectileTrackingConfig(
                trackingEnabled,
                trackingRange,
                trackingTurnSpeedDegrees,
                trackingQueryIntervalSeconds,
                trackingInitialQueryDelaySeconds);
        }

        private void SubscribeProjectileRoot()
        {
            if (projectileRoot == null || subscribedRoot == projectileRoot) return;

            projectileRoot.ProjectileHit += OnProjectileHit;
            subscribedRoot = projectileRoot;
        }

        private void UnsubscribeProjectileRoot()
        {
            if (subscribedRoot == null) return;

            subscribedRoot.ProjectileHit -= OnProjectileHit;
            subscribedRoot = null;
        }

        private void SubscribeAoeRoot()
        {
            if (aoeRoot == null || subscribedAoeRoot == aoeRoot) return;

            AoeSpawnRequested += OnAoeSpawnRequested;
            subscribedAoeRoot = aoeRoot;
        }

        private void UnsubscribeAoeRoot()
        {
            if (subscribedAoeRoot == null) return;

            AoeSpawnRequested -= OnAoeSpawnRequested;
            subscribedAoeRoot = null;
        }

        private void OnAoeSpawnRequested(ProjectileAoeSpawnRequest request)
        {
            subscribedAoeRoot?.Spawn(request);
        }

        private void OnProjectileHit(ProjectileHitContext hit)
        {
            if (!ownedProjectileIds.Contains(hit.ProjectileId)) return;

            if (impactAoeTypeId >= 0)
            {
                AoeSpawnRequested?.Invoke(new ProjectileAoeSpawnRequest(
                    impactAoeTypeId,
                    hit.Position,
                    new DamageSnapshot(Mathf.Max(0f, impactAoeDamage)),
                    impactAoeLifetimeSeconds,
                    impactAoeTickIntervalSeconds));
            }

            for (int i = 0; i < hitEffects.Length; i++)
            {
                hitEffects[i].Apply(in hit, AoeSpawnRequested);
            }
        }

        private void RegisterBasicPrefabs()
        {
            if (projectileRoot == null) return;

            projectileRoot.RegisterTemplate(basicPrefab);
        }

        private int EffectiveTargetMask()
        {
            return targetMask != 1 || projectileRoot == null ? targetMask : projectileRoot.TargetMask;
        }

        private void ValidateReferences()
        {
            if (!IsValidBasicPrefab(basicPrefab, out string reason))
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a valid {nameof(basicPrefab)}: {reason}.");
            }
        }

        private static bool IsValidBasicPrefab(BasicAttackPrefab template, out string reason)
        {
            if (template == null)
            {
                reason = "field is not assigned";
                return false;
            }

            return template.IsValidTemplate(out reason);
        }

        private void PlayPerformSound(Vector2 worldPosition)
        {
            if (performSound == null || audioManager == null) return;

            audioManager.PlaySound(performSound, worldPosition);
        }

        private ProjectileRoot FindProjectileRootForOwner()
        {
            string tag = GetComponentInParent<PlayGround.Player.PlayerRoot>() != null
                ? GameplayTags.PlayerProjectileRoot
                : GameplayTags.MobProjectileRoot;

            try
            {
                GameObject[] rootObjects = GameObject.FindGameObjectsWithTag(tag);
                for (int i = 0; i < rootObjects.Length; i++)
                {
                    if (rootObjects[i].TryGetComponent(out ProjectileRoot root))
                    {
                        return root;
                    }
                }

                return null;
            }
            catch (UnityException)
            {
                return null;
            }
        }
    }
}
