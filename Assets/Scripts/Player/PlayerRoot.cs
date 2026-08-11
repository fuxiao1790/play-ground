using PlayGround.Common;
using PlayGround.Common.Stats;
using PlayGround.Common.StatusEffects;
using PlayGround.Game;
using PlayGround.Persistence;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayGround.Player
{
    public sealed class PlayerRoot : MonoBehaviour, ICombatTarget
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Collider2D hurtbox;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private UnitStatSheet statSheet;
        [SerializeField] private SpriteFacingSet facingSet;
        [SerializeField] private PauseController pauseController;
        [SerializeField] private float stopThreshold = 0.1f;
        [SerializeField] private float accelerationMultiplier = 8f;
        [SerializeField] private float frictionMultiplier = 6f;
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float dashCooldown = 0.45f;
        [SerializeField] private float hurtFlashSeconds = 0.08f;
        [SerializeField] private float targetRadius = 0.45f;

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction pointAction;
        private InputAction dashAction;
        private InputActionMap playerMap;
        private PlayerMovement movement;
        private PlayerFacing facing;
        private PlayGround.Skills.SkillDriver skillDriver;
        private PlayerAnimatorDriver animatorDriver;
        private PlayerStateDriver stateDriver;
        private Resource health;
        private Resource mana;
        private bool requestHurtOnHealthChanged;
        private bool deathHandled;
        private static int nextTargetId;
        private readonly List<StatusStackSnapshot> statusSnapshots = new();
        private readonly List<CombatTargetRegistry<ICombatTarget>> registries = new();
        private CombatTargetSet combatTargetSet;
        private Entity combatTargetProxy;
        private bool hasRegisteredProxy;
        private bool deleteProxyInLateUpdate;
        private int targetId;
        private IGameplayInputSource fireInput;
        private GameplayInputBlock gameplayInputBlocks;
        public StatusEffects StatusEffects { get; private set; }

        public Vector2 AimDirection => facing?.AimDirection ?? Vector2.right;
        public int TargetId => targetId;
        public Entity CombatTargetProxy
        {
            get => combatTargetProxy;
            set => combatTargetProxy = value;
        }
        public CombatFaction CombatFaction => CombatFaction.Player;
        public EntityId ProjectileHitNodeId => gameObject.GetEntityId();
        public Vector2 CombatTargetPosition => ProjectileTargetShapeUtility.Position(hurtbox, transform);
        public float CombatTargetRadius => ProjectileTargetShapeUtility.Radius(hurtbox, targetRadius);
        public Vector2 CombatTargetHalfExtents => ProjectileTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
        public float CombatTargetRotationRadians => ProjectileTargetShapeUtility.RotationRadians(hurtbox);
        public CombatShapeType CombatTargetShapeType => ProjectileTargetShapeUtility.ShapeType(hurtbox);
        public int CombatTargetMask => 1 << hurtbox.gameObject.layer;
        public float CombatMaxHealth => health?.Max ?? statSheet.MaxHealth;
        public float CombatCurrentHealth => CurrentHealth;
        public float CombatHealthRegenPerSecond => health?.RegenPerSecond ?? statSheet.HealthRegenPerSecond;
        public float CombatMaxMana => mana?.Max ?? statSheet.MaxMana;
        public float CombatCurrentMana => CurrentMana;
        public float CombatManaRegenPerSecond => mana?.RegenPerSecond ?? statSheet.ManaRegenPerSecond;
        public bool IsCombatTargetActive => isActiveAndEnabled && health != null && !health.IsDepleted;
        public float CurrentHealth => health?.Current ?? 0f;
        public float CurrentMana => mana?.Current ?? 0f;
        public int EquippedAttackCount => skillDriver?.SlotCount ?? 0;
        public IReadOnlyList<StatusStackSnapshot> StatusSnapshots => statusSnapshots;
        public bool GameplayInputBlocked => gameplayInputBlocks != GameplayInputBlock.None;

        public void SetFireInput(IGameplayInputSource source) => fireInput = source;

        public void ClearFireInput(IGameplayInputSource source)
        {
            if (fireInput == source) fireInput = null;
        }

        public void SetGameplayInputBlocked(GameplayInputBlock reason, bool blocked)
        {
            if (blocked)
            {
                gameplayInputBlocks |= reason;
            }
            else
            {
                gameplayInputBlocks &= ~reason;
            }
        }

        private void Awake()
        {
            if (inputActions == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs InputSystem_Actions.");

            if (body == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a Rigidbody2D.");

            if (bodyCollider == null)
                bodyCollider = body.GetComponent<Collider2D>();

            if (hurtbox == null)
                hurtbox = bodyCollider;

            if (spriteRenderer == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a SpriteRenderer.");

            if (worldCamera == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a world camera.");

            if (statSheet == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a {nameof(UnitStatSheet)}.");

            if (facingSet == null || !facingSet.HasAllSprites)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a fully assigned {nameof(SpriteFacingSet)}.");

            if (pauseController == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a {nameof(PauseController)}.");

            playerMap = inputActions.FindActionMap("Player", true);
            moveAction = playerMap.FindAction("Move", true);
            lookAction = playerMap.FindAction("Look", false);
            dashAction = playerMap.FindAction("Jump", true);
            pointAction = inputActions.FindAction("UI/Point", false);
            playerMap.Enable();
            pointAction?.Enable();

            targetId = ++nextTargetId;
            animatorDriver = new PlayerAnimatorDriver(animator, spriteRenderer);
            movement = new PlayerMovement(
                body,
                statSheet.MoveSpeed,
                stopThreshold,
                accelerationMultiplier,
                frictionMultiplier,
                dashSpeed,
                dashDuration,
                dashCooldown);
            facing = new PlayerFacing(transform, spriteRenderer, facingSet);
            stateDriver = new PlayerStateDriver(movement, animatorDriver);
            health = new Resource(statSheet.MaxHealth, statSheet.HealthRegenPerSecond);
            mana = new Resource(statSheet.MaxMana, statSheet.ManaRegenPerSecond);
            health.Depleted += HandleHealthDepleted;
            health.Changed += HandleHealthChanged;
            skillDriver = GetComponent<PlayGround.Skills.SkillDriver>();

            if (skillDriver == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} requires a {nameof(PlayGround.Skills.SkillDriver)} component.");

            StatusEffects = GetComponent<StatusEffects>();
            StatusEffects?.Initialize(damage => RequestHurt(damage), () => !health.IsDepleted);
        }

        private void OnEnable()
        {
            playerMap?.Enable();
            pointAction?.Enable();
            pauseController.PausedChanged += OnPausedChanged;
            OnPausedChanged(pauseController.IsPaused);
        }

        private void OnDisable()
        {
            pauseController.PausedChanged -= OnPausedChanged;
            DeleteCombatTargetProxy();
            pointAction?.Disable();
            playerMap?.Disable();
            for (int i = 0; i < registries.Count; i++)
            {
                registries[i]?.Unregister(this);
            }

            registries.Clear();
            combatTargetSet?.Unregister(this);
        }

        private void Start()
        {
            if (fireInput == null)
            {
                Debug.LogWarning($"{nameof(PlayerRoot)} on {name} has no {nameof(IGameplayInputSource)} registered; player cannot fire.");
            }
        }

        private void Update()
        {
            SyncResourceAuthoring();
            MirrorResourcesFromProxy();
            if (health.IsDepleted)
            {
                QueueCombatTargetProxyDelete();
                return;
            }

            PushCombatTargetProxy();

            Vector2 move = ReadMoveInput();
            Vector2 aimWorldPosition = ReadAimWorldPosition();
            movement.SetMoveInput(move);
            if (ReadDashPressedThisFrame())
                movement.TryStartDash(aimWorldPosition);

            if (!GameplayInputBlocked)
                facing.AimAt(aimWorldPosition);

            bool fireHeld = !GameplayInputBlocked && (fireInput?.FireHeld ?? false);
            skillDriver.Tick(fireHeld, facing.AimDirection, aimWorldPosition);
            animatorDriver.Tick(Time.deltaTime);
            stateDriver.Tick();
        }

        private void LateUpdate()
        {
            if (deleteProxyInLateUpdate)
            {
                DeleteCombatTargetProxy();
            }
        }

        private void FixedUpdate()
        {
            if (health.IsDepleted)
                return;

            movement.FixedTick();
            StatusEffects?.Tick(Time.fixedDeltaTime);
        }

        public void Configure(InputActionAsset actions, Rigidbody2D playerBody, Collider2D playerHurtbox, SpriteRenderer renderer, Camera camera)
        {
            inputActions = actions;
            body = playerBody;
            bodyCollider = playerBody.GetComponent<Collider2D>();
            hurtbox = playerHurtbox;
            spriteRenderer = renderer;
            worldCamera = camera;
        }

        public void Register(CombatTargetRegistry<ICombatTarget> targetRegistry)
        {
            if (targetRegistry == null || registries.Contains(targetRegistry))
            {
                return;
            }

            registries.Add(targetRegistry);
            targetRegistry.Register(this);
            hasRegisteredProxy = true;
            skillDriver?.BindCaster(this);
        }

        public void Register(CombatTargetSet targetSet)
        {
            combatTargetSet = targetSet;
            combatTargetSet.Register(this);
        }

        public void ReceiveHit(in CombatHitData hit)
        {
            if (hit.DirectDamageEnabled)
            {
                RequestHurt(hit.Damage);
            }
        }

        public void ReceiveCombatTick(
            in CombatTickResult result,
            IReadOnlyList<StatusStackSnapshot> stacks)
        {
            if (result.HitCount > 0)
            {
                requestHurtOnHealthChanged = result.DamageTaken > 0f;
                health.MirrorCurrent(result.Health);
                requestHurtOnHealthChanged = false;
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

        public PlayerStateSaveData CapturePersistentState()
        {
            Vector2 position = body != null ? body.position : (Vector2)transform.position;
            return new PlayerStateSaveData
            {
                positionX = position.x,
                positionY = position.y,
                currentHealth = CurrentHealth
            };
        }

        public bool RestorePersistentState(PlayerStateSaveData savedState)
        {
            if (savedState == null || !savedState.IsValid() || health == null)
            {
                return false;
            }

            Vector2 position = new(savedState.positionX, savedState.positionY);
            body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            deathHandled = false;
            health.MirrorCurrent(savedState.currentHealth <= 0f ? health.Max : savedState.currentHealth);
            RestorePresentationAfterLoad();
            deleteProxyInLateUpdate = false;
            PlayGround.System.Combat.Targets.CombatTargetProxy.SetHealth(this, health.Current);
            PushCombatTargetProxy();
            return true;
        }

        private void PushCombatTargetProxy()
        {
            if (combatTargetProxy != Entity.Null)
            {
                PlayGround.System.Combat.Targets.CombatTargetProxy.Push(this);
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

        private bool RequestHurt(DamageSnapshot damage)
        {
            if (health.IsDepleted || damage.Amount <= 0f)
            {
                return false;
            }

            animatorDriver.RequestHurt(hurtFlashSeconds);
            return true;
        }

        private void HandleHealthChanged()
        {
            if (requestHurtOnHealthChanged && !health.IsDepleted)
            {
                animatorDriver.RequestHurt(hurtFlashSeconds);
            }
        }

        private void HandleHealthDepleted()
        {
            if (deathHandled)
            {
                return;
            }

            deathHandled = true;
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            if (hurtbox != null)
            {
                hurtbox.enabled = false;
            }

            spriteRenderer.enabled = false;
            QueueCombatTargetProxyDelete();
        }

        private void RestorePresentationAfterLoad()
        {
            body.linearVelocity = Vector2.zero;
            body.simulated = true;
            if (bodyCollider != null)
            {
                bodyCollider.enabled = true;
            }

            if (hurtbox != null)
            {
                hurtbox.enabled = true;
            }

            spriteRenderer.enabled = true;
        }

        private void QueueCombatTargetProxyDelete()
        {
            if (hasRegisteredProxy)
            {
                deleteProxyInLateUpdate = true;
            }
        }

        private void DeleteCombatTargetProxy()
        {
            PlayGround.System.Combat.Targets.CombatTargetProxy.Delete(this);
            deleteProxyInLateUpdate = false;
        }

        private void OnPausedChanged(bool paused) =>
            SetGameplayInputBlocked(GameplayInputBlock.Paused, paused);

        private Vector2 ReadMoveInput() =>
            GameplayInputBlocked ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

        private bool ReadDashPressedThisFrame() =>
            !GameplayInputBlocked && dashAction.WasPressedThisFrame();

        private Vector2 ReadAimWorldPosition()
        {
            if (pointAction != null && pointAction.controls.Count > 0)
            {
                Vector2 screenPosition = pointAction.ReadValue<Vector2>();
                Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -worldCamera.transform.position.z));
                return world;
            }

            Vector2 look = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;
            if (look.sqrMagnitude > 0.0001f)
                return (Vector2)transform.position + look.normalized;

            return (Vector2)transform.position + AimDirection;
        }
    }
}
