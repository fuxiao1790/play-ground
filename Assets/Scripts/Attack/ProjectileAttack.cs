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
        [SerializeField] private ProjectileConfig config;
        [SerializeField] private float recoverySeconds = 0.15f;
        [SerializeField] private AudioClip performSound;
        [SerializeField] private AudioManager audioManager;
        [SerializeField] private ProjectileHitEffectDefinition[] hitEffectDefinitions = global::System.Array.Empty<ProjectileHitEffectDefinition>();
        [SerializeField] private AoeRoot aoeRoot;

        private readonly List<ProjectileSpawnCommand> commands = new();
        private ProjectileHitEffect[] hitEffects = global::System.Array.Empty<ProjectileHitEffect>();
        private AoeRoot subscribedAoeRoot;
        private ProjectileRoot subscribedProjectileRoot;
        private float cooldownRemaining;
        private int impactAoeTypeId = -1;
        public event global::System.Action<ProjectileAoeSpawnRequest> AoeSpawnRequested;

        public ProjectileRoot Root => projectileRoot;
        public bool IsReady => cooldownRemaining <= 0f;
        private EntityId SourceNodeId => gameObject.GetEntityId();

        // --- lifecycle ---

        private void Awake()
        {
            projectileRoot ??= FindProjectileRootForOwner();
            if (projectileRoot == null)
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a projectile root.");

            ValidateConfig();
            ValidateHitEffects();
            audioManager ??= AudioManager.Instance != null ? AudioManager.Instance : FindAnyObjectByType<AudioManager>();
            hitEffects = GetComponentsInChildren<ProjectileHitEffect>(true);
            for (int i = 0; i < hitEffects.Length; i++)
                hitEffects[i].Configure(this);
        }

        private void OnEnable()
        {
            RegisterBasicPrefabs();
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
            recoverySeconds = Mathf.Max(0f, recoverySeconds);
            if (config != null && config.Prefab != null && !IsValidBasicPrefab(config.Prefab, out string reason))
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} has invalid prefab in {nameof(ProjectileConfig)} '{config.name}': {reason}.");
        }

        // --- public API ---

        public void Configure(ProjectileRoot root)
        {
            if (projectileRoot == root) return;

            UnsubscribeProjectileRoot();
            projectileRoot = root;
            RegisterBasicPrefabs();
            if (isActiveAndEnabled)
                SubscribeProjectileRoot();
        }

        public void ConfigureAoeRoot(AoeRoot root)
        {
            if (aoeRoot == root) return;

            UnsubscribeAoeRoot();
            aoeRoot = root;
            RegisterImpactAoeConfig();
            if (isActiveAndEnabled)
                SubscribeAoeRoot();
        }

        public void Tick(float deltaTime)
        {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        }

        public bool TryFire(Vector2 aimDirection) =>
            TryFire(aimDirection, ProjectileChildSpawnConfig.Disabled);

        public bool TryFire(Vector2 aimDirection, ProjectileChildSpawnConfig childConfig)
        {
            if (!IsReady) return false;

            PerformVolley(aimDirection, childConfig);
            cooldownRemaining = Mathf.Max(0.01f, recoverySeconds);
            return true;
        }

        private void PerformVolley(Vector2 aimDirection, ProjectileChildSpawnConfig childConfig)
        {
            DamageSnapshot damage = new(config.Damage);
            ProjectileVolleyBuilder.Build(
                commands,
                transform.position,
                aimDirection,
                config.Count,
                config.SpreadDegrees,
                config.JitterDegrees,
                config.Speed,
                config.Lifetime,
                config.Prefab.Radius,
                damage,
                config.Prefab.ShapeType);

            for (int i = 0; i < commands.Count; i++)
            {
                ProjectileSpawnCommand command = WithAuthoredOptions(commands[i], damage, childConfig);
                projectileRoot.Spawn(command);
            }

            PlayPerformSound(transform.position);
        }

        // --- private ---

        private ProjectileSpawnCommand WithAuthoredOptions(ProjectileSpawnCommand baseCommand, DamageSnapshot damage, ProjectileChildSpawnConfig childConfig)
        {
            int targetMask = EffectiveTargetMask();
            return new ProjectileSpawnCommand(
                baseCommand.Position,
                baseCommand.Direction,
                config.Speed,
                config.Lifetime,
                config.Prefab.Radius,
                config.Prefab.HalfExtents,
                config.Prefab.RotationRadians,
                damage,
                config.Prefab.ShapeType,
                projectileRoot.RegisterTemplate(config.Prefab),
                targetMask,
                config.PierceCount,
                config.RepeatHitCooldown,
                config.GetTrackingConfig(),
                childConfig,
                config.DirectDamageEnabled,
                SourceNodeId,
                ImpactAoeSnapshot(targetMask));
        }

        private ProjectileImpactAoeSnapshot ImpactAoeSnapshot(int targetMask)
        {
            if (impactAoeTypeId < 0)
            {
                return default;
            }

            return new ProjectileImpactAoeSnapshot(
                impactAoeTypeId,
                targetMask,
                Mathf.Max(0f, config.ImpactAoeConfig.Damage),
                config.ImpactAoeConfig.LifetimeSeconds,
                config.ImpactAoeConfig.TickIntervalSeconds);
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

        private void RegisterBasicPrefabs()
        {
            if (projectileRoot == null || config == null) return;

            projectileRoot.RegisterTemplate(config.Prefab);
            RegisterImpactAoeConfig();
        }

        private void RegisterImpactAoeConfig()
        {
            if (aoeRoot == null || config?.ImpactAoeConfig == null) return;

            impactAoeTypeId = aoeRoot.RegisterConfig(config.ImpactAoeConfig);
        }

        private int EffectiveTargetMask()
        {
            return config.TargetMask != 1 || projectileRoot == null ? config.TargetMask : projectileRoot.TargetMask;
        }

        private void ValidateConfig()
        {
            if (config == null)
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a {nameof(ProjectileConfig)}.");

            if (!IsValidBasicPrefab(config.Prefab, out string reason))
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a valid {nameof(BasicAttackPrefab)} in its {nameof(ProjectileConfig)}: {reason}.");
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
                    throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} hit effect slot {i} is empty.");
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
