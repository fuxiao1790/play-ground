using PlayGround.Common;
using PlayGround.Common.Stats;
using PlayGround.Common.StatusEffects;
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
        private InputAction attackAction;
        private InputAction dashAction;
        private InputActionMap playerMap;
        private PlayerMovement movement;
        private PlayerFacing facing;
        private PlayGround.Skills.SkillDriver skillDriver;
        private PlayerAnimatorDriver animatorDriver;
        private PlayerStateDriver stateDriver;
        private PlayerHealth health;
        private static int nextTargetId;
        private readonly List<StatusStackSnapshot> statusSnapshots = new();
        private readonly List<CombatTargetRegistry<ICombatTarget>> registries = new();
        private CombatTargetSet combatTargetSet;
        private Entity combatTargetProxy;
        private bool deleteProxyInLateUpdate;
        private int targetId;
        private bool gameplayInputBlocked;
        private bool pointerOverSkillUi;
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
        public float CombatMaxHealth => statSheet.MaxHealth;
        public bool IsCombatTargetActive => isActiveAndEnabled && health != null && health.IsAlive;
        public float CurrentHealth => health?.CurrentHealth ?? 0f;
        public int EquippedAttackCount => skillDriver?.SlotCount ?? 0;
        public IReadOnlyList<StatusStackSnapshot> StatusSnapshots => statusSnapshots;

        public void SetGameplayInputGate(bool modalOpen, bool pointerOverUi)
        {
            gameplayInputBlocked = modalOpen;
            pointerOverSkillUi = pointerOverUi;
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

            playerMap = inputActions.FindActionMap("Player", true);
            moveAction = playerMap.FindAction("Move", true);
            lookAction = playerMap.FindAction("Look", false);
            attackAction = playerMap.FindAction("Attack", true);
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
            health = new PlayerHealth(body, bodyCollider, hurtbox, spriteRenderer, animatorDriver, statSheet.MaxHealth, hurtFlashSeconds);
            skillDriver = GetComponent<PlayGround.Skills.SkillDriver>();

            if (skillDriver == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} requires a {nameof(PlayGround.Skills.SkillDriver)} component.");

            StatusEffects = GetComponent<StatusEffects>();
            StatusEffects?.Initialize(d => health.TakeDamage(d), () => health.IsAlive);
        }

        private void OnEnable()
        {
            playerMap?.Enable();
            pointAction?.Enable();
        }

        private void OnDisable()
        {
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

        private void Update()
        {
            if (!health.IsAlive)
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

            facing.AimAt(aimWorldPosition);
            skillDriver.Tick(ReadAttackHeld(), facing.AimDirection, aimWorldPosition);
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
            if (!health.IsAlive)
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
                health.TakeDamage(hit.Damage);
                if (!health.IsAlive)
                {
                    QueueCombatTargetProxyDelete();
                }
            }
        }

        public void ReceiveCombatTick(
            in CombatTickResult result,
            IReadOnlyList<StatusStackSnapshot> stacks)
        {
            if (result.HitCount > 0)
            {
                health.MirrorCombatHealth(result.Health, result.DamageTaken > 0f);
                if (!health.IsAlive)
                {
                    QueueCombatTargetProxyDelete();
                }
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

        private void PushCombatTargetProxy()
        {
            if (combatTargetProxy != Entity.Null)
            {
                PlayGround.System.Combat.Targets.CombatTargetProxy.Push(this);
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
            PlayGround.System.Combat.Targets.CombatTargetProxy.Delete(this);
            deleteProxyInLateUpdate = false;
        }

        private Vector2 ReadMoveInput() =>
            gameplayInputBlocked ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

        private bool ReadAttackHeld() =>
            !gameplayInputBlocked && !pointerOverSkillUi && attackAction.IsPressed();

        private bool ReadDashPressedThisFrame() =>
            !gameplayInputBlocked && dashAction.WasPressedThisFrame();

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
