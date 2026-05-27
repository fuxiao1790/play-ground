using Godot;

namespace PlayGround.Mob.Behaviours;

[GlobalClass]
public partial class BobAndWeaveBehaviour : MobBehaviour
{
    [Export] public float weave_strength = 0.75f;
    [Export] public float weave_frequency = 5.0f;

    private float _time;

    public override StringName Key => "bob_and_weave";

    public override bool SupportsState(MobBehaviourState state)
    {
        return state == MobBehaviourState.Chase;
    }

    public override Vector2 Update(double delta, Mob mob, MobBlackboard blackboard, MobBehaviourState state)
    {
        if (!blackboard.HasValidTarget())
        {
            return Vector2.Zero;
        }

        _time += (float)delta;
        Vector2 toTarget = blackboard.target!.GlobalPosition - mob.GlobalPosition;
        if (toTarget.IsZeroApprox())
        {
            return Vector2.Zero;
        }

        Vector2 forward = toTarget.Normalized();
        Vector2 side = forward.Orthogonal() * Mathf.Sin(_time * weave_frequency) * weave_strength;
        return (forward + side).Normalized() * mob.speed;
    }
}
