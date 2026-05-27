using Godot;

namespace PlayGround.Player;

public sealed class PlayerAnimator
{
    public enum State
    {
        Idle,
        Moving,
        Dashing,
        Staggered,
    }

    public const string AnimIdle = "idle";
    public const string AnimWalk = "walk";
    public const string AnimDash = "jump";
    public const string AnimStagger = "stagger";

    private readonly AnimatedSprite2D _sprite;
    private int _currentPriority;

    public PlayerAnimator(AnimatedSprite2D sprite)
    {
        _sprite = sprite;
        _sprite.AnimationFinished += OnAnimationFinished;
        Play(AnimIdle);
    }

    public void Request(State state, int priority = 0)
    {
        switch (state)
        {
            case State.Idle:
                PlayWithPriority(AnimIdle, priority);
                break;
            case State.Moving:
                PlayWithPriority(AnimWalk, priority);
                break;
            case State.Dashing:
                PlayWithPriority(AnimDash, priority);
                break;
            case State.Staggered:
                PlayWithPriority(AnimStagger, priority);
                break;
        }
    }

    private void PlayWithPriority(string animationName, int priority)
    {
        if (priority < _currentPriority)
        {
            return;
        }

        _currentPriority = priority;
        Play(animationName);
    }

    private void OnAnimationFinished()
    {
        if (_sprite.Animation == AnimStagger)
        {
            _currentPriority = 0;
            Play(AnimIdle);
        }
    }

    private void Play(string animationName)
    {
        if (_sprite.Animation == animationName && _sprite.IsPlaying())
        {
            return;
        }

        _sprite.Play(animationName);
    }
}
