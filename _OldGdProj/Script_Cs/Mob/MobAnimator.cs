using Godot;

namespace PlayGround.Mob;

public sealed class MobAnimator
{
    public enum State
    {
        Idle,
        Walk,
        Hurt,
    }

    public const int PriorityLocomotion = 0;
    public const int PriorityHurt = 100;
    private const string AnimIdle = "idle";
    private const string AnimWalk = "walk";
    private const string AnimHurt = "hurt";

    private readonly AnimatedSprite2D _sprite;
    private int _currentPriority;

    public MobAnimator(AnimatedSprite2D sprite)
    {
        _sprite = sprite;
        _sprite.AnimationFinished += OnAnimationFinished;
        Play(AnimIdle);
    }

    public void Request(State state, int priority = PriorityLocomotion)
    {
        switch (state)
        {
            case State.Idle:
                PlayWithPriority(AnimIdle, priority);
                break;
            case State.Walk:
                PlayWithPriority(AnimWalk, priority);
                break;
            case State.Hurt:
                PlayWithPriority(AnimHurt, priority);
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
        if (_sprite.Animation == AnimHurt)
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
