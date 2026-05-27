namespace PlayGround.Player
{
    public sealed class PlayerStateDriver
    {
        private readonly PlayerMovement movement;
        private readonly PlayerAnimatorDriver animatorDriver;

        public PlayerStateDriver(PlayerMovement movement, PlayerAnimatorDriver animatorDriver)
        {
            this.movement = movement;
            this.animatorDriver = animatorDriver;
        }

        public void Tick()
        {
            if (movement.IsDashing)
            {
                animatorDriver.Request(PlayerAnimationState.Dashing);
                return;
            }

            animatorDriver.Request(movement.IsIdle ? PlayerAnimationState.Idle : PlayerAnimationState.Moving);
        }
    }
}
