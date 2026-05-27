using Godot;

namespace PlayGround.Mob.Behaviours;

[GlobalClass]
public partial class SwarmTargetBehaviour : MobBehaviour
{
    public override StringName Key => "swarm_target";

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

        Vector2 toTarget = blackboard.target!.GlobalPosition - mob.GlobalPosition;
        return toTarget.IsZeroApprox() ? Vector2.Zero : toTarget.Normalized() * mob.speed;
    }
}
