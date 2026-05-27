using UnityEngine;

namespace PlayGround.Player
{
    public sealed class PlayerMovement
    {
        private readonly Rigidbody2D body;
        private readonly float speed;
        private readonly float stopThreshold;
        private readonly float accelerationMultiplier;
        private readonly float frictionMultiplier;
        private readonly float dashSpeed;
        private readonly float dashDuration;
        private readonly float dashCooldown;
        private Vector2 moveInput;
        private Vector2 dashDirection;
        private float dashTimer;
        private float dashCooldownTimer;

        public PlayerMovement(
            Rigidbody2D body,
            float speed,
            float stopThreshold = 0.1f,
            float accelerationMultiplier = 8f,
            float frictionMultiplier = 6f,
            float dashSpeed = 14f,
            float dashDuration = 0.16f,
            float dashCooldown = 0.45f)
        {
            this.body = body;
            this.speed = Mathf.Max(0f, speed);
            this.stopThreshold = Mathf.Max(0f, stopThreshold);
            this.accelerationMultiplier = Mathf.Max(0f, accelerationMultiplier);
            this.frictionMultiplier = Mathf.Max(0f, frictionMultiplier);
            this.dashSpeed = Mathf.Max(dashSpeed, this.speed);
            this.dashDuration = Mathf.Max(0.01f, dashDuration);
            this.dashCooldown = Mathf.Max(dashCooldown, this.dashDuration);
        }

        public Vector2 MoveInput => moveInput;
        public bool IsDashing => dashTimer > 0f;
        public bool IsIdle => body.linearVelocity.magnitude <= stopThreshold;
        public float DashCooldownRemaining => dashCooldownTimer;

        public void SetMoveInput(Vector2 input)
        {
            moveInput = Vector2.ClampMagnitude(input, 1f);
        }

        public void TryStartDash(Vector2 aimWorldPosition)
        {
            if (IsDashing || dashCooldownTimer > 0f)
            {
                return;
            }

            dashDirection = GetDashDirection(aimWorldPosition);
            dashTimer = dashDuration;
            dashCooldownTimer = dashCooldown;
        }

        public void FixedTick()
        {
            float deltaTime = Time.fixedDeltaTime;
            dashTimer = Mathf.Max(0f, dashTimer - deltaTime);
            dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - deltaTime);

            if (IsDashing)
            {
                body.linearVelocity = dashDirection * dashSpeed;
                return;
            }

            dashDirection = Vector2.zero;
            Vector2 desiredVelocity = moveInput * speed;
            float maxDelta = speed * (moveInput.sqrMagnitude > 0f ? accelerationMultiplier : frictionMultiplier) * deltaTime;
            body.linearVelocity = Vector2.MoveTowards(body.linearVelocity, desiredVelocity, maxDelta);
            body.linearVelocity = Vector2.ClampMagnitude(body.linearVelocity, speed);

            if (body.linearVelocity.magnitude < stopThreshold)
            {
                body.linearVelocity = Vector2.zero;
            }
        }

        private Vector2 GetDashDirection(Vector2 aimWorldPosition)
        {
            if (moveInput.sqrMagnitude > 0f)
            {
                return moveInput.normalized;
            }

            if (body.linearVelocity.magnitude > stopThreshold)
            {
                return body.linearVelocity.normalized;
            }

            Vector2 aimOffset = aimWorldPosition - body.position;
            return aimOffset.sqrMagnitude <= 0.0001f ? Vector2.right : aimOffset.normalized;
        }
    }
}
