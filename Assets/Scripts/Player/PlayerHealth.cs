using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Player
{
    public sealed class PlayerHealth
    {
        private readonly Rigidbody2D body;
        private readonly Collider2D bodyCollider;
        private readonly Collider2D hurtbox;
        private readonly SpriteRenderer spriteRenderer;
        private readonly PlayerAnimatorDriver animatorDriver;
        private readonly float hurtFlashSeconds;

        public PlayerHealth(
            Rigidbody2D body,
            Collider2D bodyCollider,
            Collider2D hurtbox,
            SpriteRenderer spriteRenderer,
            PlayerAnimatorDriver animatorDriver,
            float maxHealth,
            float hurtFlashSeconds)
        {
            this.body = body;
            this.bodyCollider = bodyCollider;
            this.hurtbox = hurtbox;
            this.spriteRenderer = spriteRenderer;
            this.animatorDriver = animatorDriver;
            this.hurtFlashSeconds = Mathf.Max(0f, hurtFlashSeconds);
            MaxHealth = Mathf.Max(1f, maxHealth);
            CurrentHealth = MaxHealth;
        }

        public float MaxHealth { get; }
        public float CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0f;

        public bool TakeDamage(DamageSnapshot damage)
        {
            if (!IsAlive || damage.Amount <= 0f)
            {
                return false;
            }

            animatorDriver.RequestHurt(hurtFlashSeconds);
            return true;
        }

        public void MirrorCombatHealth(float currentHealth, bool requestHurt)
        {
            if (!IsAlive)
            {
                return;
            }

            CurrentHealth = currentHealth;
            if (CurrentHealth <= 0f)
            {
                SoftDie();
                return;
            }

            if (requestHurt)
            {
                animatorDriver.RequestHurt(hurtFlashSeconds);
            }
        }

        private void SoftDie()
        {
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
        }
    }
}
