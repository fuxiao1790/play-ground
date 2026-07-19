using UnityEngine;

namespace PlayGround.Player
{
    public sealed class PlayerFacing
    {
        private const float AxisDeadzone = 0.05f;

        private readonly Transform facingRoot;
        private readonly SpriteRenderer spriteRenderer;
        private readonly SpriteFacingSet facingSet;
        private Vector2 aimDirection = Vector2.right;
        private bool facingUp;
        private bool facingRight = true;

        public PlayerFacing(Transform facingRoot, SpriteRenderer spriteRenderer, SpriteFacingSet facingSet)
        {
            this.facingRoot = facingRoot;
            this.spriteRenderer = spriteRenderer;
            this.facingSet = facingSet;
            spriteRenderer.flipX = false;
            spriteRenderer.sprite = facingSet.Resolve(facingUp, facingRight);
        }

        public Vector2 AimDirection => aimDirection;

        public void AimAt(Vector2 worldPosition)
        {
            Vector2 fromPlayer = worldPosition - (Vector2)facingRoot.position;
            if (fromPlayer.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            aimDirection = fromPlayer.normalized;

            bool previousUp = facingUp;
            bool previousRight = facingRight;

            if (Mathf.Abs(aimDirection.x) >= AxisDeadzone)
                facingRight = aimDirection.x > 0f;

            if (Mathf.Abs(aimDirection.y) >= AxisDeadzone)
                facingUp = aimDirection.y > 0f;

            if (facingUp != previousUp || facingRight != previousRight)
                spriteRenderer.sprite = facingSet.Resolve(facingUp, facingRight);
        }
    }
}
