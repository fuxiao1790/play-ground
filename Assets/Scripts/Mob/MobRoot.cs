using System;
using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.Common.Stats;
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
using PlayGround.System.Combat.Vfx;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

namespace PlayGround.Mob
{
    public class MobRoot : MonoBehaviour, ICombatTarget
    {
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Collider2D hurtbox;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Transform target;
        [SerializeField] private UnitStatSheet statSheet;
        [SerializeField] private float targetRadius = 0.5f;
        [SerializeField, Min(0.05f)] private float wanderMinDuration = 0.7f;
        [SerializeField, Min(0.05f)] private float wanderMaxDuration = 2f;
        [SerializeField, Range(0f, 1f)] private float wanderPauseChance = 0.15f;
        [SerializeField] private int randomSeed;

        private static readonly ProfilerMarker UpdateMarker = new("MobRoot.Update");
        private static readonly ProfilerMarker PushCombatTargetProxyMarker = new("MobRoot.PushCombatTargetProxy");
        private static readonly ProfilerMarker TickWanderMarker = new("MobRoot.TickWander");
        private static readonly ProfilerMarker ApplyVelocityMarker = new("MobRoot.ApplyVelocity");
        private static readonly ProfilerMarker DriveSkillsMarker = new("MobRoot.DriveSkills");
        private static readonly ProfilerMarker AcquireEnemyTargetMarker = new("MobRoot.TryAcquireEnemyTarget");
        private static readonly ProfilerMarker AimTargetMarker = new("MobRoot.AimTarget");
        private static readonly ProfilerMarker SkillTickMarker = new("MobRoot.SkillDriver.Tick");

        private static int nextTargetId;
        private readonly List<StatusStackSnapshot> statusSnapshots = new();
        private readonly List<CombatTargetRegistry<ICombatTarget>> registries = new();
        private CombatTargetSet combatTargetSet;
        private Entity combatTargetProxy;
        private SkillDriver skillDriver;
        private global::System.Random random;
        private Vector2 wanderVelocity;
        private float wanderTimer;
        private Vector2 cachedCombatTargetLocalOffset;
        private float cachedCombatTargetRadius;
        private Vector2 cachedCombatTargetHalfExtents;
        private float cachedCombatTargetRotationRadians;
        private CombatShapeType cachedCombatTargetShapeType;
        private int cachedCombatTargetMask;
        private bool combatTargetShapeCached;
        private bool despawnPending;
        private int targetId;
        private bool isAlive = true;
        private Resource health;
        private Resource mana;

        public event Action<MobRoot> SoftDied;

        public float Speed => statSheet.MoveSpeed;
        public float CurrentHealth => health?.Current ?? 0f;
        public float MaxHealth => health?.Max ?? statSheet.MaxHealth;
        public float CurrentMana => mana?.Current ?? 0f;
        public IReadOnlyList<StatusStackSnapshot> StatusSnapshots => statusSnapshots;
        public Transform Target => target;
        public int TargetId => targetId;
        public Entity CombatTargetProxy
        {
            get => combatTargetProxy;
            set => combatTargetProxy = value;
        }
        public CombatFaction CombatFaction => CombatFaction.Mob;
        public EntityId ProjectileHitNodeId => gameObject.GetEntityId();
        public Vector2 CombatTargetPosition => combatTargetShapeCached
            ? (Vector2)transform.TransformPoint(cachedCombatTargetLocalOffset)
            : CombatTargetShapeUtility.Position(hurtbox, transform);
        public float CombatTargetRadius => combatTargetShapeCached
            ? cachedCombatTargetRadius
            : CombatTargetShapeUtility.Radius(hurtbox, targetRadius);
        public Vector2 CombatTargetHalfExtents => combatTargetShapeCached
            ? cachedCombatTargetHalfExtents
            : CombatTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
        public float CombatTargetRotationRadians => combatTargetShapeCached
            ? cachedCombatTargetRotationRadians
            : CombatTargetShapeUtility.RotationRadians(hurtbox);
        public CombatShapeType CombatTargetShapeType => combatTargetShapeCached
            ? cachedCombatTargetShapeType
            : CombatTargetShapeUtility.ShapeType(hurtbox);
        public int CombatTargetMask => combatTargetShapeCached
            ? cachedCombatTargetMask
            : 1 << hurtbox.gameObject.layer;
        public float CombatMaxHealth => health?.Max ?? statSheet.MaxHealth;
        public float CombatCurrentHealth => CurrentHealth;
        public float CombatHealthRegenPerSecond => health?.RegenPerSecond ?? statSheet.HealthRegenPerSecond;
        public float CombatMaxMana => mana?.Max ?? statSheet.MaxMana;
        public float CombatCurrentMana => CurrentMana;
        public float CombatManaRegenPerSecond => mana?.RegenPerSecond ?? statSheet.ManaRegenPerSecond;
        public bool IsCombatTargetActive => isActiveAndEnabled && isAlive && CurrentHealth > 0f;
        public bool CombatDespawnOnDeath => true;
        public bool IsAlive => isAlive;
        public bool HasPendingSpawnConfirmation { get; private set; }

