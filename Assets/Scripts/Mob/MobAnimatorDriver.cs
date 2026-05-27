using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobAnimatorDriver
    {
        private readonly Animator animator;
        private readonly SpriteRenderer spriteRenderer;
        private readonly Color baseColor;
        private float hurtRemaining;

        public MobAnimatorDriver(Animator animator, SpriteRenderer spriteRenderer)
        {
            this.animator = animator;
            this.spriteRenderer = spriteRenderer;
            baseColor = spriteRenderer.color;
        }

        public void RequestState(MobBehaviourState state)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetInteger("MobState", (int)state);
            animator.SetBool("Moving", state is MobBehaviourState.Wander or MobBehaviourState.Chase);
        }

        public void RequestHurt(float seconds)
        {
            hurtRemaining = Mathf.Max(hurtRemaining, seconds);
            spriteRenderer.color = Color.Lerp(baseColor, Color.white, 0.65f);
            RequestState(MobBehaviourState.Hurt);
        }

        public void Tick(float deltaTime)
        {
            if (hurtRemaining <= 0f)
            {
                return;
            }

            hurtRemaining = Mathf.Max(0f, hurtRemaining - deltaTime);
            if (hurtRemaining <= 0f)
            {
                spriteRenderer.color = baseColor;
            }
        }
    }
}
