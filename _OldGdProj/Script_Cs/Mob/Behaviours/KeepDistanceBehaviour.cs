using Godot;

namespace PlayGround.Mob.Behaviours;

[GlobalClass]
public partial class KeepDistanceBehaviour : MobBehaviour
{
    [Export] public float preferred_distance = 72.0f;
    [Export] public float tolerance = 12.0f;

    public override StringName Key => "keep_distance";

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

        Vector2 fromTarget = mob.GlobalPosition - blackboard.target!.GlobalPosition;
        float distance = fromTarget.Length();
        if (distance <= 0.001f)
        {
            return Vector2.Right * mob.speed;
        }
        if (distance < preferred_distance - tolerance)
        {
            return fromTarget.Normalized() * mob.speed;
        }
        if (distance > preferred_distance + tolerance)
        {
            return -fromTarget.Normalized() * mob.speed;
        }
        return Vector2.Zero;
    }
}