        protected virtual void Awake()
        {
            ValidateReferences();

            skillDriver = GetComponent<SkillDriver>();
            targetId = ++nextTargetId;
            int seed = randomSeed != 0 ? randomSeed : unchecked(Environment.TickCount ^ targetId);
            random = new global::System.Random(seed);
            InitializeForSpawn();
        }

        protected virtual void Update()
        {
            using (UpdateMarker.Auto())
            {
                if (despawnPending)
                {
                    HandleDespawn();
                    return;
                }

                SyncResourceAuthoring();
                MirrorResourcesFromProxy();
                PushCombatTargetProxy();
                float deltaTime = Time.deltaTime;
                TickWander(deltaTime);
                using (ApplyVelocityMarker.Auto())
                {
                    body.linearVelocity = wanderVelocity;
                }

                DriveSkills();
            }
        }

        protected virtual void OnDisable()
        {
            PlayGround.System.Combat.Targets.CombatTargetProxy.Delete(this);
            UnregisterTargets();
        }

        public void Configure(
            Rigidbody2D mobBody,
            Collider2D mobBodyCollider,
            Collider2D mobHurtbox,
            SpriteRenderer renderer,
            Transform targetTransform)
        {
            body = mobBody;
            bodyCollider = mobBodyCollider;
            hurtbox = mobHurtbox;
            spriteRenderer = renderer;
            target = targetTransform;
            TryCacheCombatTargetShape();
        }

        public void ConfigureAuthoring(float health, float moveSpeed, float radius)
        {
            statSheet = ScriptableObject.CreateInstance<UnitStatSheet>();
            statSheet.SetRuntimeValues(health, moveSpeed);
            targetRadius = radius;
            TryCacheCombatTargetShape();
        }

        public void ConfigureWander(float minDuration, float maxDuration, float pauseChance, int seed = 0)
        {
            wanderMinDuration = Mathf.Max(0.05f, minDuration);
            wanderMaxDuration = Mathf.Max(wanderMinDuration, maxDuration);
            wanderPauseChance = Mathf.Clamp01(pauseChance);
            randomSeed = seed;
        }

        public void BindCombatRoot(CombatRoot root)
        {
            skillDriver?.BindCombatRoot(root);
        }

        public void BindVfxRoot(CombatVfxRoot root)
        {
            skillDriver?.BindVfxRoot(root);
        }

        public void Register(CombatTargetRegistry<ICombatTarget> targetRegistry)
        {
            if (targetRegistry == null || registries.Contains(targetRegistry))
            {
                return;
            }

            registries.Add(targetRegistry);
            targetRegistry.Register(this);
            skillDriver?.BindCaster(this);
        }

        public void Register(CombatTargetSet targetSet)
        {
            combatTargetSet = targetSet;
            combatTargetSet.Register(this);
        }

        public void SetTarget(Transform targetTransform)
        {
            target = targetTransform;
        }

