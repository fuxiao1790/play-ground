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
        [SerializeField] private AoeRoot aoeRoot;

        private readonly List<ProjectileSpawnCommand> commands = new();
        private ProjectileHitEffect[] hitEffects = global::System.Array.Empty<ProjectileHitEffect>();
        private AoeRoot subscribedAoeRoot;
        private IProjectileHitActor hitSource;
        private float cooldownRemaining;
        public event global::System.Action<ProjectileAoeSpawnRequest> AoeSpawnRequested;

        public ProjectileRoot Root => projectileRoot;
        public bool IsReady => cooldownRemaining <= 0f;

        // --- lifecycle ---

        private void Awake()
        {
            projectileRoot ??= FindProjectileRootForOwner();
            if (projectileRoot == null)
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} needs a projectile root.");

            ValidateConfig();
            audioManager ??= AudioManager.Instance != null ? AudioManager.Instance : FindAnyObjectByType<AudioManager>();
            hitSource = GetComponentInParent<IProjectileHitActor>();
            hitEffects = GetComponentsInChildren<ProjectileHitEffect>(true);
            for (int i = 0; i < hitEffects.Length; i++)
                hitEffects[i].Configure(this);

            RegisterBasicPrefabs();
        }

        private void OnEnable()
        {
            SubscribeAoeRoot();
        }

        private void OnDisable()
        {
            UnsubscribeAoeRoot();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnValidate()
        {
            if (config != null && config.Prefab != null && !IsValidBasicPrefab(config.Prefab, out string reason))
                throw new MissingReferenceException($"{nameof(ProjectileAttack)} on {name} has invalid prefab in {nameof(ProjectileConfig)} '{config.name}': {reason}.");
        }

        // --- public API ---

        public void Configure(ProjectileRoot root)
        {
            if (projectileRoot == root) return;

            projectileRoot = root;
            RegisterBasicPrefabs();
        }

        public void ConfigureAoeRoot(AoeRoot root)
        {
            if (aoeRoot == root) return;

            UnsubscribeAoeRoot();
            aoeRoot = root;
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
            cooldownRemaining = recoverySeconds;
            return true;
        }

        internal void SpawnForChildSpawner(Vector2 aimDirection, ProjectileChildSpawnConfig childConfig)
        {
            PerformVolley(aimDirection, childConfig);
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
                hitSource != null ? hitSource.ProjectileHitNodeId : default,
                ImpactAoeSnapshot(targetMask));
        }

        private ProjectileImpactAoeSnapshot ImpactAoeSnapshot(int targetMask)
        {
            if (config.ImpactAoeTypeId < 0)
            {
                return default;
            }

            return new ProjectileImpactAoeSnapshot(
                config.ImpactAoeTypeId,
                targetMask,
                Mathf.Max(0f, config.ImpactAoeDamage),
                config.ImpactAoeLifetimeSeconds,
                config.ImpactAoeTickIntervalSeconds);
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
            if (config.ImpactAoeTypeId >= 0)
            {
                AoeSpawnRequested?.Invoke(new ProjectileAoeSpawnRequest(
                    config.ImpactAoeTypeId,
                    hit.Position,
                    new DamageSnapshot(Mathf.Max(0f, config.ImpactAoeDamage)),
                    config.ImpactAoeLifetimeSeconds,
                    config.ImpactAoeTickIntervalSeconds));
            }

            for (int i = 0; i < hitEffects.Length; i++)
                hitEffects[i].Apply(in hit, AoeSpawnRequested);
        }

        private void RegisterBasicPrefabs()
        {
            if (projectileRoot == null || config == null) return;

            projectileRoot.RegisterTemplate(config.Prefab);
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
