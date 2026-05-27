using Godot;

namespace PlayGround.Mob.Behaviours;

[GlobalClass]
public partial class PanicBehaviour : MobBehaviour
{
    [Export] public float speed_scale = 1.2f;
    [Export] public float jitter_strength = 0.35f;

    public override StringName Key => "panic";

    public override bool SupportsState(MobBehaviourState state)
    {
        return state is MobBehaviourState.Wander or MobBehaviourState.Chase;
    }

    public override Vector2 Update(double delta, Mob mob, MobBlackboard blackboard, MobBehaviourState state)
    {
        Vector2 direction = Vector2.Right.Rotated(GD.Randf() * Mathf.Tau) * jitter_strength;
        if (blackboard.HasValidTarget())
        {
            Vector2 away = mob.GlobalPosition - blackboard.target!.GlobalPosition;
            if (!away.IsZeroApprox())
            {
                direction += away.Normalized();
            }
        }

        return direction.IsZeroApprox() ? Vector2.Zero : direction.Normalized() * mob.speed * speed_scale;
    }
}