        public void InitializeForSpawn()
        {
            isAlive = true;
            if (health == null)
            {
                health = new Resource(statSheet.MaxHealth, statSheet.HealthRegenPerSecond);
            }
            else
            {
                health.Reset(statSheet.MaxHealth, statSheet.HealthRegenPerSecond);
            }

            if (mana == null)
            {
                mana = new Resource(statSheet.MaxMana, statSheet.ManaRegenPerSecond);
            }
            else
            {
                mana.Reset(statSheet.MaxMana, statSheet.ManaRegenPerSecond);
            }
            statusSnapshots.Clear();
            CacheCombatTargetShape();

            if (bodyCollider != null)
            {
                bodyCollider.enabled = true;
            }

            if (hurtbox != null)
            {
                hurtbox.enabled = true;
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = true;
            }

            if (body != null)
            {
                body.simulated = true;
                body.linearVelocity = Vector2.zero;
            }

            wanderVelocity = Vector2.zero;
            PickNewWanderVelocity();
        }

        public void OnCombatSpawned(Entity proxy)
        {
            // Scene-placed mobs keep this harmless flag because only SpawnController consumes pooled confirmations.
            HasPendingSpawnConfirmation = true;
        }

        public void BeginLife()
        {
            HasPendingSpawnConfirmation = false;
            gameObject.SetActive(true);
            InitializeForSpawn();
        }

        public void OnCombatDespawned()
        {
            despawnPending = true;
        }

        public void ReceiveHit(in CombatHitData hit)
        {
            if (hit.DirectDamageEnabled)
                TakeDamage(hit.Damage);
        }

        public void ReceiveCombatTick(
            in CombatTickResult result,
            IReadOnlyList<StatusStackSnapshot> stacks)
        {
            if (result.HitCount > 0)
            {
                health.MirrorCurrent(result.Health);
            }

            if (result.StatusCount > 0)
            {
                ReceiveStatus(stacks);
            }
        }

        public void ReceiveStatus(IReadOnlyList<StatusStackSnapshot> stacks)
        {
            statusSnapshots.Clear();
            for (int i = 0; i < stacks.Count; i++)
            {
                statusSnapshots.Add(stacks[i]);
            }
        }

        public bool TakeDamage(DamageSnapshot damage)
        {
            if (!isAlive || damage.Amount <= 0f)
            {
                return false;
            }

            return true;
        }

        private void HandleDespawn()
        {
            despawnPending = false;
            isAlive = false;
            body.linearVelocity = Vector2.zero;
            wanderVelocity = Vector2.zero;
            body.simulated = false;
            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            hurtbox.enabled = false;
            spriteRenderer.enabled = false;
            UnregisterTargets();
            SoftDied?.Invoke(this);
        }

        private void UnregisterTargets()
        {
            for (int i = 0; i < registries.Count; i++)
            {
                registries[i]?.Unregister(this);
            }

            registries.Clear();
            combatTargetSet?.Unregister(this);
        }

        private void PushCombatTargetProxy()
        {
            if (combatTargetProxy != Entity.Null)
            {
                using (PushCombatTargetProxyMarker.Auto())
                {
                    PlayGround.System.Combat.Targets.CombatTargetProxy.Push(this);
                }
            }
        }

        private void SyncResourceAuthoring()
        {
            bool changed = health.UpdateAuthoring(statSheet.MaxHealth, statSheet.HealthRegenPerSecond);
            changed |= mana.UpdateAuthoring(statSheet.MaxMana, statSheet.ManaRegenPerSecond);
            if (changed)
            {
                PlayGround.System.Combat.Targets.CombatTargetProxy.PushResourceMaxes(this);
            }
        }

        public void ReceiveSpawnRejected(int castToken)
        {
            skillDriver?.ReceiveSpawnRejected(castToken);
        }

        private void MirrorResourcesFromProxy()
        {
            if (PlayGround.System.Combat.Targets.CombatTargetProxy.TryReadResources(this, out Health ecsHealth, out Mana ecsMana))
            {
                health.MirrorCurrent(ecsHealth.Current);
                mana.MirrorCurrent(ecsMana.Current);
            }
        }

