using PlayGround.Attack;
using PlayGround.Common;
using PlayGround.System.Aoe;
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
        [SerializeField] private Transform attacksRoot;
        [SerializeField] private float moveSpeed = 7f;
        [SerializeField] private float stopThreshold = 0.1f;
        [SerializeField] private float accelerationMultiplier = 8f;
        [SerializeField] private float frictionMultiplier = 6f;
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float dashCooldown = 0.45f;
        [SerializeField] private int maxAttackCount = 8;
        [SerializeField] private float maxHealth = 500f;
        [SerializeField] private float hurtFlashSeconds = 0.08f;
        [SerializeField] private float targetRadius = 0.45f;

        private InputAction moveAction;
        private InputAction attackAction;
        private InputAction dashAction;
        private PlayerMovement movement;
        private PlayerFacing facing;
        private PlayerAttackLoadout loadout;
        private PlayerAnimatorDriver animatorDriver;
        private PlayerStateDriver stateDriver;
        private PlayerHealth health;
        private static int nextTargetId;
        private ProjectileTargetRegistry registry;
        private AoeTargetRegistry aoeRegistry;
        private int targetId;

        public Vector2 AimDirection => facing?.AimDirection ?? Vector2.right;
        public int TargetId => targetId;
        public Vector2 ProjectileTargetPosition => ProjectileTargetShapeUtility.Position(hurtbox, transform);
        public float ProjectileTargetRadius => ProjectileTargetShapeUtility.Radius(hurtbox, targetRadius);
        public Vector2 ProjectileTargetHalfExtents => ProjectileTargetShapeUtility.HalfExtents(hurtbox, targetRadius);
        public float ProjectileTargetRotationRadians => ProjectileTargetShapeUtility.RotationRadians(hurtbox);
        public ProjectileShapeType ProjectileTargetShapeType => ProjectileTargetShapeUtility.ShapeType(hurtbox);
        public int ProjectileTargetMask => 1 << hurtbox.gameObject.layer;
        public bool IsProjectileTargetActive => isActiveAndEnabled && health != null && health.IsAlive;
        public Vector2 AoeTargetPosition => ProjectileTargetPosition;
        public float AoeTargetRadius => ProjectileTargetRadius;
        public Vector2 AoeTargetHalfExtents => ProjectileTargetHalfExtents;
        public float AoeTargetRotationRadians => ProjectileTargetRotationRadians;
        public ProjectileShapeType AoeTargetShapeType => ProjectileTargetShapeType;
        public int AoeTargetMask => ProjectileTargetMask;
        public bool IsAoeTargetActive => IsProjectileTargetActive;
        public float CurrentHealth => health?.CurrentHealth ?? 0f;
        public int EquippedAttackCount => loadout?.AttackCount ?? 0;

        private void Awake()
        {
            if (inputActions == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs InputSystem_Actions.");
            }

            if (body == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a Rigidbody2D.");
            }

            if (bodyCollider == null)
            {
                bodyCollider = body.GetComponent<Collider2D>();
            }

            if (hurtbox == null)
            {
                hurtbox = bodyCollider;
            }

            if (spriteRenderer == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a SpriteRenderer.");
            }

            if (worldCamera == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a world camera.");
            }

            InputActionMap playerMap = inputActions.FindActionMap("Player", true);
            moveAction = playerMap.FindAction("Move", true);
            attackAction = playerMap.FindAction("Attack", true);
            dashAction = playerMap.FindAction("Jump", true);
            playerMap.Enable();

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
            Transform attackSearchRoot = attacksRoot != null ? attacksRoot : transform;
            loadout = new PlayerAttackLoadout(
                attackSearchRoot.GetComponentsInChildren<ProjectileAttack>(true),
                attackSearchRoot.GetComponentsInChildren<AoeAttack>(true),
                maxAttackCount);

            if (loadout.AttackCount == 0)
            {
                throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs at least one child ProjectileAttack.");
            }
        }

        private void OnDisable()
        {
            registry?.Unregister(this);
            aoeRegistry?.Unregister(this);
        }

        private void Update()
        {
            if (!health.IsAlive)
            {
                return;
            }

            Vector2 move = moveAction.ReadValue<Vector2>();
            Vector2 mouseWorldPosition = GetMouseWorldPosition();
            movement.SetMoveInput(move);
            if (dashAction.WasPressedThisFrame())
            {
                movement.TryStartDash(mouseWorldPosition);
            }

            facing.AimAt(mouseWorldPosition);
            loadout.TickHeldFire(attackAction.IsPressed(), facing.AimDirection, mouseWorldPosition);
            animatorDriver.Tick(Time.deltaTime);
            stateDriver.Tick();
        }

        private void FixedUpdate()
        {
            if (!health.IsAlive)
            {
                return;
            }

            movement.FixedTick();
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

        public void ReceiveAoeHit(DamageSnapshot damage)
        {
            health.TakeDamage(damage);
        }

        private Vector2 GetMouseWorldPosition()
        {
            if (Mouse.current == null)
            {
                return (Vector2)transform.position + AimDirection;
            }

            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -worldCamera.transform.position.z));
            return world;
        }
    }
}
