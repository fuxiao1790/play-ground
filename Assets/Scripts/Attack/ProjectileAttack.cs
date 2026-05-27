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
        private static int nextChildSpawnerId;

        [SerializeField] private ProjectileRoot projectileRoot;
        [SerializeField] private float recoverySeconds = 0.15f;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioManager audioManager;
        [SerializeField] private AoeRoot aoeRoot;
        [SerializeField] private BasicAttackPrefab basicPrefab;
        [SerializeField] private BasicAttackPrefab childBasicPrefab;
        [SerializeField, HideInInspector] private int projectileTypeId;
        [SerializeField, HideInInspector] private int childProjectileTypeId;
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
        [SerializeField] private int childProjectileCount;
        [SerializeField] private float childSpawnIntervalSeconds;
        [SerializeField] private float childSpawnIntervalJitterSeconds;
        [SerializeField] private ProjectileChildSpawnPattern childSpawnPattern;
        [SerializeField] private float childDamageMultiplier = 1f;

        private readonly List<ProjectileSpawnCommand> commands = new();
        private readonly List<ProjectileVolleyBuilder.SpawnRequest> childRequests = new();
        private readonly HashSet<int> ownedProjectileIds = new();
        private ProjectileHitEffect[] hitEffects = global::System.Array.Empty<ProjectileHitEffect>();
        private ProjectileRoot subscribedRoot;
        private AoeRoot subscribedAoeRoot;
        private int childSpawnerId;
        private float cooldownRemaining;

        public event global::System.Action<ProjectileAoeSpawnRequest> AoeSpawnRequested;

        public bool IsReady => cooldownRemaining <= 0f;

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
            // Keep existing automatic wiring for child components
            // Do not clear invalid prefab references silently; instead throw so authoring errors are visible.
            // This prevents inspector fields from being nulled and ensures developers correct renderability issues.
            if (basicPrefab != null && !IsValidBasicPrefab(basicPrefab, out string basicReason))
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} has invalid {nameof(basicPrefab)} '{basicPrefab.name}': {basicReason}.");
            }

            if (childBasicPrefab != null && !IsValidBasicPrefab(childBasicPrefab, out string childReason))
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} has invalid {nameof(childBasicPrefab)} '{childBasicPrefab.name}': {childReason}.");
            }
        }

        public void Configure(ProjectileRoot root)
        {
            if (projectileRoot == root)
            {
                return;
            }

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
            if (aoeRoot == root)
            {
                return;
            }

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
            if (!IsReady)
            {
                return false;
            }

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
                ProjectileRadius(basicPrefab),
                damage,
                ProjectileShape(basicPrefab));

            for (int i = 0; i < commands.Count; i++)
            {
                ProjectileSpawnCommand command = WithAuthoredOptions(commands[i], damage, ChildSpawnConfig(), basicPrefab);
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
            bool trackingEnabled,
            int childProjectileCount,
            float childSpawnIntervalSeconds)
        {
            this.projectileCount = projectileCount;
            this.pierceCount = pierceCount;
            projectileDirectDamageEnabled = directDamageEnabled;
            this.trackingEnabled = trackingEnabled;
            this.childProjectileCount = childProjectileCount;
            this.childSpawnIntervalSeconds = childSpawnIntervalSeconds;
        }

        private ProjectileSpawnCommand WithAuthoredOptions(
            ProjectileSpawnCommand baseCommand,
            DamageSnapshot damage,
            ProjectileChildSpawnConfig childSpawn,
            BasicAttackPrefab basicPrefab)
        {
            return new ProjectileSpawnCommand(
                baseCommand.Position,
                baseCommand.Direction,
                projectileSpeed,
                projectileLifetime,
                ProjectileRadius(basicPrefab),
                ProjectileHalfExtents(basicPrefab),
                ProjectileRotationRadians(basicPrefab),
                damage,
                ProjectileShape(basicPrefab),
                ProjectileTypeId(basicPrefab, projectileTypeId),
                EffectiveTargetMask(),
                pierceCount,
                repeatHitCooldownSeconds,
                TrackingConfig(),
                childSpawn,
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

        private ProjectileChildSpawnConfig ChildSpawnConfig()
        {
            if (childProjectileCount <= 0 || childSpawnIntervalSeconds <= 0f)
            {
                return ProjectileChildSpawnConfig.Disabled;
            }

            if (childSpawnerId <= 0)
            {
                childSpawnerId = ++nextChildSpawnerId;
            }

            return new ProjectileChildSpawnConfig(
                childSpawnerId,
                Mathf.Max(0.01f, childSpawnIntervalSeconds),
                childSpawnIntervalJitterSeconds);
        }

        private void SubscribeProjectileRoot()
        {
            if (projectileRoot == null || subscribedRoot == projectileRoot)
            {
                return;
            }

            projectileRoot.ProjectileHit += OnProjectileHit;
            projectileRoot.ChildSpawnRequested += OnChildSpawnRequested;
            subscribedRoot = projectileRoot;
        }

        private void UnsubscribeProjectileRoot()
        {
            if (subscribedRoot == null)
            {
                return;
            }

            subscribedRoot.ProjectileHit -= OnProjectileHit;
            subscribedRoot.ChildSpawnRequested -= OnChildSpawnRequested;
            subscribedRoot = null;
        }

        private void SubscribeAoeRoot()
        {
            if (aoeRoot == null || subscribedAoeRoot == aoeRoot)
            {
                return;
            }

            AoeSpawnRequested += OnAoeSpawnRequested;
            subscribedAoeRoot = aoeRoot;
        }

        private void UnsubscribeAoeRoot()
        {
            if (subscribedAoeRoot == null)
            {
                return;
            }

            AoeSpawnRequested -= OnAoeSpawnRequested;
            subscribedAoeRoot = null;
        }

        private void OnAoeSpawnRequested(ProjectileAoeSpawnRequest request)
        {
            subscribedAoeRoot?.Spawn(request);
        }

        private void OnProjectileHit(ProjectileHitContext hit)
        {
            if (!ownedProjectileIds.Contains(hit.ProjectileId))
            {
                return;
            }

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

        private void OnChildSpawnRequested(ProjectileChildSpawnRequest request)
        {
            if (request.ChildSpawnerId != childSpawnerId || !ownedProjectileIds.Contains(request.ProjectileId))
            {
                return;
            }

            BuildChildRequests(request);
            DamageSnapshot childDamage = new(request.Damage.Amount * Mathf.Max(0f, childDamageMultiplier));
            for (int i = 0; i < childRequests.Count; i++)
            {
                ProjectileVolleyBuilder.SpawnRequest child = childRequests[i];
                Vector2 direction = child.Velocity.sqrMagnitude > 0f ? child.Velocity.normalized : Vector2.right;
                BasicAttackPrefab childPrefab = ChildBasicPrefab();
                var command = new ProjectileSpawnCommand(
                    child.Position,
                    direction,
                    child.Velocity.magnitude,
                    projectileLifetime,
                    ProjectileRadius(childPrefab),
                    ProjectileHalfExtents(childPrefab),
                    ProjectileRotationRadians(childPrefab),
                    childDamage,
                    ProjectileShape(childPrefab),
                    ProjectileTypeId(childPrefab, childProjectileTypeId),
                    EffectiveTargetMask(),
                    pierceCount,
                    repeatHitCooldownSeconds,
                    TrackingConfig(),
                    ProjectileChildSpawnConfig.Disabled,
                    projectileDirectDamageEnabled);
                ownedProjectileIds.Add(projectileRoot.Spawn(command));
            }
        }

        private void RegisterBasicPrefabs()
        {
            if (projectileRoot == null)
            {
                return;
            }

            projectileRoot.RegisterTemplate(basicPrefab);

            if (childBasicPrefab != null)
            {
                projectileRoot.RegisterTemplate(childBasicPrefab);
            }
        }

        private BasicAttackPrefab ChildBasicPrefab()
        {
            return childBasicPrefab != null ? childBasicPrefab : basicPrefab;
        }

        private float ProjectileRadius(BasicAttackPrefab basicPrefab)
        {
            return basicPrefab.Radius;
        }

        private Vector2 ProjectileHalfExtents(BasicAttackPrefab basicPrefab)
        {
            return basicPrefab.HalfExtents;
        }

        private float ProjectileRotationRadians(BasicAttackPrefab basicPrefab)
        {
            return basicPrefab.RotationRadians;
        }

        private ProjectileShapeType ProjectileShape(BasicAttackPrefab basicPrefab)
        {
            return basicPrefab.ShapeType;
        }

        private int ProjectileTypeId(BasicAttackPrefab basicPrefab, int fallbackTypeId)
        {
            return projectileRoot != null ? projectileRoot.RegisterTemplate(basicPrefab) : fallbackTypeId;
        }

        private void ValidateReferences()
        {
            if (!IsValidBasicPrefab(basicPrefab, out string reason))
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a valid {nameof(basicPrefab)}: {reason}.");
            }

            if (childBasicPrefab != null && !IsValidBasicPrefab(childBasicPrefab, out string childReason))
            {
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} has invalid {nameof(childBasicPrefab)}: {childReason}.");
            }
        }

        private void ClearInvalidPrefab(ref BasicAttackPrefab template, string fieldName)
        {
            if (template == null)
            {
                return;
            }

            if (IsValidBasicPrefab(template, out string reason))
            {
                return;
            }

            Debug.LogError($"{nameof(ProjectileAttack)} on {name} rejected {fieldName} '{template.name}': {reason}.", this);
            template = null;
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

        private void BuildChildRequests(ProjectileChildSpawnRequest request)
        {
            if (childSpawnPattern != null)
            {
                childSpawnPattern.Build(
                    childRequests,
                    request.Position,
                    request.Velocity,
                    childProjectileCount,
                    projectileSpeed,
                    request.TickIndex);
                return;
            }

            BuildDefaultSideSpray(
                childRequests,
                request.Position,
                request.Velocity,
                childProjectileCount,
                projectileSpeed);
        }

        private int EffectiveTargetMask()
        {
            return targetMask != 1 || projectileRoot == null ? targetMask : projectileRoot.TargetMask;
        }

        private static void BuildDefaultSideSpray(
            List<ProjectileVolleyBuilder.SpawnRequest> buffer,
            Vector2 parentPosition,
            Vector2 parentVelocity,
            int childCount,
            float childSpeed)
        {
            buffer.Clear();
            int count = Mathf.Max(1, childCount);
            Vector2 forward = parentVelocity.sqrMagnitude > 0f ? parentVelocity.normalized : Vector2.right;
            Vector2 left = Quaternion.Euler(0f, 0f, -90f) * forward;
            Vector2 right = Quaternion.Euler(0f, 0f, 90f) * forward;
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = (i % 2) == 0 ? left : right;
                buffer.Add(new ProjectileVolleyBuilder.SpawnRequest(parentPosition, direction * childSpeed));
            }
        }

        private void PlayPerformSound(Vector2 worldPosition)
        {
            if (performSound == null || audioManager == null)
            {
                return;
            }

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
