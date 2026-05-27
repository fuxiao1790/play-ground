using Godot;

namespace PlayGround.Mob.Behaviours;

[GlobalClass]
public partial class WanderBehaviour : MobBehaviour
{
    [Export] public float change_interval = 1.4f;
    [Export] public float speed_scale = 0.65f;

    private Vector2 _direction = Vector2.Zero;
    private float _remaining;

    public override StringName Key => "wander";

    public override bool SupportsState(MobBehaviourState state)
    {
        return state == MobBehaviourState.Wander;
    }

    public override Vector2 Update(double delta, Mob mob, MobBlackboard blackboard, MobBehaviourState state)
    {
        _remaining -= (float)delta;
        if (_remaining <= 0.0f)
        {
            _remaining = (float)GD.RandRange(change_interval * 0.5f, change_interval * 1.5f);
            _direction = Vector2.Right.Rotated(GD.Randf() * Mathf.Tau);
        }

        return _direction * mob.speed * speed_scale;
    }
}
