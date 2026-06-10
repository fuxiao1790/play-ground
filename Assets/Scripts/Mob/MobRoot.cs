using System;
using PlayGround.Skills;
using PlayGround.Common;
using PlayGround.Common.StatusEffects;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Mob
{
    public class MobRoot : MonoBehaviour, IProjectileTarget, IAoeTarget
    {
        public const string DefaultTriggerKey = "on_spawn";

        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Collider2D hurtbox;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform target;
        [SerializeField] private float speed = 40f;
        [SerializeField] private float maxHealth = 35f;
        [SerializeField] private float hurtFlashSeconds = 0.08f;
        [SerializeField] private float targetRadius = 0.5f;
        [SerializeField] private MobBehaviour[] behaviours = Array.Empty<MobBehaviour>();
        [SerializeField] private MobTrigger[] triggers = Array.Empty<MobTrigger>();
        [SerializeField] private MobTriggerBehaviourMapping[] triggerBehaviourMap = Array.Empty<MobTriggerBehaviourMapping>();
        [SerializeField] private bool projectileAttackEnabled;
        [SerializeField] private ProjectileRoot projectileRoot;
        [SerializeField] private float projectileCooldown = 1.4f;
        [SerializeField] private float projectileRange = 130f;
        [SerializeField] private float projectileSpawnOffset = 12f;
        [SerializeField] private float projectileSpeed = 220f;
        [SerializeField] private float projectileLifetime = 1.8f;
        [SerializeField] private float projectileDamage = 1f;
        [SerializeField] private float projectileRadius = 0.25f;
        [SerializeField] private CombatShapeType projectileShapeType = CombatShapeType.Circle;
        [SerializeField] private BasicAttackPrefab projectileBasicPrefab;

        private static int nextTargetId;
        private readonly MobDebuffStackState debuffStacks = new();
        public StatusEffects StatusEffects { get; private set; }
        private readonly MobBlackboard blackboard = new();
        private CombatTargetRegistry<IProjectileTarget> registry;
        private CombatTargetRegistry<IAoeTarget> aoeRegistry;
        private AoeRoot aoeRoot;
        private MobEventQueue eventQueue;
        private MobStateDriver stateDriver;
        private MobBehaviourSelector behaviourSelector;
        private MobAnimatorDriver animatorDriver;
        private MobProjectileAttack projectileAttack;
        private int targetId;
        private bool isAlive = true;
        private bool softDeathNotified;

        public event Action<MobRoot> SoftDied;

        public float Speed => speed;
        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
        public MobBehaviourState BehaviourState => stateDriver?.CurrentState ?? MobBehaviourState.Idle;
        public Transform Target => target;
        public MobBlackboard Blackboard => blackboard;
        public int TargetId => targetId;
        public EntityId ProjectileHitNodeId => gameObject.GetEntityId();
        public Vector2 CombatTargetPosition => ProjectileTargetShapeUtility.Position(hurtbox, transform);
        public float CombatTargetRadius => ProjectileTargetShapeUtility.Radius(hurtbox, targetRadius);
        public Vector2 CombatTargetHalfExtents => ProjectileTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
        public float CombatTargetRotationRadians => ProjectileTargetShapeUtility.RotationRadians(hurtbox);
        public CombatShapeType CombatTargetShapeType => ProjectileTargetShapeUtility.ShapeType(hurtbox);
        public int CombatTargetMask => 1 << hurtbox.gameObject.layer;
        public bool IsCombatTargetActive => isActiveAndEnabled && isAlive && CurrentHealth > 0f;

        protected virtual void Awake()
        {
            ValidateReferences();
            behaviours = CloneRuntimeAssets(behaviours);
            triggers = CloneRuntimeAssets(triggers);

            targetId = ++nextTargetId;
            CurrentHealth = Mathf.Max(1f, maxHealth);
            eventQueue = new MobEventQueue();
            behaviourSelector = new MobBehaviourSelector();
            behaviourSelector.Configure(behaviours, triggerBehaviourMap);
            stateDriver = new MobStateDriver(blackboard);
            animatorDriver = new MobAnimatorDriver(animator, spriteRenderer);
            blackboard.Target = target;
            blackboard.Health = CurrentHealth;
            blackboard.MaxHealth = MaxHealth;

            RebuildProjectileAttack();
            StatusEffects = GetComponent<StatusEffects>();
            if (StatusEffects != null)
            {
                StatusEffects.Initialize(d => TakeDamage(d), () => isAlive);
                StatusEffects.EffectTriggered += OnStatusEffectTriggered;
            }
        }

        protected virtual void Update()
        {
            if (!isAlive)
            {
                return;
            }

            animatorDriver.Tick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (!isAlive)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            blackboard.BeginFrame(DefaultTriggerKey);
            blackboard.Target = target != null ? target : blackboard.Target;
            blackboard.Health = CurrentHealth;
            blackboard.BehaviourState = stateDriver.CurrentState;

            for (int i = 0; i < triggers.Length; i++)
            {
                triggers[i].UpdateTrigger(deltaTime, this, blackboard, eventQueue);
            }

            StatusEffects?.Tick(deltaTime);
            eventQueue.PushType(MobEventType.Tick, this);
            stateDriver.Update(eventQueue.Drain());
            blackboard.BehaviourState = stateDriver.CurrentState;
            animatorDriver.RequestState(stateDriver.CurrentState);

            if (stateDriver.CurrentState == MobBehaviourState.Dead)
            {
                SoftDie();
                return;
            }

            projectileAttack?.Update(deltaTime, blackboard.Target);

            Vector2 velocity = stateDriver.CurrentState == MobBehaviourState.Hurt
                ? Vector2.zero
                : behaviourSelector.Update(deltaTime, this, blackboard, stateDriver.CurrentState);
            body.linearVelocity = velocity;
        }

        protected virtual void OnDisable()
        {
            registry?.Unregister(this);
            aoeRegistry?.Unregister(this);
        }

        protected virtual void OnDestroy()
        {
            if (StatusEffects != null)
            {
                StatusEffects.EffectTriggered -= OnStatusEffectTriggered;
            }
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

        public void ConfigureAuthoring(
            MobBehaviour[] mobBehaviours,
            MobTrigger[] mobTriggers,
            MobTriggerBehaviourMapping[] mobTriggerBehaviourMap,
            float health,
            float moveSpeed,
            float radius)
        {
            behaviours = mobBehaviours;
            triggers = mobTriggers;
            triggerBehaviourMap = mobTriggerBehaviourMap;
            maxHealth = health;
            speed = moveSpeed;
            targetRadius = radius;
        }

        public void ConfigureProjectileAttack(
            ProjectileRoot root,
            float cooldown,
            float range,
            float spawnOffset,
            float attackSpeed,
            float lifetime,
            float damage,
            float radius,
            BasicAttackPrefab basicPrefab = null)
        {
            projectileAttackEnabled = true;
            projectileRoot = root;
            projectileCooldown = cooldown;
            projectileRange = range;
            projectileSpawnOffset = spawnOffset;
            projectileSpeed = attackSpeed;
            projectileLifetime = lifetime;
            projectileDamage = damage;
            projectileRadius = radius;
            projectileBasicPrefab = basicPrefab;
            RebuildProjectileAttack();
        }

        public void BindProjectileRoot(ProjectileRoot root)
        {
            projectileRoot = root;
            RebuildProjectileAttack();
        }

        public void BindAoeRoot(AoeRoot root)
        {
            aoeRoot = root;
        }

        public void Register(CombatTargetRegistry<IProjectileTarget> targetRegistry)
        {
            registry = targetRegistry;
            registry.Register(this);
        }

        public void Register(CombatTargetRegistry<IAoeTarget> targetRegistry)
        {
            aoeRegistry = targetRegistry;
            aoeRegistry.Register(this);
        }

        public void SetTarget(Transform targetTransform)
        {
            target = targetTransform;
            blackboard.Target = targetTransform;
        }

        public void ReceiveProjectileHit(DamageSnapshot damage)
        {
            TakeDamage(damage);
        }

        public void ReceiveProjectileHitPayload(
            in ProjectileHitPayload payload,
            in ProjectileHitContext context,
            ProjectileHitActorRole role)
        {
            if (role != ProjectileHitActorRole.Target)
                return;

            if (payload.DirectDamageEnabled)
                TakeDamage(context.Damage);

            if (payload.StackEffect.Enabled)
                ApplyStackEffect(payload.StackEffect);
        }

        private void ApplyStackEffect(ProjectileStackEffectSnapshot effect)
        {
            var status = (MobDebuffStatus)effect.DebuffStatusId;
            bool triggered = debuffStacks.AddStacks(status, Mathf.Max(1, effect.StacksPerHit), Mathf.Max(1, effect.StackThreshold));
            if (!triggered || aoeRoot == null || effect.AoeTypeId < 0)
                return;

            debuffStacks.ClearStacks(status);
            aoeRoot.Spawn(new AoeSpawnCommand(
                effect.AoeTypeId,
                transform.position,
                aoeRoot.TargetMask,
                new DamageSnapshot(Mathf.Max(0f, effect.AoeDamage)),
                effect.AoeLifetimeSeconds,
                effect.AoeTickIntervalSeconds));
        }

        public void ReceiveAoeHit(DamageSnapshot damage)
        {
            TakeDamage(damage);
        }

        public bool TakeDamage(DamageSnapshot damage)
        {
            if (!isAlive || damage.Amount <= 0f)
            {
                return false;
            }

            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage.Amount);
            blackboard.Health = CurrentHealth;
            if (CurrentHealth <= 0f)
            {
                eventQueue.PushType(MobEventType.Died, this);
                SoftDie();
                return true;
            }

            animatorDriver.RequestHurt(hurtFlashSeconds);
            eventQueue.PushType(damage.IsCrit ? MobEventType.CritDamaged : MobEventType.Damaged, this, damage.Amount);
            return true;
        }

        public bool AddDebuffStacks(MobDebuffStatus status, int amount, int threshold)
        {
            return debuffStacks.AddStacks(status, amount, threshold);
        }

        public int GetDebuffStackCount(MobDebuffStatus status)
        {
            return debuffStacks.GetStackCount(status);
        }

        public void ClearDebuffStacks(MobDebuffStatus status)
        {
            debuffStacks.ClearStacks(status);
        }

        private void OnStatusEffectTriggered(StatusEffectDef def, StatusEffectTriggerResult result)
        {
            if (def is not StackingTriggerDef triggerDef
                || triggerDef.TriggerAoeConfig == null
                || aoeRoot == null
                || !result.Triggered)
            {
                return;
            }

            int typeId = aoeRoot.RegisterConfig(triggerDef.TriggerAoeConfig);
            float damagePerFire = result.TriggerCount > 0
                ? result.TotalTriggerDamage / result.TriggerCount
                : 0f;

            for (int i = 0; i < result.TriggerCount; i++)
            {
                aoeRoot.Spawn(new ProjectileAoeSpawnRequest(
                    typeId,
                    result.OwnerPosition,
                    new DamageSnapshot(Mathf.Max(0f, damagePerFire)),
                    triggerDef.TriggerAoeLifetimeSeconds,
                    triggerDef.TriggerAoeTickIntervalSeconds));
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
            body.simulated = false;
            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            hurtbox.enabled = false;
            spriteRenderer.enabled = false;
            registry?.Unregister(this);
            aoeRegistry?.Unregister(this);
            softDeathNotified = true;
            SoftDied?.Invoke(this);
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

            if (behaviours == null || behaviours.Length == 0)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs behaviours.");
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null)
                {
                    throw new MissingReferenceException($"{nameof(MobRoot)} on {name} behaviour slot {i} is empty.");
                }
            }

            if (triggers == null || triggers.Length == 0)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs triggers.");
            }

            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i] == null)
                {
                    throw new MissingReferenceException($"{nameof(MobRoot)} on {name} trigger slot {i} is empty.");
                }
            }

            if (triggerBehaviourMap == null || triggerBehaviourMap.Length == 0)
            {
                throw new MissingReferenceException($"{nameof(MobRoot)} on {name} needs trigger behaviour mappings.");
            }
        }

        private void RebuildProjectileAttack()
        {
            projectileRoot ??= FindTaggedProjectileRoot();
            if (!projectileAttackEnabled || projectileRoot == null)
            {
                projectileAttack = null;
                return;
            }

            projectileAttack = new MobProjectileAttack(
                this,
                projectileRoot,
                projectileCooldown,
                projectileRange,
                projectileSpawnOffset,
                projectileSpeed,
                projectileLifetime,
                projectileDamage,
                projectileRadius,
                projectileShapeType,
                projectileBasicPrefab);
        }

        private static T[] CloneRuntimeAssets<T>(T[] assets)
            where T : ScriptableObject
        {
            var clones = new T[assets.Length];
            for (int i = 0; i < assets.Length; i++)
            {
                clones[i] = Instantiate(assets[i]);
            }

            return clones;
        }

        private static ProjectileRoot FindTaggedProjectileRoot()
        {
            try
            {
                GameObject rootObject = GameObject.FindWithTag(GameplayTags.MobProjectileRoot);
                return rootObject != null ? rootObject.GetComponent<ProjectileRoot>() : null;
            }
            catch (UnityException)
            {
                return null;
            }
        }
    }
}
