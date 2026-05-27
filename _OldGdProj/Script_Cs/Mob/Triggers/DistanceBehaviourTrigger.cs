using Godot;

namespace PlayGround.Mob.Triggers;

[GlobalClass]
public partial class DistanceBehaviourTrigger : MobTrigger
{
    [Export] public float keep_distance_radius = 52.0f;

    public override void Update(double delta, Mob mob, MobBlackboard blackboard, MobEventQueue events)
    {
        if (!blackboard.target_visible || !blackboard.HasValidTarget())
        {
            return;
        }

        float distance = mob.GlobalPosition.DistanceTo(blackboard.target!.GlobalPosition);
        if (distance < keep_distance_radius)
        {
            blackboard.RequestTrigger("on_close_to_target", 20);
        }
    }
}
