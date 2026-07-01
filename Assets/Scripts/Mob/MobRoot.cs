using System;
using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Common;
using Unity.Entities;
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
        [SerializeField] private float speed = 2.5f;
        [SerializeField] private float maxHealth = 35f;
        [SerializeField] private float targetRadius = 0.5f;
        [SerializeField, Min(0.05f)] private float wanderMinDuration = 0.7f;
        [SerializeField, Min(0.05f)] private float wanderMaxDuration = 2f;
        [SerializeField, Range(0f, 1f)] private float wanderPauseChance = 0.15f;
        [SerializeField] private int randomSeed;

        private static int nextTargetId;
        private readonly List<StatusStackSnapshot> statusSnapshots = new();
        private readonly List<CombatTargetRegistry<ICombatTarget>> registries = new();
        private CombatTargetSet combatTargetSet;
        private Entity combatTargetProxy;
        private global::System.Random random;
        private Vector2 wanderVelocity;
        private float wanderTimer;
        private bool deleteProxyInLateUpdate;
        private int targetId;
        private bool isAlive = true;
        private bool softDeathNotified;

        public event Action<MobRoot> SoftDied;

        public float Speed => speed;
        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
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
        public Vector2 CombatTargetPosition => CombatTargetShapeUtility.Position(hurtbox, transform);
        public float CombatTargetRadius => CombatTargetShapeUtility.Radius(hurtbox, targetRadius);
        public Vector2 CombatTargetHalfExtents => CombatTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
        public float CombatTargetRotationRadians => CombatTargetShapeUtility.RotationRadians(hurtbox);
        public CombatShapeType CombatTargetShapeType => CombatTargetShapeUtility.ShapeType(hurtbox);
        public int CombatTargetMask => 1 << hurtbox.gameObject.layer;
        public float CombatMaxHealth => MaxHealth;
        public bool IsCombatTargetActive => isActiveAndEnabled && isAlive && CurrentHealth > 0f;

        protected virtual void Awake()
        {
            ValidateReferences();

            targetId = ++nextTargetId;
            CurrentHealth = Mathf.Max(1f, maxHealth);
            int seed = randomSeed != 0 ? randomSeed : unchecked(Environment.TickCount ^ targetId);
            random = new global::System.Random(seed);
            PickNewWanderVelocity();
        }

        protected virtual void Update()
        {
            if (!isAlive)
            {
                QueueCombatTargetProxyDelete();
                return;
            }

            PushCombatTargetProxy();
            float deltaTime = Time.deltaTime;
            TickWander(deltaTime);
            body.linearVelocity = wanderVelocity;
        }

        protected virtual void LateUpdate()
        {
            if (deleteProxyInLateUpdate)
            {
                DeleteCombatTargetProxy();
            }
        }

        protected virtual void OnDisable()
        {
            DeleteCombatTargetProxy();
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
        }

        public void ConfigureAuthoring(float health, float moveSpeed, float radius)
        {
            maxHealth = health;
            speed = moveSpeed;
            targetRadius = radius;
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
        }

        public void Register(CombatTargetRegistry<ICombatTarget> targetRegistry)
        {
            if (targetRegistry == null || registries.Contains(targetRegistry))
            {
                return;
            }

            registries.Add(targetRegistry);
            targetRegistry.Register(this);
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
                ApplyCombatHealth(result.Health);
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

        private void ApplyCombatHealth(float health)
        {
            if (!isAlive)
            {
                return;
            }

            CurrentHealth = health;
            if (CurrentHealth <= 0f)
            {
                SoftDie();
                return;
            }
        }

        public void SoftDie()
        {
            if (softDeathNotified)
            {
                return;
            }

            isAlive = false;
            CurrentHealth = 0f;
            body.linearVelocity = Vector2.zero;
            wanderVelocity = Vector2.zero;
            body.simulated = false;
            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            hurtbox.enabled = false;
            spriteRenderer.enabled = false;
            QueueCombatTargetProxyDelete();
            UnregisterTargets();
            softDeathNotified = true;
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
                PlayGround.System.Common.CombatTargetProxy.Push(this);
            }
        }

        private void QueueCombatTargetProxyDelete()
        {
            if (combatTargetProxy != Entity.Null)
            {
                deleteProxyInLateUpdate = true;
            }
        }

        private void DeleteCombatTargetProxy()
        {
            PlayGround.System.Common.CombatTargetProxy.Delete(this);
            deleteProxyInLateUpdate = false;
            if (softDeathNotified && this != null)
            {
                Destroy(gameObject);
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
        }

        private void TickWander(float deltaTime)
        {
            wanderTimer -= deltaTime;
            if (wanderTimer > 0f)
            {
                return;
            }

            PickNewWanderVelocity();
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
            wanderVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
        }

        private float NextRandom01()
        {
            random ??= new global::System.Random(unchecked(Environment.TickCount ^ targetId));
            return (float)random.NextDouble();
        }
    }
}
