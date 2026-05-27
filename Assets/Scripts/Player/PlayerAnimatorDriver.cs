using UnityEngine;

namespace PlayGround.Player
{
    public enum PlayerAnimationState
    {
        Idle,
        Moving,
        Dashing,
        Staggered
    }

    public sealed class PlayerAnimatorDriver
    {
        public const string IdleState = "idle";
        public const string WalkState = "walk";
        public const string DashState = "jump";
        public const string StaggerState = "stagger";

        private readonly Animator animator;
        private readonly SpriteRenderer spriteRenderer;
        private readonly Color baseColor;
        private PlayerAnimationState currentState = PlayerAnimationState.Idle;
        private float staggerTimer;

        public PlayerAnimatorDriver(Animator animator, SpriteRenderer spriteRenderer)
        {
            this.animator = animator;
            this.spriteRenderer = spriteRenderer;
            baseColor = spriteRenderer.color;
            Play(IdleState);
        }

        public PlayerAnimationState CurrentState => staggerTimer > 0f ? PlayerAnimationState.Staggered : currentState;

        public void Request(PlayerAnimationState state)
        {
            if (staggerTimer > 0f && state != PlayerAnimationState.Staggered)
            {
                return;
            }

            if (currentState == state)
            {
                return;
            }

            currentState = state;
            Play(AnimationName(state));
        }

        public void RequestHurt(float duration)
        {
            staggerTimer = Mathf.Max(staggerTimer, duration);
            currentState = PlayerAnimationState.Staggered;
            spriteRenderer.color = Color.Lerp(baseColor, Color.white, 0.65f);
            Play(StaggerState);
        }

        public void Tick(float deltaTime)
        {
            if (staggerTimer <= 0f)
            {
                return;
            }

            staggerTimer = Mathf.Max(0f, staggerTimer - deltaTime);
            if (staggerTimer <= 0f)
            {
                spriteRenderer.color = baseColor;
            }
        }

        private void Play(string stateName)
        {
            if (animator != null)
            {
                animator.Play(stateName);
            }
        }

        private static string AnimationName(PlayerAnimationState state)
        {
            return state switch
            {
                PlayerAnimationState.Moving => WalkState,
                PlayerAnimationState.Dashing => DashState,
                PlayerAnimationState.Staggered => StaggerState,
                _ => IdleState
            };
        }
    }
}
