using PlayGround.Common;

namespace PlayGround.Player;

public sealed class PlayerStateDriver
{
    private readonly StateMachineCore _stateMachine;
    private readonly PlayerMovement _movement;

    public PlayerStateDriver(StateMachineCore stateMachine, PlayerMovement movement)
    {
        _stateMachine = stateMachine;
        _movement = movement;
    }

    public void Update()
    {
        _stateMachine.TransitionTo((int)DesiredState());
    }

    private PlayerAnimator.State DesiredState()
    {
        if (_movement.IsDashing())
        {
            return PlayerAnimator.State.Dashing;
        }

        return _movement.IsIdle() ? PlayerAnimator.State.Idle : PlayerAnimator.State.Moving;
    }
}
