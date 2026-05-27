using UnityEngine;

namespace PlayGround.Player
{
    public sealed class PlayerFacing
    {
        private readonly Transform facingRoot;
        private readonly SpriteRenderer spriteRenderer;
        private Vector2 aimDirection = Vector2.right;

        public PlayerFacing(Transform facingRoot, SpriteRenderer spriteRenderer)
        {
            this.facingRoot = facingRoot;
            this.spriteRenderer = spriteRenderer;
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
            spriteRenderer.flipX = aimDirection.x < 0f;
        }
    }
}