        private void ValidateReferences()
        {
            if (body == null)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs a Rigidbody2D.");
            }

            if (bodyCollider == null)
            {
                bodyCollider = body.GetComponent<Collider2D>();
            }

            if (bodyCollider == null)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs a body collider.");
            }

            if (hurtbox == null)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs a hurtbox.");
            }

            if (spriteRenderer == null)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs a SpriteRenderer.");
            }

            if (statSheet == null)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs a {nameof(UnitStatSheet)}.");
            }
        }

        private void TryCacheCombatTargetShape()
        {
            if (hurtbox == null)
            {
                combatTargetShapeCached = false;
                return;
            }

            CacheCombatTargetShape();
        }

        private void CacheCombatTargetShape()
        {
            Vector2 worldPosition = CombatTargetShapeUtility.Position(hurtbox, transform);
            cachedCombatTargetLocalOffset = transform.InverseTransformPoint(worldPosition);
            cachedCombatTargetRadius = CombatTargetShapeUtility.Radius(hurtbox, targetRadius);
            cachedCombatTargetHalfExtents = CombatTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
            cachedCombatTargetRotationRadians = CombatTargetShapeUtility.RotationRadians(hurtbox);
            cachedCombatTargetShapeType = CombatTargetShapeUtility.ShapeType(hurtbox);
            cachedCombatTargetMask = 1 << hurtbox.gameObject.layer;
            combatTargetShapeCached = true;
        }

        private void TickWander(float deltaTime)
        {
            using (TickWanderMarker.Auto())
            {
                wanderTimer -= deltaTime;
                if (wanderTimer > 0f)
                {
                    return;
                }

                PickNewWanderVelocity();
            }
        }

        private void DriveSkills()
        {
            using (DriveSkillsMarker.Auto())
            {
                if (skillDriver == null)
                {
                    return;
                }

                if (TryAcquireEnemyTarget(out ICombatTarget enemy))
                {
                    Vector2 targetPosition;
                    Vector2 aimDirection;
                    using (AimTargetMarker.Auto())
                    {
                        targetPosition = enemy.CombatTargetPosition;
                        Vector2 toTarget = targetPosition - (Vector2)transform.position;
                        aimDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
                        spriteRenderer.flipX = aimDirection.x < 0f;
                    }

                    using (SkillTickMarker.Auto())
                    {
                        skillDriver.Tick(true, aimDirection, targetPosition);
                    }

                    return;
                }

                using (SkillTickMarker.Auto())
                {
                    skillDriver.Tick(false, Vector2.zero, (Vector2)transform.position);
                }
            }
        }

        private bool TryAcquireEnemyTarget(out ICombatTarget enemy)
        {
            using (AcquireEnemyTargetMarker.Auto())
            {
                for (int registryIndex = 0; registryIndex < registries.Count; registryIndex++)
                {
                    CombatTargetRegistry<ICombatTarget> registry = registries[registryIndex];
                    if (registry == null)
                    {
                        continue;
                    }

                    IReadOnlyList<ICombatTarget> targets = registry.Targets;
                    for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                    {
                        ICombatTarget targetCandidate = targets[targetIndex];
                        if (targetCandidate != null
                            && targetCandidate.IsCombatTargetActive
                            && targetCandidate.CombatFaction != CombatFaction)
                        {
                            enemy = targetCandidate;
                            return true;
                        }
                    }
                }

                enemy = null;
                return false;
            }
        }

        private void PickNewWanderVelocity()
        {
            float durationT = NextRandom01();
            wanderTimer = Mathf.Lerp(wanderMinDuration, Mathf.Max(wanderMinDuration, wanderMaxDuration), durationT);
            if (NextRandom01() < wanderPauseChance)
            {
                wanderVelocity = Vector2.zero;
                return;
            }

            float angle = NextRandom01() * Mathf.PI * 2f;
            wanderVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Speed;
        }

        private float NextRandom01()
        {
            random ??= new global::System.Random(unchecked(Environment.TickCount ^ targetId));
            return (float)random.NextDouble();
        }
    }
}
