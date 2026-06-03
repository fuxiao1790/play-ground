using System.Collections.Generic;
using PlayGround.Audio;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class ChildSpawningProjectileAttack : MonoBehaviour
    {
        private static int nextChildSpawnerId;

        [SerializeField] private ProjectileRoot projectileRoot;
        [SerializeField] private ProjectileConfig parentConfig;
        [SerializeField] private ProjectileConfig childConfig;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioManager audioManager;
        [SerializeField] private ProjectileHitEffectDefinition[] hitEffectDefinitions = global::System.Array.Empty<ProjectileHitEffectDefinition>();
        [SerializeField] private AoeRoot aoeRoot;

        [SerializeField] private float recoverySeconds = 0.15f;
        [SerializeField] private int childProjectileCount;
        [SerializeField] private float childSpawnIntervalSeconds;
        [SerializeField] private float childSpawnIntervalJitterSeconds;
        [SerializeField, Range(0f, 180f)] private float childSideSpreadDegrees = 30f;

        private readonly List<ProjectileSpawnCommand> commands = new();
        private ProjectileHitEffect[] hitEffects = global::System.Array.Empty<ProjectileHitEffect>();
        private AoeRoot subscribedAoeRoot;
        private ProjectileRoot subscribedProjectileRoot;
        private int childSpawnerId;
        private float localCooldown;
        public event global::System.Action<ProjectileAoeSpawnRequest> AoeSpawnRequested;

        public ProjectileRoot Root => projectileRoot;
        public bool IsReady => localCooldown <= 0f;
        private EntityId SourceNodeId => gameObject.GetEntityId();

        public bool TryFire(Vector2 aimDirection)
        {
            if (!IsReady) return false;
            PerformVolley(aimDirection, BuildActiveChildConfig());
            localCooldown = Mathf.Max(0.01f, recoverySeconds);
            return true;
        }

        public void ConfigureAoeRoot(AoeRoot root)
        {
            if (aoeRoot == root) return;

            UnsubscribeAoeRoot();
            aoeRoot = root;
            if (isActiveAndEnabled)
                SubscribeAoeRoot();
        }

        private void Awake()
        {
            projectileRoot ??= FindProjectileRootForOwner();
            ValidateReferences();
            ValidateHitEffects();
            audioManager ??= AudioManager.Instance != null ? AudioManager.Instance : FindAnyObjectByType<AudioManager>();
            hitEffects = GetComponentsInChildren<ProjectileHitEffect>(true);
            for (int i = 0; i < hitEffects.Length; i++)
                hitEffects[i].Configure(this);

            RegisterBasicPrefabs();
        }

        private void OnEnable()
        {
            AssignChildSpawnerId();
            RegisterBasicPrefabs();
            SubscribeProjectileRoot();
            SubscribeAoeRoot();
        }

        private void Update()
        {
            if (localCooldown > 0f)
                localCooldown = Mathf.Max(0f, localCooldown - Time.deltaTime);
        }

        private void OnDisable()
        {
            UnsubscribeProjectileRoot();
            UnsubscribeAoeRoot();
        }

        private void OnValidate()
        {
            recoverySeconds = Mathf.Max(0f, recoverySeconds);
            childSpawnIntervalSeconds = Mathf.Max(0f, childSpawnIntervalSeconds);
            childSpawnIntervalJitterSeconds = Mathf.Max(0f, childSpawnIntervalJitterSeconds);
        }

        // --- private ---

        private void ValidateReferences()
        {
            if (projectileRoot == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: projectileRoot is not assigned.");
            if (parentConfig == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: parentConfig is not assigned.");
            if (childConfig == null)
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name}: childConfig is not assigned.");
            if (!IsValidBasicPrefab(parentConfig.Prefab, out string parentReason))
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name} needs a valid parent {nameof(BasicAttackPrefab)} in '{parentConfig.name}': {parentReason}.");
            if (!IsValidBasicPrefab(childConfig.Prefab, out string childReason))
                throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name} needs a valid child {nameof(BasicAttackPrefab)} in '{childConfig.name}': {childReason}.");
        }

        private void AssignChildSpawnerId()
        {
            if (childSpawnerId <= 0 && childProjectileCount > 0 && childSpawnIntervalSeconds > 0f)
                childSpawnerId = ++nextChildSpawnerId;
        }

        private void RegisterBasicPrefabs()
        {
            if (projectileRoot == null) return;

            if (parentConfig != null && parentConfig.Prefab != null)
                projectileRoot.RegisterTemplate(parentConfig.Prefab);
            if (childConfig != null && childConfig.Prefab != null)
                projectileRoot.RegisterTemplate(childConfig.Prefab);
        }

        private ProjectileChildSpawnConfig BuildChildConfig()
        {
            BasicAttackPrefab prefab = childConfig.Prefab;
            int typeId = projectileRoot.RegisterTemplate(prefab);

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

        private ProjectileChildSpawnConfig BuildActiveChildConfig()
        {
            if (childProjectileCount <= 0 || childSpawnIntervalSeconds <= 0f)
            {
                return ProjectileChildSpawnConfig.Disabled;
            }

            return BuildChildConfig();
        }

        private void PerformVolley(Vector2 aimDirection, ProjectileChildSpawnConfig childSpawnConfig)
        {
            DamageSnapshot damage = new(parentConfig.Damage);
            ProjectileVolleyBuilder.Build(
                commands,
                transform.position,
                aimDirection,
                parentConfig.Count,
                parentConfig.SpreadDegrees,
                parentConfig.JitterDegrees,
                parentConfig.Speed,
                parentConfig.Lifetime,
                parentConfig.Prefab.Radius,
                damage,
                parentConfig.Prefab.ShapeType);

            for (int i = 0; i < commands.Count; i++)
            {
                ProjectileSpawnCommand command = WithAuthoredOptions(commands[i], damage, childSpawnConfig);
                projectileRoot.Spawn(command);
            }

            PlayPerformSound(transform.position);
        }

        private ProjectileSpawnCommand WithAuthoredOptions(ProjectileSpawnCommand baseCommand, DamageSnapshot damage, ProjectileChildSpawnConfig childSpawnConfig)
        {
            int targetMask = EffectiveTargetMask();
            return new ProjectileSpawnCommand(
                baseCommand.Position,
                baseCommand.Direction,
                parentConfig.Speed,
                parentConfig.Lifetime,
                parentConfig.Prefab.Radius,
                parentConfig.Prefab.HalfExtents,
                parentConfig.Prefab.RotationRadians,
                damage,
                parentConfig.Prefab.ShapeType,
                projectileRoot.RegisterTemplate(parentConfig.Prefab),
                targetMask,
                parentConfig.PierceCount,
                parentConfig.RepeatHitCooldown,
                parentConfig.GetTrackingConfig(),
                childSpawnConfig,
                parentConfig.DirectDamageEnabled,
                SourceNodeId,
                ImpactAoeSnapshot(targetMask));
        }

        private ProjectileImpactAoeSnapshot ImpactAoeSnapshot(int targetMask)
        {
            if (parentConfig.ImpactAoeTypeId < 0)
            {
                return default;
            }

            return new ProjectileImpactAoeSnapshot(
                parentConfig.ImpactAoeTypeId,
                targetMask,
                Mathf.Max(0f, parentConfig.ImpactAoeDamage),
                parentConfig.ImpactAoeLifetimeSeconds,
                parentConfig.ImpactAoeTickIntervalSeconds);
        }

        private int EffectiveTargetMask()
        {
            return parentConfig.TargetMask != 1 || projectileRoot == null ? parentConfig.TargetMask : projectileRoot.TargetMask;
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

        private void SubscribeProjectileRoot()
        {
            if (projectileRoot == null || subscribedProjectileRoot == projectileRoot) return;

            projectileRoot.ProjectileHit += OnRootProjectileHit;
            subscribedProjectileRoot = projectileRoot;
        }

        private void UnsubscribeProjectileRoot()
        {
            if (subscribedProjectileRoot == null) return;

            subscribedProjectileRoot.ProjectileHit -= OnRootProjectileHit;
            subscribedProjectileRoot = null;
        }

        private void OnAoeSpawnRequested(ProjectileAoeSpawnRequest request)
        {
            subscribedAoeRoot?.Spawn(request);
        }

        private void OnRootProjectileHit(ProjectileHitContext hit)
        {
            if (hit.Payload.SourceNodeId.Equals(SourceNodeId))
            {
                OnProjectileHit(hit);
            }
        }

        private void OnProjectileHit(ProjectileHitContext hit)
        {
            for (int i = 0; i < hitEffectDefinitions.Length; i++)
                hitEffectDefinitions[i].Apply(in hit, AoeSpawnRequested);

            for (int i = 0; i < hitEffects.Length; i++)
                hitEffects[i].Apply(in hit, AoeSpawnRequested);
        }

        private void ValidateHitEffects()
        {
            if (hitEffectDefinitions == null)
            {
                hitEffectDefinitions = global::System.Array.Empty<ProjectileHitEffectDefinition>();
                return;
            }

            for (int i = 0; i < hitEffectDefinitions.Length; i++)
            {
                if (hitEffectDefinitions[i] == null)
                    throw new MissingReferenceException($"{nameof(ChildSpawningProjectileAttack)} on {name} hit effect slot {i} is empty.");
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
                        return root;
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
