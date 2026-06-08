using PlayGround.Common;
using PlayGround.Common.StatusEffects;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayGround.Player
{
    public sealed class PlayerRoot : MonoBehaviour, IProjectileTarget, IAoeTarget
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Collider2D hurtbox;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private float moveSpeed = 7f;
        [SerializeField] private float stopThreshold = 0.1f;
        [SerializeField] private float accelerationMultiplier = 8f;
        [SerializeField] private float frictionMultiplier = 6f;
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float dashCooldown = 0.45f;
        [SerializeField] private float maxHealth = 500f;
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
        private PlayGround.Skills.PlayerSkillDriver skillDriver;
        private PlayerAnimatorDriver animatorDriver;
        private PlayerStateDriver stateDriver;
        private PlayerHealth health;
        private static int nextTargetId;
        private ProjectileTargetRegistry registry;
        private AoeTargetRegistry aoeRegistry;
        private int targetId;
        public StatusEffects StatusEffects { get; private set; }

        public Vector2 AimDirection => facing?.AimDirection ?? Vector2.right;
        public int TargetId => targetId;
        public EntityId ProjectileHitNodeId => gameObject.GetEntityId();
        public Vector2 ProjectileTargetPosition => ProjectileTargetShapeUtility.Position(hurtbox, transform);
        public float ProjectileTargetRadius => ProjectileTargetShapeUtility.Radius(hurtbox, targetRadius);
        public Vector2 ProjectileTargetHalfExtents => ProjectileTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
        public float ProjectileTargetRotationRadians => ProjectileTargetShapeUtility.RotationRadians(hurtbox);
        public CombatShapeType ProjectileTargetShapeType => ProjectileTargetShapeUtility.ShapeType(hurtbox);
        public int ProjectileTargetMask => 1 << hurtbox.gameObject.layer;
        public bool IsProjectileTargetActive => isActiveAndEnabled && health != null && health.IsAlive;
        public Vector2 AoeTargetPosition => ProjectileTargetPosition;
        public float AoeTargetRadius => ProjectileTargetRadius;
        public Vector2 AoeTargetHalfExtents => ProjectileTargetHalfExtents;
        public float AoeTargetRotationRadians => ProjectileTargetRotationRadians;
        public CombatShapeType AoeTargetShapeType => ProjectileTargetShapeType;
        public int AoeTargetMask => ProjectileTargetMask;
        public bool IsAoeTargetActive => IsProjectileTargetActive;
        public float CurrentHealth => health?.CurrentHealth ?? 0f;
        public int EquippedAttackCount => skillDriver?.SlotCount ?? 0;

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
                moveSpeed,
                stopThreshold,
                accelerationMultiplier,
                frictionMultiplier,
                dashSpeed,
                dashDuration,
                dashCooldown);
            facing = new PlayerFacing(transform, spriteRenderer);
            stateDriver = new PlayerStateDriver(movement, animatorDriver);
            health = new PlayerHealth(body, bodyCollider, hurtbox, spriteRenderer, animatorDriver, maxHealth, hurtFlashSeconds);
            skillDriver = GetComponent<PlayGround.Skills.PlayerSkillDriver>();

            if (skillDriver == null)
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} requires a {nameof(PlayGround.Skills.PlayerSkillDriver)} component.");

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
            pointAction?.Disable();
            playerMap?.Disable();
            registry?.Unregister(this);
            aoeRegistry?.Unregister(this);
        }

        private void Update()
        {
            if (!health.IsAlive)
                return;

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

        public void Register(ProjectileTargetRegistry targetRegistry)
        {
            registry = targetRegistry;
            registry.Register(this);
        }

        public void Register(AoeTargetRegistry targetRegistry)
        {
            aoeRegistry = targetRegistry;
            aoeRegistry.Register(this);
        }

        public void ReceiveProjectileHit(DamageSnapshot damage)
        {
            health.TakeDamage(damage);
        }

        public void ReceiveProjectileHitPayload(
            in ProjectileHitPayload payload,
            in ProjectileHitContext context,
            ProjectileHitActorRole role)
        {
            if (role == ProjectileHitActorRole.Target && payload.DirectDamageEnabled)
                health.TakeDamage(context.Damage);
        }

        public void ReceiveAoeHit(DamageSnapshot damage)
        {
            health.TakeDamage(damage);
        }

        private Vector2 ReadMoveInput() =>
            Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

        private bool ReadAttackHeld() =>
            attackAction.IsPressed();

        private bool ReadDashPressedThisFrame() =>
            dashAction.WasPressedThisFrame();

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
